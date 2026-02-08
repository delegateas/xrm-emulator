using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace XrmEmulator.Middleware;

/// <summary>
/// Represents request response logging middleware.
/// </summary>
internal sealed class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    public RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    private static string GetReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "Unknown"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        context.Items["CorrelationId"] = correlationId;

        // Start timing
        var stopwatch = Stopwatch.StartNew();

        // Log incoming request
        await LogRequestAsync(context, correlationId).ConfigureAwait(false);

        // Capture response
        var originalResponseBodyStream = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogUnhandledExceptionDuringRequestProcessing(ex, correlationId);
            throw;
        }
        finally
        {
            stopwatch.Stop();

            // Log outgoing response
            await LogResponseAsync(context, correlationId, stopwatch.ElapsedMilliseconds).ConfigureAwait(false);

            // Copy response back to original stream
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalResponseBodyStream).ConfigureAwait(false);
        }
    }

    private async Task LogRequestAsync(HttpContext context, string correlationId)
    {
        var request = context.Request;

        // Enable request body buffering
        request.EnableBuffering();

        // Read request body
        string requestBody = "";
        if (request.Body.CanRead)
        {
            request.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
            request.Body.Seek(0, SeekOrigin.Begin);
        }

        // Log comprehensive request information
        _logger.LogIncomingRequest(
            correlationId,
            request.Method,
            request.Path.ToString(),
            request.QueryString.ToString(),
            request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.Ordinal),
            string.IsNullOrEmpty(requestBody) ? "[empty]" : requestBody,
            request.ContentType ?? "[not set]",
            request.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "[not set]",
            request.Host.ToString(),
            request.Headers.UserAgent.ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? "[unknown]");

        // Special logging for authentication headers
        if (request.Headers.ContainsKey("Authorization"))
        {
            var authHeader = request.Headers.Authorization.ToString();
            var maskedAuth = authHeader.Length > 20
                ? $"{authHeader[..20]}...{authHeader[^4..]}"
                : "[short_token]";

            _logger.LogAuthenticationHeaderPresent(correlationId, maskedAuth);
        }

        // Log OData specific headers
        if (request.Headers.TryGetValue("OData-Version", out var odataVersion))
        {
            _logger.LogODataVersion(correlationId, odataVersion.ToString());
        }

        if (request.Headers.TryGetValue("OData-MaxVersion", out var odataMaxVersion))
        {
            _logger.LogODataMaxVersion(correlationId, odataMaxVersion.ToString());
        }

        // Log Power Platform specific headers
        if (request.Headers.TryGetValue("MSCRMCallerID", out var mscrmCallerId))
        {
            _logger.LogMSCRMCallerId(correlationId, mscrmCallerId.ToString());
        }

        if (request.Headers.TryGetValue("x-ms-client-request-id", out var clientRequestId))
        {
            _logger.LogClientRequestId(correlationId, clientRequestId.ToString());
        }
    }

    private async Task LogResponseAsync(HttpContext context, string correlationId, long elapsedMs)
    {
        var response = context.Response;

        // Read response body
        string responseBody = "";
        if (response.Body.CanRead && response.Body.CanSeek)
        {
            response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(response.Body, Encoding.UTF8, leaveOpen: true);
            responseBody = await reader.ReadToEndAsync().ConfigureAwait(false);
            response.Body.Seek(0, SeekOrigin.Begin);
        }

        // Determine log level based on status code
        if (response.StatusCode >= 400)
        {
            _logger.LogOutgoingResponseWarning(
                correlationId,
                response.StatusCode,
                GetReasonPhrase(response.StatusCode),
                elapsedMs,
                response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.Ordinal),
                string.IsNullOrEmpty(responseBody) ? "[empty]" : responseBody,
                response.ContentType ?? "[not set]",
                response.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "[calculated]");
        }
        else
        {
            _logger.LogOutgoingResponseInfo(
                correlationId,
                response.StatusCode,
                GetReasonPhrase(response.StatusCode),
                elapsedMs,
                response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.Ordinal),
                string.IsNullOrEmpty(responseBody) ? "[empty]" : responseBody,
                response.ContentType ?? "[not set]",
                response.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "[calculated]");
        }

        // Log performance metrics
        if (elapsedMs > 1000) // Log slow requests
        {
            _logger.LogSlowRequestDetected(correlationId, elapsedMs, context.Request.Method, context.Request.Path.ToString());
        }

        // Log specific response headers that might be important for Dataverse clients
        if (response.Headers.TryGetValue("OData-Version", out var responseODataVersion))
        {
            _logger.LogResponseODataVersion(correlationId, responseODataVersion.ToString());
        }

        if (response.Headers.TryGetValue("x-ms-resource-usage", out var resourceUsage))
        {
            _logger.LogResourceUsage(correlationId, resourceUsage.ToString());
        }
    }
}