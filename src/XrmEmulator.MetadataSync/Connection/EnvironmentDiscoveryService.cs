using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace XrmEmulator.MetadataSync.Connection;

public record DiscoveredEnvironment(
    string FriendlyName,
    string Url,
    string? Purpose,
    string? Version,
    string TenantName,
    string TenantId);

public record DiscoveredTenant(string TenantId, string DisplayName);

public record DiscoveryResult(List<DiscoveredEnvironment> Environments, string RefreshToken, string ClientId);

public static class EnvironmentDiscoveryService
{
    private const string GlobalDiscoveryUrl = "https://globaldisco.crm.dynamics.com/api/discovery/v2.0/Instances";
    private const string GlobalDiscoveryScope = "https://globaldisco.crm.dynamics.com/user_impersonation offline_access";
    private const string ArmScope = "https://management.azure.com/user_impersonation offline_access";
    private const string ArmTenantsUrl = "https://management.azure.com/tenants?api-version=2020-01-01";

    private const string DiscoveryCacheUrl = "https://discovery.global";

    public static async Task<DiscoveryResult> DiscoverAsync(string clientId)
    {
        // Step 1: Sign in to get ARM token + refresh token (try cache first)
        var (armAccessToken, refreshToken) = await AuthenticateWithCacheAsync(clientId);
        var tenants = await FetchTenantsAsync(armAccessToken);

        if (tenants.Count == 0)
            throw new InvalidOperationException("No tenants found for your account.");

        AnsiConsole.MarkupLine($"[green]Found {tenants.Count} tenant(s).[/]");
        AnsiConsole.WriteLine();

        // Step 2: Let user pick which tenants to query
        List<DiscoveredTenant> selectedTenants;
        if (tenants.Count == 1)
        {
            selectedTenants = tenants;
            AnsiConsole.MarkupLine($"[grey]Tenant: {Markup.Escape(tenants[0].DisplayName)}[/]");
        }
        else
        {
            selectedTenants = AnsiConsole.Prompt(
                new MultiSelectionPrompt<DiscoveredTenant>()
                    .Title("Select [green]tenants[/] to discover environments from:")
                    .PageSize(15)
                    .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                    .AddChoices(tenants)
                    .UseConverter(t => $"{Markup.Escape(t.DisplayName)} ({t.TenantId})"));

            if (selectedTenants.Count == 0)
                throw new InvalidOperationException("No tenants selected.");
        }

        // Step 3: For each tenant, exchange refresh token for GDS token (no browser needed)
        //         If that fails, fall back to interactive browser auth for that tenant
        var allEnvironments = new List<DiscoveredEnvironment>();
        var failedTenants = new List<DiscoveredTenant>();

        AnsiConsole.MarkupLine("[grey]Querying environments (silent token exchange)...[/]");
        foreach (var tenant in selectedTenants)
        {
            try
            {
                var gdsToken = await ExchangeRefreshTokenAsync(
                    clientId, tenant.TenantId, refreshToken, GlobalDiscoveryScope);
                var environments = await FetchEnvironmentsAsync(gdsToken, tenant);
                allEnvironments.AddRange(environments);
                AnsiConsole.MarkupLine(
                    $"  [green]{environments.Count}[/] environment(s) from {Markup.Escape(tenant.DisplayName)}");
            }
            catch
            {
                failedTenants.Add(tenant);
                AnsiConsole.MarkupLine(
                    $"  [yellow]![/] {Markup.Escape(tenant.DisplayName)} — needs interactive sign-in");
            }
        }

        // Offer interactive auth for tenants where silent exchange failed
        foreach (var tenant in failedTenants)
        {
            AnsiConsole.WriteLine();
            var retry = AnsiConsole.Confirm(
                $"Sign in to [blue]{Markup.Escape(tenant.DisplayName)}[/] via browser?", defaultValue: true);

            if (!retry)
                continue;

            try
            {
                var gdsToken = await AuthenticateInteractiveForTenantAsync(clientId, tenant.TenantId);
                var environments = await FetchEnvironmentsAsync(gdsToken, tenant);
                allEnvironments.AddRange(environments);
                AnsiConsole.MarkupLine(
                    $"  [green]{environments.Count}[/] environment(s) from {Markup.Escape(tenant.DisplayName)}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(
                    $"  [red]Failed[/] for {Markup.Escape(tenant.DisplayName)}: {Markup.Escape(ex.Message)}");
            }
        }

        return new DiscoveryResult(
            allEnvironments.OrderBy(e => e.FriendlyName).ToList(),
            refreshToken,
            clientId);
    }

