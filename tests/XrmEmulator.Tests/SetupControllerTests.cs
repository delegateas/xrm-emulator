using System.Net;

namespace XrmEmulator.Tests;

[Collection("XrmEmulator")]
public class SetupControllerTests
{
    private readonly HttpClient _client;

    public SetupControllerTests(XrmEmulatorFixture fixture)
    {
        _client = fixture.HttpClient;
    }

    [Fact]
    public async Task GetSetupPage_ReturnsHtml()
    {
        var response = await _client.GetAsync("/debug/setup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("XRM Emulator Setup", content);
        Assert.Contains("System Users", content);
        Assert.Contains("Teams", content);
    }

    [Fact]
    public async Task CreateUser_RedirectsToSetup()
    {
        var form = new FormUrlEncodedContent(
        [
            new("firstname", "Test"),
            new("lastname", "User"),
            new("domainname", "testuser001"),
        ]);

        // Don't follow redirects so we can assert the redirect
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = _client.BaseAddress };
        var response = await client.PostAsync("/debug/setup/users", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/debug/setup", response.Headers.Location?.OriginalString);

        // Verify user appears in setup page
        var setupPage = await _client.GetStringAsync("/debug/setup");
        Assert.Contains("testuser001", setupPage);
    }

    [Fact]
    public async Task CreateTeam_RedirectsToSetup()
    {
        var form = new FormUrlEncodedContent(
        [
            new("name", "IntegrationTestTeam"),
        ]);

        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = _client.BaseAddress };
        var response = await client.PostAsync("/debug/setup/teams", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/debug/setup", response.Headers.Location?.OriginalString);

        // Verify team appears in setup page
        var setupPage = await _client.GetStringAsync("/debug/setup");
        Assert.Contains("IntegrationTestTeam", setupPage);
    }
}
