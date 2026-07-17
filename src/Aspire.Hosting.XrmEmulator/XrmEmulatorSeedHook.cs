using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace Aspire.Hosting;

#pragma warning disable CA1812 // instantiated via DI

internal sealed class XrmEmulatorSeedDataAnnotation(Func<IOrganizationService, Task> seedAction) : IResourceAnnotation
{
    public Func<IOrganizationService, Task> SeedAction { get; } = seedAction;
}

#pragma warning disable CS0618 // IDistributedApplicationLifecycleHook is obsolete; keep until eventing API stabilises
internal sealed class XrmEmulatorSeedHook(ILogger<XrmEmulatorSeedHook> logger)
    : IDistributedApplicationLifecycleHook
{
    public Task BeforeStartAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task AfterEndpointsAllocatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
    {
        foreach (var resource in appModel.Resources)
        {
            var annotations = resource.Annotations.OfType<XrmEmulatorSeedDataAnnotation>().ToList();
            if (annotations.Count == 0) continue;

            // Get the allocated HTTP endpoint URL from EndpointAnnotation.AllocatedEndpoint
            var endpointAnnotation = resource.Annotations
                .OfType<EndpointAnnotation>()
                .FirstOrDefault(a => a.Name == "http");
            var url = endpointAnnotation?.AllocatedEndpoint?.UriString;

            if (url is null)
            {
                logger.LogWarning("XRM Emulator {Name}: no allocated HTTP endpoint; seed skipped.", resource.Name);
                try { File.AppendAllText("/tmp/booking-seed-diagnostics.log", $"[XrmEmulatorSeedHook] {resource.Name}: no allocated HTTP endpoint; seed skipped.\n"); } catch { }
                continue;
            }

            _ = RunSeedAsync(resource.Name, url, annotations, cancellationToken);
        }
        return Task.CompletedTask;
    }

    public Task AfterResourcesCreatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private async Task RunSeedAsync(
        string resourceName,
        string baseUrl,
        IList<XrmEmulatorSeedDataAnnotation> annotations,
        CancellationToken cancellationToken)
    {
        const string diagPath = "/tmp/booking-seed-diagnostics.log";
        void DiagLog(string message)
        {
            try { File.AppendAllText(diagPath, message + "\n"); } catch { }
        }

        baseUrl = baseUrl.TrimEnd('/');
        DiagLog($"[XrmEmulatorSeedHook] RunSeedAsync starting for {resourceName}, baseUrl={baseUrl}, annotationCount={annotations.Count}");
        using var http = new HttpClient();

        // Poll /health until the emulator is ready (up to 120 s)
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var probe = await http.GetAsync($"{baseUrl}/health", cancellationToken);
                if (probe.IsSuccessStatusCode) break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            DiagLog("[XrmEmulatorSeedHook] Cancelled before health check completed.");
            return;
        }

        DiagLog($"[XrmEmulatorSeedHook] Health check passed for {baseUrl}. Running seed data.");
        logger.LogInformation("XRM Emulator {Name}: running seed data.", resourceName);

        var tokenUrl = $"{baseUrl}/organizations/oauth2/v2.0/token";
        ServiceClient CreateServiceClient() => new(
            new Uri(baseUrl),
            async _ =>
            {
                DiagLog($"[XrmEmulatorSeedHook] Requesting token from {tokenUrl}");
                var resp = await http.PostAsync(
                    tokenUrl,
                    new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "client_credentials"),
                        new KeyValuePair<string, string>("client_id", "fake-client-id-1"),
                        new KeyValuePair<string, string>("client_secret", "fake-secret"),
                    }));
                DiagLog($"[XrmEmulatorSeedHook] Token endpoint responded {(int)resp.StatusCode} {resp.StatusCode}");
                resp.EnsureSuccessStatusCode();
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(
                    await resp.Content.ReadAsStreamAsync());
                var token = doc.RootElement.GetProperty("access_token").GetString()!;
                DiagLog($"[XrmEmulatorSeedHook] Token acquired, length={token.Length}");
                return token;
            });

        // Each annotation gets its own freshly connected ServiceClient and is isolated in its own
        // try/catch: one seed action throwing (e.g. a duplicate-key conflict against records
        // already restored from a prior snapshot) must not prevent the other registered seed
        // actions from running.
        //
        // The very first organization-service call made against a newly (re)started emulator can
        // fail with "Connection refused (localhost:80)" deterministically (reproduced 3/3 times
        // even with a fresh ServiceClient and a 2s delay between attempts — not a timing race).
        // This looks like a known ServiceClient quirk where the WCF channel factory isn't fully
        // initialized until after its first (failing) call. Warm up each fresh client with a
        // throwaway WhoAmIRequest, retried on the SAME client instance, before handing it to the
        // seed action.
        const int maxWarmupAttempts = 5;
        foreach (var ann in annotations)
        {
            try
            {
                DiagLog($"[XrmEmulatorSeedHook] Creating ServiceClient for annotation against {baseUrl}...");
                using var serviceClient = CreateServiceClient();
                DiagLog("[XrmEmulatorSeedHook] ServiceClient constructor returned. Starting warm-up.");

                for (var attempt = 1; attempt <= maxWarmupAttempts; attempt++)
                {
                    try
                    {
                        serviceClient.Execute(new WhoAmIRequest());
                        DiagLog($"[XrmEmulatorSeedHook] Warm-up WhoAmIRequest succeeded on attempt {attempt}.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        DiagLog($"[XrmEmulatorSeedHook] Warm-up WhoAmIRequest failed on attempt {attempt}/{maxWarmupAttempts}: {ex}");
                        if (attempt < maxWarmupAttempts)
                        {
                            logger.LogWarning(ex, "XRM Emulator {Name}: warm-up WhoAmIRequest failed on attempt {Attempt}/{Attempts}; retrying on the same connection.", resourceName, attempt, maxWarmupAttempts);
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        }
                    }
                }

                DiagLog("[XrmEmulatorSeedHook] Calling ann.SeedAction now.");
                await ann.SeedAction(serviceClient);
                DiagLog("[XrmEmulatorSeedHook] ann.SeedAction returned successfully.");
            }
            catch (Exception ex)
            {
                DiagLog($"[XrmEmulatorSeedHook] Seed action failed: {ex}");
                logger.LogError(ex, "XRM Emulator {Name}: a seed action failed; continuing with remaining seed actions.", resourceName);
            }
        }

        try
        {
            var saveResponse = await http.PostAsync($"{baseUrl}/api/snapshot/save", content: null, cancellationToken);
            if (saveResponse.IsSuccessStatusCode)
                logger.LogInformation("XRM Emulator {Name}: seed complete, snapshot saved.", resourceName);
            else
                logger.LogWarning("XRM Emulator {Name}: seed complete, but snapshot save returned {StatusCode}.", resourceName, saveResponse.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "XRM Emulator {Name}: snapshot save failed.", resourceName);
        }
    }
}
#pragma warning restore CS0618
