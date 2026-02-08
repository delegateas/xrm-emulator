using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Crm.Sdk.Messages;

namespace XrmEmulator.Services;

/// <summary>
/// Implementation of IRequestMapper that maps generic OrganizationRequest to specific typed requests.
/// </summary>
internal sealed class RequestMapper : IRequestMapper
{
    private readonly ILogger<RequestMapper> _logger;

    public RequestMapper(ILogger<RequestMapper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the CLR Type for a request name.
    /// </summary>
    /// <param name="requestName">The name of the request.</param>
    /// <returns>The Type object for the request, or null if not found.</returns>
    public Type? GetRequestType(string requestName)
    {
        ArgumentNullException.ThrowIfNull(requestName);
        return SdkTypeCache.Instance.TryGetRequestType(requestName);
    }

    /// <summary>
    /// Map to typed request.
    /// </summary>
    /// <param name="requestName">The name of the request to map.</param>
    /// <param name="parameters">The parameter collection for the request.</param>
    /// <returns>A typed OrganizationRequest corresponding to the request name.</returns>
    public OrganizationRequest MapToTypedRequest(string requestName, ParameterCollection parameters)
    {
        ArgumentNullException.ThrowIfNull(requestName);
        ArgumentNullException.ThrowIfNull(parameters);

        _logger.LogMappingRequestNameToTypedRequest(requestName);

        // Try to find the request type using the SDK type cache
        var requestType = GetRequestType(requestName);

        OrganizationRequest typedRequest;
        if (requestType != null)
        {
            // Create an instance of the typed request using reflection
            typedRequest = (OrganizationRequest)Activator.CreateInstance(requestType)!;
            _logger.LogSuccessfullyMappedToTypedRequest(requestName, requestType.Name);
        }
        else
        {
            // Fall back to generic OrganizationRequest if no specific type found
            _logger.LogNoSpecificMappingFound(requestName);
            typedRequest = new OrganizationRequest(requestName);
        }

        // Copy parameters from the original request
        foreach (var parameter in parameters)
        {
            typedRequest.Parameters[parameter.Key] = parameter.Value;
        }

        return typedRequest;
    }
}