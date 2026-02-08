using Microsoft.Xrm.Sdk;

namespace XrmEmulator.Services;

/// <summary>
/// Interface for serializing OrganizationResponse objects into SOAP XML.
/// </summary>
public interface IXmlResponseSerializer
{
    /// <summary>
    /// Serializes an OrganizationResponse to SOAP XML format.
    /// </summary>
    /// <param name="response">The OrganizationResponse to serialize.</param>
    /// <returns>The SOAP XML string.</returns>
    string SerializeResponse(OrganizationResponse response);
}