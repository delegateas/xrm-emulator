using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace XrmEmulator.Tests;

public sealed class XrmEmulatorFixture : IAsyncLifetime
{
    private DistributedApplication? _app;

    public HttpClient HttpClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.XrmEmulator_Tests_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(http =>
            http.AddStandardResilienceHandler());

        _app = await appHost.BuildAsync();
        var notificationService = _app.Services.GetRequiredService<ResourceNotificationService>();
        await _app.StartAsync();

        await notificationService.WaitForResourceHealthyAsync("xrm-emulator")
            .WaitAsync(TimeSpan.FromMinutes(5));

        HttpClient = _app.CreateHttpClient("xrm-emulator");
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

[CollectionDefinition("XrmEmulator")]
public class XrmEmulatorCollection : ICollectionFixture<XrmEmulatorFixture>
{
}
