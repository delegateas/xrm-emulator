using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
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
        baseUrl = baseUrl.TrimEnd('/');
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

        if (cancellationToken.IsCancellationRequested) return;

        logger.LogInformation("XRM Emulator {Name}: running seed data.", resourceName);

        var tokenUrl = $"{baseUrl}/organizations/oauth2/v2.0/token";
        var serviceClient = new ServiceClient(
            new Uri(baseUrl),
            async _ =>
            {
                var resp = await http.PostAsync(
                    tokenUrl,
                    new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "client_credentials"),
                        new KeyValuePair<string, string>("client_id", "fake-client-id-1"),
                        new KeyValuePair<string, string>("client_secret", "fake-secret"),
                    }));
                resp.EnsureSuccessStatusCode();
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(
                    await resp.Content.ReadAsStreamAsync());
                return doc.RootElement.GetProperty("access_token").GetString()!;
            });

        try
        {
            foreach (var ann in annotations)
                await ann.SeedAction(serviceClient);

            var saveResponse = await http.PostAsync($"{baseUrl}/api/snapshot/save", content: null, cancellationToken);
            if (saveResponse.IsSuccessStatusCode)
                logger.LogInformation("XRM Emulator {Name}: seed complete, snapshot saved.", resourceName);
            else
                logger.LogWarning("XRM Emulator {Name}: seed complete, but snapshot save returned {StatusCode}.", resourceName, saveResponse.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "XRM Emulator {Name}: seed failed.", resourceName);
        }
    }
}
#pragma warning restore CS0618
