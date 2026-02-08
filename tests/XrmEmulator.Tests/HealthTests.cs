using System.Net;

namespace XrmEmulator.Tests;

[Collection("XrmEmulator")]
public class HealthTests
{
    private readonly HttpClient _client;

    public HealthTests(XrmEmulatorFixture fixture)
    {
        _client = fixture.HttpClient;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Alive_ReturnsOk()
    {
        var response = await _client.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
