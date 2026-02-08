using Microsoft.Xrm.Sdk;

namespace XrmEmulator.DataverseFakeApi.Services;

/// <summary>
/// Interface for deserializing SOAP XML requests into OrganizationRequest objects.
/// </summary>
public interface IXmlRequestDeserializer
{
    /// <summary>
    /// Deserializes a SOAP XML string into an OrganizationRequest.
    /// </summary>
    /// <param name="soapXml">The SOAP XML string containing the request.</param>
    /// <returns>The deserialized OrganizationRequest.</returns>
    OrganizationRequest DeserializeRequest(string soapXml);
}