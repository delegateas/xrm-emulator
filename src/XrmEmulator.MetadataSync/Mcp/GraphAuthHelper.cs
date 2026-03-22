using System.Net.Http.Json;
using System.Text.Json;

namespace XrmEmulator.MetadataSync.Mcp;

public static class GraphAuthHelper
{
    private static readonly HttpClient Http = new();

    /// <summary>
    /// Start a temporary HTTP listener, print an auth URL, wait for the browser redirect,
    /// and exchange the auth code for tokens. Returns (accessToken, refreshToken).
    /// </summary>
    public static async Task<(string AccessToken, string RefreshToken)> AcquireTokensInteractiveAsync(
        string clientId, string tenantId)
    {
        // Find a free port
        var listener = new System.Net.HttpListener();
        var port = FindFreePort();
        var redirectUri = $"http://localhost:{port}/";
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var scopes = "Chat.Create ChatMessage.Send offline_access";
        var authUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(scopes)}";

        Console.WriteLine();
        Console.WriteLine("Open the following URL in your browser to sign in:");
        Console.WriteLine();
        Console.WriteLine(authUrl);
        Console.WriteLine();
        Console.WriteLine("Waiting for browser redirect...");

        // Wait for the browser callback
        var context = await listener.GetContextAsync();
        var code = context.Request.QueryString["code"];
        var error = context.Request.QueryString["error"];

        // Send response to browser
        var responseHtml = error != null
            ? "<html><body><h2>Authentication failed</h2><p>You can close this tab.</p></body></html>"
            : "<html><body><h2>Authentication successful!</h2><p>You can close this tab.</p><script>setTimeout(()=>window.close(),2000)</script></body></html>";
        var buffer = System.Text.Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.Close();
        listener.Stop();

        if (string.IsNullOrEmpty(code))
            throw new InvalidOperationException($"Authentication failed: {error ?? "no code received"}");

        // Exchange code for tokens
        return await ExchangeCodeForTokensAsync(clientId, tenantId, code, redirectUri);
    }

    /// <summary>
    /// Use a refresh token to get a fresh access token.
    /// Returns (newAccessToken, newRefreshToken) — refresh tokens may rotate.
    /// </summary>
    public static async Task<(string AccessToken, string RefreshToken)> RefreshAccessTokenAsync(
        string clientId, string tenantId, string refreshToken)
    {
        var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = "Chat.Create ChatMessage.Send offline_access"
        });

        var response = await Http.PostAsync(tokenUrl, body);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token refresh failed: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return (
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!
        );
    }

    private static async Task<(string AccessToken, string RefreshToken)> ExchangeCodeForTokensAsync(
        string clientId, string tenantId, string code, string redirectUri)
    {
        var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "Chat.Create ChatMessage.Send offline_access"
        });

        var response = await Http.PostAsync(tokenUrl, body);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return (
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!
        );
    }

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
