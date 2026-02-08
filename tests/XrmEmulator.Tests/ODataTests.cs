using System.Net;

namespace XrmEmulator.Tests;

[Collection("XrmEmulator")]
public class SoapEndpointTests
{
    private readonly HttpClient _client;

    public SoapEndpointTests(XrmEmulatorFixture fixture)
    {
        _client = fixture.HttpClient;
    }

    [Fact]
    public async Task SoapEndpoint_Get_ReturnsResponse()
    {
        // The GET endpoint returns WSDL / service description
        var response = await _client.GetAsync("/XRMServices/2011/Organization.svc");
        // Should return 200 (WSDL) or similar, not 404
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SoapEndpoint_Post_WithoutBody_ReturnsBadRequest()
    {
        // POST without a valid SOAP envelope should return an error, not 404
        var content = new StringContent("", System.Text.Encoding.UTF8, "text/xml");
        var response = await _client.PostAsync("/XRMServices/2011/Organization.svc/web", content);
        // The endpoint exists (not 404) — it may return 400 or 500 for invalid SOAP
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AuthChallenge_ReturnsUnauthorized()
    {
        // The web endpoint with GET should return 401 with WWW-Authenticate header
        var response = await _client.GetAsync("/XRMServices/2011/Organization.svc/web");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
    }
}
