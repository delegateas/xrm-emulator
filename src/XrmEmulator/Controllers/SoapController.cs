using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using XrmEmulator.Services;

namespace XrmEmulator.Controllers;

/// <summary>
/// Controller that handles SOAP-style requests for ServiceClient compatibility
/// ServiceClient expects to call /XRMServices/2011/Organization.svc/web endpoints.
/// </summary>
[ApiController]
public sealed class SoapController : ControllerBase
{
    private readonly IOrganizationServiceAsync _organizationServiceAdapter;
    private readonly IXmlRequestDeserializer _requestDeserializer;
    private readonly IXmlResponseSerializer _responseSerializer;
    private readonly ISnapshotService _snapshotService;
    private readonly ILogger<SoapController> _logger;

    public SoapController(
        IOrganizationServiceAsync organizationServiceAdapter,
        IXmlRequestDeserializer requestDeserializer,
        IXmlResponseSerializer responseSerializer,
        ISnapshotService snapshotService,
        ILogger<SoapController> logger)
    {
        _organizationServiceAdapter = organizationServiceAdapter;
        _requestDeserializer = requestDeserializer;
        _responseSerializer = responseSerializer;
        _snapshotService = snapshotService;
        _logger = logger;
    }

    /// <summary>
    /// Handle SOAP requests to the legacy Organization.svc endpoint
    /// ServiceClient will POST SOAP XML to this endpoint for any OrganizationRequest.
    /// </summary>
    /// <returns>A <see cref="Task{IActionResult}"/> representing the asynchronous operation.</returns>
    [HttpPost("/XRMServices/2011/Organization.svc/web")]
    public async Task<IActionResult> HandleSoapRequest()
    {
        try
        {
            _logger.LogSoapEndpointCalled();

            // Read the request body to get SOAP XML
            string requestBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            _logger.LogSoapRequestBody(requestBody);

            // Deserialize the SOAP XML into an OrganizationRequest
            OrganizationRequest request;
            try
            {
                request = _requestDeserializer.DeserializeRequest(requestBody);
                _logger.LogSuccessfullyDeserializedRequest(request.RequestName);
            }
            catch (Exception ex)
            {
                _logger.LogFailedToDeserializeSoapRequest(ex);
                var faultResponse = CreateSoapFaultResponse($"Invalid request format: {ex.Message}");
                Response.ContentType = "text/xml; charset=utf-8";
                return BadRequest(faultResponse);
            }

            // Execute the request using the organization service adapter
            OrganizationResponse response;
            try
            {
                response = await _organizationServiceAdapter.ExecuteAsync(request).ConfigureAwait(false);
                _logger.LogSuccessfullyExecutedRequest(request.RequestName);

                // Mark snapshot as dirty if this was a mutating operation
                if (IsMutatingRequest(request.RequestName))
                {
                    _snapshotService.MarkDirty();
                }
            }
            catch (Exception ex)
            {
                _logger.LogFailedToExecuteRequest(ex, request.RequestName);
                var faultResponse = CreateSoapFaultResponse($"Request execution failed: {ex.Message}");
                Response.ContentType = "text/xml; charset=utf-8";
                return StatusCode(500, faultResponse);
            }

            // Serialize the response back to SOAP XML
            try
            {
                var soapResponse = _responseSerializer.SerializeResponse(response);
                _logger.LogSuccessfullySerializedResponse(request.RequestName);

                return Content(soapResponse, "text/xml; charset=utf-8");
            }
            catch (Exception ex)
            {
                _logger.LogFailedToSerializeResponse(ex, request.RequestName);
                var faultResponse = CreateSoapFaultResponse($"Response serialization failed: {ex.Message}");
                Response.ContentType = "text/xml; charset=utf-8";
                return StatusCode(500, faultResponse);
            }
        }
        catch (Exception ex)
        {
            _logger.LogUnexpectedErrorProcessingSoapRequest(ex);
            var faultResponse = CreateSoapFaultResponse($"Internal server error: {ex.Message}");
            Response.ContentType = "text/xml; charset=utf-8";
            return StatusCode(500, faultResponse);
        }
    }

    /// <summary>
    /// Handle GET requests to the SOAP endpoint (for metadata or discovery).
    /// </summary>
    /// <returns>A Content result with service metadata information.</returns>
    [HttpGet("/XRMServices/2011/Organization.svc")]
    public IActionResult GetServiceMetadata()
    {
        _logger.LogSoapServiceMetadataRequested();

        // Return minimal WSDL-style response to indicate this is a SOAP service
        var wsdlResponse = @"<?xml version=""1.0"" encoding=""utf-8""?>
<definitions xmlns=""http://schemas.xmlsoap.org/wsdl/""
             targetNamespace=""http://schemas.microsoft.com/crm/2011/Contracts/Services""
             xmlns:tns=""http://schemas.microsoft.com/crm/2011/Contracts/Services"">
    <types>
        <schema targetNamespace=""http://schemas.microsoft.com/crm/2011/Contracts/Services""/>
    </types>
    <service name=""OrganizationService"">
        <port name=""BasicHttpBinding_IOrganizationService"" binding=""tns:BasicHttpBinding_IOrganizationService"">
            <soap:address location=""" + Request.Scheme + "://" + Request.Host + @"/XRMServices/2011/Organization.svc/web"" xmlns:soap=""http://schemas.xmlsoap.org/soap/""/>
        </port>
    </service>
</definitions>";

        return Content(wsdlResponse, "text/xml; charset=utf-8");
    }

    /// <summary>
    /// Create a SOAP fault response for errors.
    /// </summary>
    private static string CreateSoapFaultResponse(string faultMessage)
    {
        var soapFault = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
    <soap:Body>
        <soap:Fault>
            <faultcode>Server</faultcode>
            <faultstring>{faultMessage}</faultstring>
        </soap:Fault>
    </soap:Body>
</soap:Envelope>";

        return soapFault;
    }

    /// <summary>
    /// Determines if a request type mutates data (requiring a snapshot).
    /// </summary>
    /// <param name="requestName">The name of the request.</param>
    /// <returns>True if the request mutates data, false otherwise.</returns>
    private static bool IsMutatingRequest(string requestName)
    {
        // Read-only operations that don't require snapshot
        return requestName switch
        {
            // Explicitly non-mutating operations
            "Retrieve" => false,
            "RetrieveMultiple" => false,
            "RetrieveEntity" => false,
            "RetrieveAllEntities" => false,
            "RetrieveAttribute" => false,
            "RetrieveRelationship" => false,
            "RetrieveMetadataChanges" => false,
            "RetrieveOptionSet" => false,
            "RetrieveAllOptionSets" => false,
            "RetrieveVersion" => false,
            "WhoAmI" => false,
            "RetrievePrincipalAccess" => false,
            "RetrieveExchangeRate" => false,
            "FetchXmlToQueryExpression" => false,
            "RetrieveCurrentOrganization" => false,

            // All other operations are considered mutating (Create, Update, Delete, Associate, etc.)
            _ => true
        };
    }
}
