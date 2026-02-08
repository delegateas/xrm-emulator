using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using XrmEmulator.Services;

namespace XrmEmulator.Controllers;

/// <summary>
/// Represents token controller.
/// </summary>
[ApiController]
[Route("organizations/oauth2/v2.0")]
public sealed class TokenController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly ILogger<TokenController> _logger;

    public TokenController(ITokenService tokenService, ILogger<TokenController> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// OAuth2 token endpoint that mimics Azure AD token endpoint.
    /// </summary>
    /// <param name="request">Token request parameters.</param>
    /// <returns>Access token response.</returns>
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    [Produces("application/json")]
    public async Task<IActionResult> GetToken([FromForm] TokenRequestForm request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);

        _logger.LogTokenRequestReceived(correlationId, request.grant_type, request.client_id);

        try
        {
            // Validate required parameters
            if (string.IsNullOrEmpty(request.grant_type))
            {
                _logger.LogMissingGrantType(correlationId);
                return BadRequest(new { error = "invalid_request", error_description = "grant_type is required" });
            }

            if (!string.Equals(request.grant_type, "client_credentials", StringComparison.Ordinal))
            {
                _logger.LogUnsupportedGrantType(correlationId, request.grant_type);
                return BadRequest(new { error = "unsupported_grant_type", error_description = "Only client_credentials is supported" });
            }

            // Create token request
            var tokenRequest = new TokenRequest(
                GrantType: request.grant_type,
                ClientId: request.client_id,
                ClientSecret: request.client_secret,
                Resource: request.resource,
                Scope: request.scope);

            // Generate token
            var tokenResponse = await _tokenService.GenerateTokenAsync(tokenRequest).ConfigureAwait(false);

            _logger.LogTokenGeneratedSuccessfully(correlationId, request.client_id);

            // Return response in the format expected by Azure AD
            return Ok(new
            {
                access_token = tokenResponse.AccessToken,
                token_type = tokenResponse.TokenType,
                expires_in = tokenResponse.ExpiresIn,
                resource = tokenResponse.Resource,
                scope = tokenResponse.Scope
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogInvalidTokenRequest(ex, correlationId);
            return BadRequest(new { error = "invalid_request", error_description = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogUnexpectedErrorGeneratingToken(ex, correlationId);
            return StatusCode(500, new { error = "server_error", error_description = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Validate a bearer token (for debugging purposes).
    /// </summary>
    /// <param name="authorizationHeader">The authorization header containing the bearer token.</param>
    /// <returns>Token validation result.</returns>
    [HttpPost("validate")]
    [Produces("application/json")]
    public IActionResult ValidateToken([FromHeader(Name = "Authorization")] string? authorizationHeader)
    {
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);

        if (string.IsNullOrEmpty(authorizationHeader))
        {
            _logger.LogTokenValidationMissingAuthHeader(correlationId);
            return BadRequest(new { error = "missing_authorization_header" });
        }

        var isValid = _tokenService.ValidateToken(authorizationHeader);
        var tokenInfo = _tokenService.DecodeToken(authorizationHeader);

        _logger.LogTokenValidationResult(correlationId, isValid);

        return Ok(new
        {
            valid = isValid,
            token_info = tokenInfo != null ? new
            {
                client_id = tokenInfo.ClientId,
                resource = tokenInfo.Resource,
                expires_at = tokenInfo.ExpiresAt,
                claims = tokenInfo.Claims
            }
            : null
        });
    }

    /// <summary>
    /// Get OpenID Connect configuration (mimics /.well-known/openid_configuration).
    /// </summary>
    /// <returns>An OK result with OpenID Connect configuration information.</returns>
    [HttpGet(".well-known/openid_configuration")]
    [Produces("application/json")]
    public IActionResult GetOpenIdConfiguration()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var config = new
        {
            issuer = $"{baseUrl}",
            authorization_endpoint = $"{baseUrl}/oauth2/authorize",
            token_endpoint = $"{baseUrl}/oauth2/token",
            jwks_uri = $"{baseUrl}/oauth2/keys",
            response_types_supported = new[] { "code", "token" },
            grant_types_supported = new[] { "client_credentials", "authorization_code" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
            scopes_supported = new[] { "openid", "user_impersonation" },
            claims_supported = new[] { "sub", "aud", "iat", "exp", "iss", "client_id" }
        };

        return Ok(config);
    }
}

/// <summary>
/// Form model for token requests.
/// </summary>
public sealed class TokenRequestForm
{
    public string grant_type { get; set; } = string.Empty;
    public string? client_id { get; set; }
    public string? client_secret { get; set; }
    public string? resource { get; set; }
    public string? scope { get; set; }
}