    private static async Task<(string AccessToken, string RefreshToken)> AuthenticateWithCacheAsync(string clientId)
    {
        var cached = TokenCache.Load(DiscoveryCacheUrl, clientId);
        if (cached != null)
        {
            AnsiConsole.MarkupLine("[grey]Found cached discovery credentials, attempting silent refresh...[/]");
            try
            {
                using var http = new HttpClient();
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = cached.RefreshToken,
                    ["scope"] = ArmScope
                });

                var tokenEndpoint = $"https://login.microsoftonline.com/{cached.TenantId}/oauth2/v2.0/token";
                var response = await http.PostAsync(tokenEndpoint, content);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("Refresh failed");

                using var doc = JsonDocument.Parse(json);
                var accessToken = doc.RootElement.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("No access_token");
                var newRefreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt)
                    ? rt.GetString() ?? cached.RefreshToken
                    : cached.RefreshToken;

                TokenCache.Save(DiscoveryCacheUrl, clientId, cached.TenantId, newRefreshToken);
                AnsiConsole.MarkupLine("[green]Discovery authentication successful (cached).[/]");
                return (accessToken, newRefreshToken);
            }
            catch
            {
                AnsiConsole.MarkupLine("[yellow]Cached discovery token expired, falling back to browser sign-in...[/]");
                TokenCache.Clear(DiscoveryCacheUrl, clientId);
            }
        }

        AnsiConsole.MarkupLine("[grey]Signing in to discover your tenants...[/]");
        return await AuthenticateInteractiveAsync(clientId);
    }

    private static async Task<(string AccessToken, string RefreshToken)> AuthenticateInteractiveAsync(string clientId)
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);

        var port = GetAvailablePort();
        var redirectUri = $"http://localhost:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var authorizeUrl = "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(ArmScope)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            $"&code_challenge_method=S256" +
            $"&prompt=select_account";

        if (TryOpenBrowser(authorizeUrl))
        {
            AnsiConsole.MarkupLine("[grey]Browser opened. Waiting for sign-in...[/]");
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold yellow]Open this URL to sign in:[/]");
            AnsiConsole.WriteLine();
            Console.Out.WriteLine(authorizeUrl);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Waiting for authentication callback...[/]");
        }

        var context = await listener.GetContextAsync();
        var code = context.Request.QueryString["code"];
        var error = context.Request.QueryString["error"];

        var responseHtml = error == null
            ? "<html><body><h2>Authentication successful!</h2><p>You can close this tab.</p></body></html>"
            : $"<html><body><h2>Authentication failed</h2><p>{WebUtility.HtmlEncode(error)}</p></body></html>";

        var buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.Close();
        listener.Stop();

        if (string.IsNullOrEmpty(code))
        {
            var errorDescription = context.Request.QueryString["error_description"] ?? error ?? "Unknown error";
            throw new InvalidOperationException($"Authentication failed: {errorDescription}");
        }

        // Exchange code for tokens
        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = ArmScope
        });

        var response = await http.PostAsync(
            "https://login.microsoftonline.com/organizations/oauth2/v2.0/token", content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed: {json}");

        using var doc = JsonDocument.Parse(json);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("No access_token in response.");
        var refreshToken = doc.RootElement.GetProperty("refresh_token").GetString()
            ?? throw new InvalidOperationException("No refresh_token in response.");

        TokenCache.Save(DiscoveryCacheUrl, clientId, "organizations", refreshToken);
        return (accessToken, refreshToken);
    }

    /// <summary>
    /// Interactive PKCE browser auth against a specific tenant for GDS scope.
    /// Used as fallback when silent refresh token exchange fails.
    /// </summary>
    private static async Task<string> AuthenticateInteractiveForTenantAsync(string clientId, string tenantId)
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);

        var port = GetAvailablePort();
        var redirectUri = $"http://localhost:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var authorizeUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(GlobalDiscoveryScope)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            $"&code_challenge_method=S256";

        if (TryOpenBrowser(authorizeUrl))
        {
            AnsiConsole.MarkupLine("[grey]Browser opened. Waiting for sign-in...[/]");
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold yellow]Open this URL to sign in:[/]");
            AnsiConsole.WriteLine();
            Console.Out.WriteLine(authorizeUrl);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Waiting for authentication callback...[/]");
        }

        var context = await listener.GetContextAsync();
        var code = context.Request.QueryString["code"];
        var error = context.Request.QueryString["error"];

        var responseHtml = error == null
            ? "<html><body><h2>Authentication successful!</h2><p>You can close this tab.</p></body></html>"
            : $"<html><body><h2>Authentication failed</h2><p>{WebUtility.HtmlEncode(error)}</p></body></html>";

        var buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.Close();
        listener.Stop();

        if (string.IsNullOrEmpty(code))
        {
            var errorDescription = context.Request.QueryString["error_description"] ?? error ?? "Unknown error";
            throw new InvalidOperationException($"Authentication failed: {errorDescription}");
        }

        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = GlobalDiscoveryScope
        });

        var response = await http.PostAsync(
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token", content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed: {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("No access_token in response.");
    }

    /// <summary>
    /// Exchange a refresh token for an access token with a different scope/tenant.
    /// No browser interaction needed — this is a pure HTTP POST.
    /// </summary>
    private static async Task<string> ExchangeRefreshTokenAsync(
        string clientId, string tenantId, string refreshToken, string scope)
    {
        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = scope
        });

        var tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var response = await http.PostAsync(tokenEndpoint, content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed for tenant {tenantId}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("No access_token in response.");
    }

    private static async Task<List<DiscoveredTenant>> FetchTenantsAsync(string armToken)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", armToken);

        var response = await http.GetAsync(ArmTenantsUrl);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to list tenants: {json}");

        using var doc = JsonDocument.Parse(json);
        var tenants = new List<DiscoveredTenant>();

        foreach (var tenant in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            var tenantId = tenant.GetProperty("tenantId").GetString() ?? "";
            var displayName = tenant.TryGetProperty("displayName", out var dn)
                ? dn.GetString() ?? tenantId
                : tenantId;

            if (!string.IsNullOrEmpty(tenantId))
                tenants.Add(new DiscoveredTenant(tenantId, displayName));
        }

        return tenants.OrderBy(t => t.DisplayName).ToList();
    }

    private static async Task<List<DiscoveredEnvironment>> FetchEnvironmentsAsync(
        string accessToken, DiscoveredTenant tenant)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.GetAsync($"{GlobalDiscoveryUrl}?$filter=State eq 0");
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Environment discovery failed");

        using var doc = JsonDocument.Parse(json);
        var environments = new List<DiscoveredEnvironment>();

        foreach (var instance in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            var friendlyName = instance.GetProperty("FriendlyName").GetString() ?? "Unknown";
            var url = instance.GetProperty("Url").GetString() ?? "";
            var purpose = instance.TryGetProperty("Purpose", out var p) ? p.GetString() : null;
            var version = instance.TryGetProperty("Version", out var v) ? v.GetString() : null;

            if (!string.IsNullOrEmpty(url))
                environments.Add(new DiscoveredEnvironment(
                    friendlyName, url, purpose, version, tenant.DisplayName, tenant.TenantId));
        }

        return environments;
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static int GetAvailablePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}
