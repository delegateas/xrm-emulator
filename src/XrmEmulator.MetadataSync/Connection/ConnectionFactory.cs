using Microsoft.PowerPlatform.Dataverse.Client;
using Spectre.Console;

#pragma warning disable SYSLIB0014 // ServicePointManager is obsolete but still functional for connection tuning

namespace XrmEmulator.MetadataSync.Connection;

public static class ConnectionFactory
{
    public static async Task<ServiceClient> CreateAsync(ConnectionSettings settings)
    {
        ApplyPerformanceOptimizations();

        if (settings.AuthMode == AuthMode.InteractiveBrowser)
        {
            return await CreateWithOAuthAsync(settings);
        }

        var connectionString = settings.AuthMode switch
        {
            AuthMode.ConnectionString => settings.ConnectionString
                ?? throw new InvalidOperationException("Connection string is required for ConnectionString auth mode."),

            AuthMode.ClientSecret =>
                $"AuthType=ClientSecret;Url={settings.Url};ClientId={settings.ClientId};ClientSecret={settings.ClientSecret};TenantId={settings.TenantId}",

            _ => throw new ArgumentOutOfRangeException(nameof(settings.AuthMode), settings.AuthMode, "Unsupported auth mode.")
        };

        AnsiConsole.MarkupLine("[grey]Connecting to Dataverse...[/]");

        var client = new ServiceClient(connectionString)
        {
            UseWebApi = true,
            EnableAffinityCookie = false
        };

        if (!client.IsReady)
        {
            throw new InvalidOperationException(
                $"Failed to connect to Dataverse: {client.LastError}");
        }

        AnsiConsole.MarkupLine("[green]Connected successfully.[/]");
        return client;
    }

    private static async Task<ServiceClient> CreateWithOAuthAsync(ConnectionSettings settings)
    {
        var tokenProvider = new OAuthTokenProvider(
            settings.Url,
            settings.ClientId ?? ConnectionSettings.MicrosoftPublicClientId,
            settings.NoCache);

        await tokenProvider.AuthenticateAsync();

        AnsiConsole.MarkupLine("[grey]Connecting to Dataverse...[/]");

        var client = new ServiceClient(
            new Uri(settings.Url),
            tokenProvider.GetTokenAsync)
        {
            UseWebApi = true,
            EnableAffinityCookie = false
        };

        if (!client.IsReady)
        {
            throw new InvalidOperationException(
                $"Failed to connect to Dataverse: {client.LastError}");
        }

        AnsiConsole.MarkupLine("[green]Connected successfully.[/]");
        return client;
    }

    private static void ApplyPerformanceOptimizations()
    {
        System.Net.ServicePointManager.DefaultConnectionLimit = 65000;
        System.Net.ServicePointManager.Expect100Continue = false;
        System.Net.ServicePointManager.UseNagleAlgorithm = false;
        ThreadPool.SetMinThreads(100, 100);
    }
}

#pragma warning restore SYSLIB0014
