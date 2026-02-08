namespace XrmEmulator.DataverseFakeApi.Services;

/// <summary>
/// Defines i token service contract.
/// </summary>
public interface ITokenService
{
    Task<TokenResponse> GenerateTokenAsync(TokenRequest request);
    bool ValidateToken(string token);
    TokenInfo? DecodeToken(string token);
}

/// <summary>
/// Represents token request.
/// </summary>
public record TokenRequest(
    string GrantType,
    string? ClientId = null,
    string? ClientSecret = null,
    string? Resource = null,
    string? Scope = null);

/// <summary>
/// Represents token response.
/// </summary>
public record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? Resource = null,
    string? Scope = null);

/// <summary>
/// Represents token info.
/// </summary>
public record TokenInfo(
    string ClientId,
    string Resource,
    DateTime ExpiresAt,
    Dictionary<string, object> Claims);