using Microsoft.Extensions.Logging;

namespace XrmEmulator;

/// <summary>
/// High-performance logging messages for XrmEmulator project.
/// Uses source-generated LoggerMessage for optimal performance.
/// </summary>
/// <remarks>
/// Event ID Allocation:
///
/// Project Range: 5000-5999
///
/// Component Ranges:
/// - SoapController:                        5001-5050
/// - TokenController:                       5051-5100
/// - RequestResponseLoggingMiddleware:      5101-5200
/// - RequestMapper:                         5201-5250
/// - TokenService:                          5251-5300
/// - XmlRequestDeserializer:                5301-5400
/// - XmlResponseSerializer:                 5401-5450
///
/// Next Available Event ID: 5451.
/// </remarks>
internal static partial class LogMessages
{
    // ============================================================================
    // SoapController (Event IDs: 5001-5050)
    // ============================================================================
    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "SOAP endpoint called")]
    public static partial void LogSoapEndpointCalled(this ILogger logger);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Information,
        Message = "SOAP Request body: {RequestBody}")]
    public static partial void LogSoapRequestBody(this ILogger logger, string requestBody);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Information,
        Message = "Successfully deserialized request: {RequestName}")]
    public static partial void LogSuccessfullyDeserializedRequest(this ILogger logger, string requestName);

    [LoggerMessage(
        EventId = 5004,
        Level = LogLevel.Error,
        Message = "Failed to deserialize SOAP request")]
    public static partial void LogFailedToDeserializeSoapRequest(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5005,
        Level = LogLevel.Information,
        Message = "Successfully executed request: {RequestName}")]
    public static partial void LogSuccessfullyExecutedRequest(this ILogger logger, string requestName);

    [LoggerMessage(
        EventId = 5006,
        Level = LogLevel.Error,
        Message = "Failed to execute request: {RequestName}")]
    public static partial void LogFailedToExecuteRequest(this ILogger logger, Exception exception, string requestName);

    [LoggerMessage(
        EventId = 5007,
        Level = LogLevel.Information,
        Message = "Successfully serialized response for: {RequestName}")]
    public static partial void LogSuccessfullySerializedResponse(this ILogger logger, string requestName);

    [LoggerMessage(
        EventId = 5008,
        Level = LogLevel.Error,
        Message = "Failed to serialize response for: {RequestName}")]
    public static partial void LogFailedToSerializeResponse(this ILogger logger, Exception exception, string requestName);

    [LoggerMessage(
        EventId = 5009,
        Level = LogLevel.Error,
        Message = "Unexpected error processing SOAP request")]
    public static partial void LogUnexpectedErrorProcessingSoapRequest(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5010,
        Level = LogLevel.Information,
        Message = "SOAP service metadata requested")]
    public static partial void LogSoapServiceMetadataRequested(this ILogger logger);

    // ============================================================================
    // TokenController (Event IDs: 5051-5100)
    // ============================================================================
    [LoggerMessage(
        EventId = 5051,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] Token request received for grant_type: {GrantType}, client_id: {ClientId}")]
    public static partial void LogTokenRequestReceived(
        this ILogger logger,
        string correlationId,
        string? grantType,
        string? clientId);

    [LoggerMessage(
        EventId = 5052,
        Level = LogLevel.Warning,
        Message = "[{CorrelationId}] Missing grant_type parameter")]
    public static partial void LogMissingGrantType(this ILogger logger, string correlationId);

    [LoggerMessage(
        EventId = 5053,
        Level = LogLevel.Warning,
        Message = "[{CorrelationId}] Unsupported grant_type: {GrantType}")]
    public static partial void LogUnsupportedGrantType(this ILogger logger, string correlationId, string? grantType);

    [LoggerMessage(
        EventId = 5054,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] Token generated successfully for client {ClientId}")]
    public static partial void LogTokenGeneratedSuccessfully(this ILogger logger, string correlationId, string? clientId);

    [LoggerMessage(
        EventId = 5055,
        Level = LogLevel.Error,
        Message = "[{CorrelationId}] Invalid token request")]
    public static partial void LogInvalidTokenRequest(this ILogger logger, Exception exception, string correlationId);

    [LoggerMessage(
        EventId = 5056,
        Level = LogLevel.Error,
        Message = "[{CorrelationId}] Unexpected error generating token")]
    public static partial void LogUnexpectedErrorGeneratingToken(this ILogger logger, Exception exception, string correlationId);

    [LoggerMessage(
        EventId = 5057,
        Level = LogLevel.Warning,
        Message = "[{CorrelationId}] Token validation request missing Authorization header")]
    public static partial void LogTokenValidationMissingAuthHeader(this ILogger logger, string correlationId);

    [LoggerMessage(
        EventId = 5058,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] Token validation result: {IsValid}")]
    public static partial void LogTokenValidationResult(this ILogger logger, string correlationId, bool isValid);

    // ============================================================================
    // RequestResponseLoggingMiddleware (Event IDs: 5101-5200)
    // ============================================================================
    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Error,
        Message = "[{CorrelationId}] Unhandled exception occurred during request processing")]
    public static partial void LogUnhandledExceptionDuringRequestProcessing(
        this ILogger logger,
        Exception exception,
        string correlationId);

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] INCOMING REQUEST: {Method} {Path}{QueryString}\nHeaders: {@Headers}\nBody: {RequestBody}\nContentType: {ContentType}\nContentLength: {ContentLength}\nHost: {Host}\nUserAgent: {UserAgent}\nRemoteIP: {RemoteIP}")]
    public static partial void LogIncomingRequest(
        this ILogger logger,
        string correlationId,
        string method,
        string path,
        string queryString,
        Dictionary<string, string> headers,
        string requestBody,
        string contentType,
        string contentLength,
        string host,
        string userAgent,
        string remoteIP);

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] Authentication header present: {AuthHeader}")]
    public static partial void LogAuthenticationHeaderPresent(
        this ILogger logger,
        string correlationId,
        string authHeader);

    [LoggerMessage(
        EventId = 5104,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] OData-Version: {ODataVersion}")]
    public static partial void LogODataVersion(
        this ILogger logger,
        string correlationId,
        string? odataVersion);

    [LoggerMessage(
        EventId = 5105,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] OData-MaxVersion: {ODataMaxVersion}")]
    public static partial void LogODataMaxVersion(
        this ILogger logger,
        string correlationId,
        string? odataMaxVersion);

    [LoggerMessage(
        EventId = 5106,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] MSCRMCallerID: {CallerId}")]
    public static partial void LogMSCRMCallerId(
        this ILogger logger,
        string correlationId,
        string? callerId);

    [LoggerMessage(
        EventId = 5107,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] ClientRequestId: {ClientRequestId}")]
    public static partial void LogClientRequestId(
        this ILogger logger,
        string correlationId,
        string? clientRequestId);

    [LoggerMessage(
        EventId = 5108,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] OUTGOING RESPONSE: {StatusCode} {ReasonPhrase} in {ElapsedMs}ms\nHeaders: {@Headers}\nBody: {ResponseBody}\nContentType: {ContentType}\nContentLength: {ContentLength}")]
    public static partial void LogOutgoingResponseInfo(
        this ILogger logger,
        string correlationId,
        int statusCode,
        string reasonPhrase,
        long elapsedMs,
        Dictionary<string, string> headers,
        string responseBody,
        string contentType,
        string contentLength);

    [LoggerMessage(
        EventId = 5109,
        Level = LogLevel.Warning,
        Message = "[{CorrelationId}] OUTGOING RESPONSE: {StatusCode} {ReasonPhrase} in {ElapsedMs}ms\nHeaders: {@Headers}\nBody: {ResponseBody}\nContentType: {ContentType}\nContentLength: {ContentLength}")]
    public static partial void LogOutgoingResponseWarning(
        this ILogger logger,
        string correlationId,
        int statusCode,
        string reasonPhrase,
        long elapsedMs,
        Dictionary<string, string> headers,
        string responseBody,
        string contentType,
        string contentLength);

    [LoggerMessage(
        EventId = 5110,
        Level = LogLevel.Warning,
        Message = "[{CorrelationId}] SLOW REQUEST detected: {ElapsedMs}ms for {Method} {Path}")]
    public static partial void LogSlowRequestDetected(
        this ILogger logger,
        string correlationId,
        long elapsedMs,
        string method,
        string path);

    [LoggerMessage(
        EventId = 5111,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] Response OData-Version: {ODataVersion}")]
    public static partial void LogResponseODataVersion(
        this ILogger logger,
        string correlationId,
        string? odataVersion);

    [LoggerMessage(
        EventId = 5112,
        Level = LogLevel.Information,
        Message = "[{CorrelationId}] Resource Usage: {ResourceUsage}")]
    public static partial void LogResourceUsage(
        this ILogger logger,
        string correlationId,
        string? resourceUsage);

    // ============================================================================
    // RequestMapper (Event IDs: 5201-5250)
    // ============================================================================
    [LoggerMessage(
        EventId = 5201,
        Level = LogLevel.Debug,
        Message = "Mapping request name '{RequestName}' to typed request")]
    public static partial void LogMappingRequestNameToTypedRequest(
        this ILogger logger,
        string requestName);

    [LoggerMessage(
        EventId = 5202,
        Level = LogLevel.Debug,
        Message = "Successfully mapped '{RequestName}' to {TypedRequestType}")]
    public static partial void LogSuccessfullyMappedToTypedRequest(
        this ILogger logger,
        string requestName,
        string typedRequestType);

    [LoggerMessage(
        EventId = 5203,
        Level = LogLevel.Warning,
        Message = "No specific mapping found for request name '{RequestName}', using generic OrganizationRequest")]
    public static partial void LogNoSpecificMappingFound(
        this ILogger logger,
        string requestName);

    // ============================================================================
    // TokenService (Event IDs: 5251-5300)
    // ============================================================================
    [LoggerMessage(
        EventId = 5251,
        Level = LogLevel.Information,
        Message = "Generating token for grant_type: {GrantType}, client_id: {ClientId}, resource: {Resource}")]
    public static partial void LogGeneratingToken(
        this ILogger logger,
        string? grantType,
        string? clientId,
        string? resource);

    [LoggerMessage(
        EventId = 5252,
        Level = LogLevel.Warning,
        Message = "Unsupported grant type: {GrantType}")]
    public static partial void LogUnsupportedGrantTypeInService(
        this ILogger logger,
        string? grantType);

    [LoggerMessage(
        EventId = 5253,
        Level = LogLevel.Information,
        Message = "Generated token with ID {TokenId} for client {ClientId}, expires at {ExpiresAt}")]
    public static partial void LogGeneratedTokenWithId(
        this ILogger logger,
        string tokenId,
        string? clientId,
        DateTime expiresAt);

    [LoggerMessage(
        EventId = 5254,
        Level = LogLevel.Debug,
        Message = "Token validation failed: empty token")]
    public static partial void LogTokenValidationFailedEmptyToken(this ILogger logger);

    [LoggerMessage(
        EventId = 5255,
        Level = LogLevel.Debug,
        Message = "Token validation failed: token not found in store")]
    public static partial void LogTokenValidationFailedNotFound(this ILogger logger);

    [LoggerMessage(
        EventId = 5256,
        Level = LogLevel.Debug,
        Message = "Token validation failed: token expired at {ExpiresAt}")]
    public static partial void LogTokenValidationFailedExpired(
        this ILogger logger,
        DateTime expiresAt);

    [LoggerMessage(
        EventId = 5257,
        Level = LogLevel.Debug,
        Message = "Token validation successful for client {ClientId}")]
    public static partial void LogTokenValidationSuccessful(
        this ILogger logger,
        string clientId);

    [LoggerMessage(
        EventId = 5258,
        Level = LogLevel.Debug,
        Message = "Decoded token data: {@TokenData}")]
    public static partial void LogDecodedTokenData(
        this ILogger logger,
        Dictionary<string, object>? tokenData);

    [LoggerMessage(
        EventId = 5259,
        Level = LogLevel.Debug,
        Message = "Failed to decode token manually")]
    public static partial void LogFailedToDecodeTokenManually(
        this ILogger logger,
        Exception exception);

    // ============================================================================
    // XmlRequestDeserializer (Event IDs: 5301-5400)
    // ============================================================================
    [LoggerMessage(
        EventId = 5301,
        Level = LogLevel.Debug,
        Message = "Deserializing SOAP XML request")]
    public static partial void LogDeserializingSoapXmlRequest(this ILogger logger);

    [LoggerMessage(
        EventId = 5302,
        Level = LogLevel.Debug,
        Message = "Found request name: {RequestName}")]
    public static partial void LogFoundRequestName(
        this ILogger logger,
        string requestName);

    [LoggerMessage(
        EventId = 5303,
        Level = LogLevel.Debug,
        Message = "Successfully deserialized {RequestType}")]
    public static partial void LogSuccessfullyDeserializedType(
        this ILogger logger,
        string requestType);

    [LoggerMessage(
        EventId = 5304,
        Level = LogLevel.Error,
        Message = "Failed to deserialize SOAP XML request")]
    public static partial void LogFailedToDeserializeSoapXmlRequest(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 5305,
        Level = LogLevel.Debug,
        Message = "Found request element using path: {Path}")]
    public static partial void LogFoundRequestElementUsingPath(
        this ILogger logger,
        string path);

    [LoggerMessage(
        EventId = 5306,
        Level = LogLevel.Debug,
        Message = "Failed to find element with path {Path}: {Error}")]
    public static partial void LogFailedToFindElementWithPath(
        this ILogger logger,
        string path,
        string error);

    [LoggerMessage(
        EventId = 5307,
        Level = LogLevel.Debug,
        Message = "Parsing {EnumType} value: '{EnumValue}'")]
    public static partial void LogParsingEnumValue(
        this ILogger logger,
        string enumType,
        string enumValue);

    [LoggerMessage(
        EventId = 5308,
        Level = LogLevel.Warning,
        Message = "Unknown {EnumType} value: '{Part}'")]
    public static partial void LogUnknownEnumValue(
        this ILogger logger,
        string enumType,
        string part);

    [LoggerMessage(
        EventId = 5309,
        Level = LogLevel.Warning,
        Message = "Failed to parse enum value '{Value}' for type '{TypeValue}'")]
    public static partial void LogFailedToParseEnumValue(
        this ILogger logger,
        Exception exception,
        string value,
        string typeValue);

    [LoggerMessage(
        EventId = 5310,
        Level = LogLevel.Debug,
        Message = "Resolved enum type '{TypeName}' from assembly")]
    public static partial void LogResolvedEnumType(
        this ILogger logger,
        string? typeName);

    // ============================================================================
    // XmlResponseSerializer (Event IDs: 5401-5450)
    // ============================================================================
    [LoggerMessage(
        EventId = 5401,
        Level = LogLevel.Debug,
        Message = "Serializing {ResponseType} to SOAP XML")]
    public static partial void LogSerializingResponseToSoapXml(
        this ILogger logger,
        string responseType);

    [LoggerMessage(
        EventId = 5402,
        Level = LogLevel.Debug,
        Message = "Successfully serialized {ResponseType} to SOAP XML")]
    public static partial void LogSuccessfullySerializedResponseToSoapXml(
        this ILogger logger,
        string responseType);

    [LoggerMessage(
        EventId = 5403,
        Level = LogLevel.Error,
        Message = "Failed to serialize {ResponseType} to SOAP XML")]
    public static partial void LogFailedToSerializeResponseToSoapXml(
        this ILogger logger,
        Exception exception,
        string responseType);

    [LoggerMessage(
        EventId = 5404,
        Level = LogLevel.Debug,
        Message = "Direct EntityMetadata serialization: LogicalName='{LogicalName}', SchemaName='{SchemaName}', PrimaryIdAttribute='{PrimaryIdAttribute}', PrimaryNameAttribute='{PrimaryNameAttribute}'")]
    public static partial void LogDirectEntityMetadataSerialization(
        this ILogger logger,
        string logicalName,
        string schemaName,
        string primaryIdAttribute,
        string primaryNameAttribute);

    [LoggerMessage(
        EventId = 5405,
        Level = LogLevel.Debug,
        Message = "EntityMetadata values: LogicalName='{LogicalName}', SchemaName='{SchemaName}', PrimaryIdAttribute='{PrimaryIdAttribute}', PrimaryNameAttribute='{PrimaryNameAttribute}'")]
    public static partial void LogEntityMetadataValues(
        this ILogger logger,
        string logicalName,
        string schemaName,
        string primaryIdAttribute,
        string primaryNameAttribute);

    [LoggerMessage(
        EventId = 5406,
        Level = LogLevel.Information,
        Message = "Generated EntityMetadata XML manually")]
    public static partial void LogGeneratedEntityMetadataXmlManually(this ILogger logger);
}
