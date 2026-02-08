using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XrmEmulator.DataverseFakeApi.Services;

/// <summary>
/// Represents token service.
/// </summary>
internal sealed class TokenService : ITokenService
{
    private readonly ILogger<TokenService> _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, TokenInfo> _tokenStore = new(StringComparer.Ordinal);

    public TokenService(ILogger<TokenService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task<TokenResponse> GenerateTokenAsync(TokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogGeneratingToken(request.GrantType, request.ClientId, request.Resource);

        // Validate grant type
        if (!string.Equals(request.GrantType, "client_credentials", StringComparison.Ordinal))
        {
            _logger.LogUnsupportedGrantTypeInService(request.GrantType);
            throw new ArgumentException($"Unsupported grant_type: {request.GrantType}", nameof(request.GrantType));
        }

        // Generate a mock access token
        var tokenId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        var expiresAt = DateTime.UtcNow.AddHours(1); // 1 hour expiry
        var expiresIn = 3600; // 1 hour in seconds

        // Create token info
        var tokenInfo = new TokenInfo(
            ClientId: request.ClientId ?? "unknown-client",
            Resource: request.Resource ?? "https://contoso.crm.dynamics.com/",
            ExpiresAt: expiresAt,
            Claims: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "client_id", request.ClientId ?? "unknown-client" },
                { "resource", request.Resource ?? "https://contoso.crm.dynamics.com/" },
                { "scope", request.Scope ?? "user_impersonation" },
                { "iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() },
                { "iss", "https://login.microsoftonline.com/fake-tenant-id/v2.0" },
                { "aud", request.Resource ?? "https://contoso.crm.dynamics.com/" }
            });

        // Generate base64-encoded token that contains the token ID
        var tokenData = new
        {
            id = tokenId,
            client_id = request.ClientId,
            resource = request.Resource,
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            scope = request.Scope ?? "user_impersonation"
        };

        var tokenJson = JsonSerializer.Serialize(tokenData);
        var tokenBytes = Encoding.UTF8.GetBytes(tokenJson);
        var accessToken = $"fake_token_{Convert.ToBase64String(tokenBytes)}";

        // Store token info for validation later
        _tokenStore[accessToken] = tokenInfo;

        var response = new TokenResponse(
            AccessToken: accessToken,
            TokenType: "Bearer",
            ExpiresIn: expiresIn,
            Resource: request.Resource,
            Scope: request.Scope ?? "user_impersonation");

        _logger.LogGeneratedTokenWithId(tokenId, request.ClientId, expiresAt);

        return Task.FromResult(response);
    }

    /// <summary>
    /// Validate token.
    /// </summary>
    /// <param name="token">The token to validate.</param>
    /// <returns>True if the token is valid; otherwise, false.</returns>
    public bool ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogTokenValidationFailedEmptyToken();
            return false;
        }

        // Remove "Bearer " prefix if present
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token[7..];
        }

        if (!_tokenStore.TryGetValue(token, out var tokenInfo))
        {
            _logger.LogTokenValidationFailedNotFound();
            return false;
        }

        if (DateTime.UtcNow > tokenInfo.ExpiresAt)
        {
            _logger.LogTokenValidationFailedExpired(tokenInfo.ExpiresAt);
            _tokenStore.Remove(token); // Clean up expired token
            return false;
        }

        _logger.LogTokenValidationSuccessful(tokenInfo.ClientId);
        return true;
    }

    public TokenInfo? DecodeToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        // Remove "Bearer " prefix if present
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token[7..];
        }

        if (_tokenStore.TryGetValue(token, out var tokenInfo))
        {
            if (DateTime.UtcNow <= tokenInfo.ExpiresAt)
            {
                return tokenInfo;
            }
            else
            {
                // Clean up expired token
                _tokenStore.Remove(token);
            }
        }

        // Try to decode the token manually for additional info
        try
        {
            if (token.StartsWith("fake_token_", StringComparison.Ordinal))
            {
                var base64Part = token[11..]; // Remove "fake_token_" prefix
                var tokenBytes = Convert.FromBase64String(base64Part);
                var tokenJson = Encoding.UTF8.GetString(tokenBytes);
                var tokenData = JsonSerializer.Deserialize<Dictionary<string, object>>(tokenJson);

                if (tokenData != null)
                {
                    _logger.LogDecodedTokenData(tokenData);

                    return new TokenInfo(
                        ClientId: tokenData.TryGetValue("client_id", out var clientId) ? clientId.ToString() ?? "unknown" : "unknown",
                        Resource: tokenData.TryGetValue("resource", out var resource) ? resource.ToString() ?? "" : "",
                        ExpiresAt: tokenData.TryGetValue("exp", out var exp) && long.TryParse(exp.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var expLong)
                            ? DateTimeOffset.FromUnixTimeSeconds(expLong).DateTime
                            : DateTime.MinValue,
                        Claims: tokenData);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogFailedToDecodeTokenManually(ex);
        }

        return null;
    }
}