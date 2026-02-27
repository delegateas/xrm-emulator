using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace XrmEmulator.MetadataSync.Connection;

public partial class OAuthTokenProvider
{
    private readonly string _dataverseUrl;
    private readonly string _clientId;
    private readonly bool _noCache;

    private string? _authorizeEndpoint;
    private string? _tokenEndpoint;
    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _expiresAt;

    public OAuthTokenProvider(string dataverseUrl, string clientId, bool noCache = false)
    {
        _dataverseUrl = dataverseUrl.TrimEnd('/');
        _clientId = clientId;
        _noCache = noCache;
    }

    public async Task AuthenticateAsync()
    {
        if (_noCache)
        {
            TokenCache.Clear(_dataverseUrl, _clientId);
            AnsiConsole.MarkupLine("[grey]Cache disabled (--no-cache), forcing fresh sign-in...[/]");
        }

        // Try cached refresh token first
        var cached = _noCache ? null : TokenCache.Load(_dataverseUrl, _clientId);
        if (cached != null)
        {
            AnsiConsole.MarkupLine("[grey]Found cached credentials, attempting silent refresh...[/]");
            var tenantId = cached.TenantId;
            _tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            _authorizeEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize";
            _refreshToken = cached.RefreshToken;

            try
            {
                await RefreshTokenAsync();
                SaveTokenToCache(tenantId);
                AnsiConsole.MarkupLine("[green]Authentication successful (cached).[/]");
                return;
            }
            catch
            {
                AnsiConsole.MarkupLine("[yellow]Cached token expired, falling back to browser sign-in...[/]");
                TokenCache.Clear(_dataverseUrl, _clientId);
                _refreshToken = null;
            }
        }

        AnsiConsole.MarkupLine("[grey]Discovering tenant...[/]");
        await DiscoverTenantEndpointsAsync();

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);

        var port = GetAvailablePort();
        var redirectUri = $"http://localhost:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var scope = $"{_dataverseUrl}/.default offline_access";
        var authorizeUrl = $"{_authorizeEndpoint}" +
            $"?client_id={Uri.EscapeDataString(_clientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(scope)}" +
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

        // Send response to the browser
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

        await ExchangeCodeForTokenAsync(code, redirectUri, codeVerifier);

        // Cache the refresh token for future runs
        SaveTokenToCache(ExtractTenantId());

        AnsiConsole.MarkupLine("[green]Authentication successful.[/]");
    }

    public async Task<string> GetTokenAsync(string _)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-5))
            return _accessToken;

        if (_refreshToken != null)
            await RefreshTokenAsync();
        else
            throw new InvalidOperationException("No token available. Call AuthenticateAsync first.");

        return _accessToken!;
    }

    private async Task DiscoverTenantEndpointsAsync()
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        // Send unauthenticated request to get WWW-Authenticate header with tenant info
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_dataverseUrl}/api/data/v9.2/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer");

        var response = await http.SendAsync(request);

        var wwwAuth = response.Headers.WwwAuthenticate.ToString();
        var match = AuthorizationUriRegex().Match(wwwAuth);

        if (!match.Success)
            throw new InvalidOperationException(
                $"Could not discover tenant from Dataverse. WWW-Authenticate header: {wwwAuth}");

        // authorization_uri is like https://login.microsoftonline.com/{tenant}/oauth2/authorize
        // We need the tenant ID to build v2.0 endpoints
        var authorityUri = new Uri(match.Groups[1].Value);
        var tenantId = authorityUri.Segments[1].TrimEnd('/');

        _authorizeEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize";
        _tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

        AnsiConsole.MarkupLine($"[grey]Tenant: {tenantId}[/]");
    }

    private async Task ExchangeCodeForTokenAsync(string code, string redirectUri, string codeVerifier)
    {
        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = $"{_dataverseUrl}/.default offline_access"
        });

        var response = await http.PostAsync(_tokenEndpoint, content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed: {json}");

        ParseTokenResponse(json);
    }

    private async Task RefreshTokenAsync()
    {
        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _refreshToken!,
            ["scope"] = $"{_dataverseUrl}/.default offline_access"
        });

        var response = await http.PostAsync(_tokenEndpoint, content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token refresh failed: {json}");

        ParseTokenResponse(json);
    }

    private void ParseTokenResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        _accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("No access_token in response.");

        if (root.TryGetProperty("refresh_token", out var rt))
            _refreshToken = rt.GetString();

        var expiresIn = root.GetProperty("expires_in").GetInt32();
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
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

    private string ExtractTenantId()
    {
        // _tokenEndpoint is like https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token
        var uri = new Uri(_tokenEndpoint!);
        return uri.Segments[1].TrimEnd('/');
    }

    private void SaveTokenToCache(string tenantId)
    {
        if (_refreshToken != null)
        {
            TokenCache.Save(_dataverseUrl, _clientId, tenantId, _refreshToken);
        }
    }

    [GeneratedRegex(@"authorization_uri=(\S+)")]
    private static partial Regex AuthorizationUriRegex();
}
