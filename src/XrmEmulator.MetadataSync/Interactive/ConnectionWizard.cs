using Microsoft.Extensions.Configuration;
using Spectre.Console;
using XrmEmulator.MetadataSync.Connection;

namespace XrmEmulator.MetadataSync.Interactive;

public static class ConnectionWizard
{
    public static async Task<ConnectionSettings> RunAsync(IConfiguration configuration, bool noCache = false)
    {
        // Check if connection settings are already provided via configuration
        var configUrl = configuration["Url"] ?? configuration["DATAVERSE_URL"];
        var configAuthMode = configuration["AuthMode"];
        var configClientId = configuration["ClientId"] ?? configuration["DATAVERSE_CLIENT_ID"];
        var configClientSecret = configuration["ClientSecret"] ?? configuration["DATAVERSE_CLIENT_SECRET"];
        var configTenantId = configuration["TenantId"] ?? configuration["DATAVERSE_TENANT_ID"];
        var configConnectionString = configuration["ConnectionString"] ?? configuration["DATAVERSE_CONNECTION_STRING"];

        if (!string.IsNullOrEmpty(configConnectionString))
        {
            AnsiConsole.MarkupLine("[grey]Using connection string from configuration.[/]");
            return new ConnectionSettings
            {
                Url = configUrl ?? "from-connection-string",
                AuthMode = AuthMode.ConnectionString,
                ConnectionString = configConnectionString
            };
        }

        AnsiConsole.Write(new Rule("[bold blue]Dataverse Connection[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var authMode = SelectAuthMode(configAuthMode);

        return authMode switch
        {
            AuthMode.ConnectionString => PromptConnectionString(),
            AuthMode.ClientSecret => PromptClientSecret(configUrl, configClientId, configClientSecret, configTenantId),
            AuthMode.InteractiveBrowser => await PromptInteractiveBrowserAsync(configUrl, configClientId, noCache),
            _ => throw new ArgumentOutOfRangeException(nameof(authMode), authMode, null)
        };
    }

    private static AuthMode SelectAuthMode(string? preconfigured)
    {
        if (!string.IsNullOrEmpty(preconfigured) && Enum.TryParse<AuthMode>(preconfigured, ignoreCase: true, out var mode))
        {
            AnsiConsole.MarkupLine($"[grey]Using auth mode from configuration: {mode}[/]");
            return mode;
        }

        return AnsiConsole.Prompt(
            new SelectionPrompt<AuthMode>()
                .Title("Select [green]authentication method[/]:")
                .AddChoices(AuthMode.InteractiveBrowser, AuthMode.ClientSecret, AuthMode.ConnectionString)
                .UseConverter(mode => mode switch
                {
                    AuthMode.ClientSecret => "Client Secret (App Registration)",
                    AuthMode.InteractiveBrowser => "Interactive Browser (OAuth)",
                    AuthMode.ConnectionString => "Raw Connection String",
                    _ => mode.ToString()
                }));
    }

    private static ConnectionSettings PromptConnectionString()
    {
        var connectionString = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter [green]connection string[/]:")
                .Secret());

        return new ConnectionSettings
        {
            Url = "from-connection-string",
            AuthMode = AuthMode.ConnectionString,
            ConnectionString = connectionString
        };
    }

    private static ConnectionSettings PromptClientSecret(
        string? configUrl, string? configClientId, string? configClientSecret, string? configTenantId)
    {
        var url = configUrl ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Enter [green]Dataverse URL[/] (e.g. https://org.crm.dynamics.com):"));

        var tenantId = configTenantId ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Enter [green]Tenant ID[/]:"));

        var clientId = configClientId ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Enter [green]Client ID[/] (App Registration):"));

        var clientSecret = configClientSecret ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Enter [green]Client Secret[/]:")
                .Secret());

        return new ConnectionSettings
        {
            Url = url,
            TenantId = tenantId,
            ClientId = clientId,
            ClientSecret = clientSecret,
            AuthMode = AuthMode.ClientSecret
        };
    }

    private static async Task<ConnectionSettings> PromptInteractiveBrowserAsync(string? configUrl, string? configClientId, bool noCache = false)
    {
        var url = configUrl;

        if (string.IsNullOrEmpty(url))
        {
            url = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter [green]Dataverse URL[/] (or press [yellow]Enter[/] to discover environments):")
                    .AllowEmpty());
        }

        if (string.IsNullOrEmpty(url))
        {
            return await DiscoverAndSelectEnvironmentAsync(configClientId);
        }

        var clientId = configClientId ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Enter [green]Client ID[/] (App Registration, press Enter for Microsoft default):")
                .DefaultValue(ConnectionSettings.MicrosoftPublicClientId)
                .ShowDefaultValue());

        return new ConnectionSettings
        {
            Url = url,
            ClientId = clientId,
            AuthMode = AuthMode.InteractiveBrowser,
            NoCache = noCache
        };
    }

    private static async Task<ConnectionSettings> DiscoverAndSelectEnvironmentAsync(string? configClientId)
    {
        var discoveryClientId = configClientId ?? ConnectionSettings.MicrosoftPublicClientId;

        AnsiConsole.MarkupLine("[grey]Discovering environments via Global Discovery Service...[/]");

        var result = await EnvironmentDiscoveryService.DiscoverAsync(discoveryClientId);

        if (result.Environments.Count == 0)
            throw new InvalidOperationException(
                "No Dataverse environments found. Ensure your account has access to at least one environment.");

        AnsiConsole.WriteLine();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<DiscoveredEnvironment>()
                .Title($"Select a [green]Dataverse environment[/] ({result.Environments.Count} found):")
                .PageSize(15)
                .AddChoices(result.Environments)
                .UseConverter(env =>
                {
                    var name = Markup.Escape(env.FriendlyName);
                    var purpose = env.Purpose != null ? $" ({env.Purpose})" : "";
                    return $"{name}{purpose} ({Markup.Escape(env.TenantName)}) - {env.Url}";
                }));

        AnsiConsole.MarkupLine($"[green]Selected:[/] {selected.Url}");

        // Pre-cache the refresh token so OAuthTokenProvider can do a silent refresh
        TokenCache.Save(selected.Url, result.ClientId, selected.TenantId, result.RefreshToken);

        return new ConnectionSettings
        {
            Url = selected.Url,
            ClientId = result.ClientId,
            AuthMode = AuthMode.InteractiveBrowser
        };
    }
}
