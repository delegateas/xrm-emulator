using Microsoft.Xrm.Sdk;

namespace XrmEmulator.Services;

/// <summary>
/// Interface for mapping generic OrganizationRequest to specific typed requests.
/// </summary>
internal interface IRequestMapper
{
    /// <summary>
    /// Gets the CLR Type for a request name.
    /// </summary>
    /// <param name="requestName">The name of the request.</param>
    /// <returns>The Type object for the request, or null if not found.</returns>
    Type? GetRequestType(string requestName);

    /// <summary>
    /// Maps a generic OrganizationRequest to the appropriate typed request based on request name.
    /// </summary>
    /// <param name="requestName">The name of the request (e.g., "Create", "Retrieve", "WhoAmI").</param>
    /// <param name="parameters">The parameters from the generic request.</param>
    /// <returns>A typed OrganizationRequest instance.</returns>
    OrganizationRequest MapToTypedRequest(string requestName, ParameterCollection parameters);
}