using System.Net;

namespace XrmEmulator.Tests;

[Collection("XrmEmulator")]
public class DataControllerTests
{
    private readonly HttpClient _client;

    public DataControllerTests(XrmEmulatorFixture fixture)
    {
        _client = fixture.HttpClient;
    }

    [Fact]
    public async Task GetAllData_ReturnsHtml()
    {
        var response = await _client.GetAsync("/debug/data");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("XRM Emulator Data", content);
        Assert.Contains("Table of Contents", content);
    }

    [Fact]
    public async Task GetEntityData_SystemUser_ReturnsHtml()
    {
        var response = await _client.GetAsync("/debug/data/systemuser");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("systemuser", content);
    }
}
