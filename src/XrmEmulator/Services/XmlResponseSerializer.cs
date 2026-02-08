#pragma warning disable S1144 // Unused private metadata serialization methods may be needed for future functionality

using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security;
using System.Text;
using System.Xml;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Newtonsoft.Json;

namespace XrmEmulator.DataverseFakeApi.Services;

/// <summary>
/// Implementation of IXmlResponseSerializer that serializes OrganizationResponse objects to SOAP XML.
/// </summary>
internal sealed class XmlResponseSerializer : IXmlResponseSerializer
{
    private readonly ILogger<XmlResponseSerializer> _logger;

    public XmlResponseSerializer(ILogger<XmlResponseSerializer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Serialize response.
    /// </summary>
    /// <param name="response">The organization response to serialize.</param>
    /// <returns>A SOAP XML string representation of the organization response.</returns>
    public string SerializeResponse(OrganizationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        _logger.LogSerializingResponseToSoapXml(response.GetType().Name);

        try
        {
            // Use DataContractSerializer to serialize the response
            var serializer = new DataContractSerializer(
                response.GetType(),
                new DataContractSerializerSettings
                {
                    PreserveObjectReferences = false,
                    MaxItemsInObjectGraph = int.MaxValue
                });

            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = false
            }))
            {
                serializer.WriteObject(writer, response);
            }

            var serializedResponse = sb.ToString();

            // Parse the serialized response to extract inner content (avoid double-wrapping)
            var doc = System.Xml.Linq.XDocument.Parse(serializedResponse);
            var rootElement = doc.Root;

            // Extract the inner content (child elements) without the root wrapper
            var innerContent = new StringBuilder();
            if (rootElement != null)
            {
                foreach (var element in rootElement.Elements())
                {
                    innerContent.Append(element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
                }
            }

            // Wrap in SOAP envelope with the inner content directly in ExecuteResult
            var soapResponse = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:a=""http://schemas.microsoft.com/xrm/2011/Contracts"" xmlns:i=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:b=""http://schemas.microsoft.com/crm/2011/Contracts"" xmlns:c=""http://schemas.datacontract.org/2004/07/System.Collections.Generic"" xmlns:e=""http://schemas.microsoft.com/xrm/2011/Metadata"">
    <s:Body>
        <ExecuteResponse xmlns=""http://schemas.microsoft.com/xrm/2011/Contracts/Services"">
            <ExecuteResult i:type=""{GetResponseTypeName(response)}"">
                {innerContent}
            </ExecuteResult>
        </ExecuteResponse>
    </s:Body>
</s:Envelope>";

            _logger.LogSuccessfullySerializedResponseToSoapXml(response.GetType().Name);
            return soapResponse;
        }
        catch (Exception ex)
        {
            _logger.LogFailedToSerializeResponseToSoapXml(ex, response.GetType().Name);
            throw new InvalidOperationException($"Failed to serialize {response.GetType().Name} to SOAP XML", ex);
        }
    }

    private string GetResponseTypeName(OrganizationResponse response)
    {
        var responseType = response.GetType();
        var typeName = responseType.Name;

        // Determine the appropriate namespace prefix
        if (responseType.Namespace?.Contains("Microsoft.Crm.Sdk.Messages", StringComparison.Ordinal) == true)
        {
            return $"b:{typeName}";
        }

        return $"a:{typeName}";
    }
}
