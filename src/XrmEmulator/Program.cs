using System.Diagnostics;
using DG.Tools.XrmMockup;
using Microsoft.AspNetCore.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Serilog;
using XrmEmulator.Licensing;
using XrmEmulator.Middleware;
using XrmEmulator.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for detailed logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .CreateLogger();

builder.Host.UseSerilog();

// Add health checks
builder.Services.AddHealthChecks();

// Add API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Dataverse Fake API",
        Version = "v1",
        Description = "A fake Dataverse API for testing PowerPlatform.Dataverse.Client"
    });

    c.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter your bearer token"
    });

    c.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new()
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configure CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add licensing
builder.Services.AddLicensing();

// Add custom services
builder.Services.AddSingleton<XrmEmulator.Services.ITokenService, TokenService>();

// Configure snapshot persistence options
builder.Services.Configure<SnapshotOptions>(options =>
{
    options.Enabled = builder.Configuration.GetValue<bool>("Snapshot:Enabled", true);
    options.FilePath = builder.Configuration.GetValue<string>("Snapshot:FilePath") ?? "./xrm-emulator-snapshot.zip";
    options.SaveIntervalSeconds = builder.Configuration.GetValue<int>("Snapshot:SaveIntervalSeconds", 10);
    options.SaveOnShutdown = builder.Configuration.GetValue<bool>("Snapshot:SaveOnShutdown", true);
    options.RestoreOnStartup = builder.Configuration.GetValue<bool>("Snapshot:RestoreOnStartup", true);
});

// Register XrmMockup365 instance
builder.Services.AddSingleton<XrmMockup365>(provider =>
{
    var settings = new XrmMockupSettings
    {
        BasePluginTypes = [],
        BaseCustomApiTypes = [],
        EnableProxyTypes = false,
        IncludeAllWorkflows = false,
        MetadataDirectoryPath = builder.Configuration.GetValue<string>("XrmMockup:MetadataDirectoryPath") ?? "Metadata",
        EnablePowerFxFields = false, // Disable PowerFx - it has type incompatibilities with SDK
    };

    var xrm = XrmMockup365.GetInstance(settings);
    return xrm;
});

// Register IOrganizationServiceAsync using the XrmMockup365 instance
builder.Services.AddSingleton<IOrganizationServiceAsync>(provider =>
{
    var xrm = provider.GetRequiredService<XrmMockup365>();
    return xrm.GetAdminService();
});

// Register snapshot service
builder.Services.AddSingleton<ISnapshotService, SnapshotService>();
builder.Services.AddHostedService<SnapshotService>(provider =>
    (SnapshotService)provider.GetRequiredService<ISnapshotService>());

// Add XML serialization services for SOAP controller
builder.Services.AddScoped<IRequestMapper, RequestMapper>();
builder.Services.AddScoped<IXmlRequestDeserializer, XmlRequestDeserializer>();
builder.Services.AddScoped<IXmlResponseSerializer, XmlResponseSerializer>();

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

// Log license status
var licenseProvider = app.Services.GetRequiredService<ILicenseProvider>();
if (licenseProvider.CurrentLicense is { } license)
{
    Log.Information("License: Licensed to {Subject} (features: {Features}, expires: {Expiry})",
        license.Subject,
        string.Join(", ", license.Features),
        license.ExpiresAt?.ToString("yyyy-MM-dd") ?? "never");
}
else
{
    Log.Information("License: Unlicensed (core features only). {Error}",
        licenseProvider.ValidationResult.Error ?? "No license key provided");
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Dataverse Fake API v1");
        c.RoutePrefix = "swagger"; // Swagger UI at /swagger
    });
}

// Enable CORS
app.UseCors();

// Add comprehensive request/response logging middleware
app.UseMiddleware<RequestResponseLoggingMiddleware>();

// Add authentication middleware (we'll implement this)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Map health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive");

// Redirect root to debug data page
app.MapGet("/", () => Results.Redirect("/debug/data"));

app.MapGet("/XRMServices/2011/Organization.svc/web", (HttpContext context) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    context.Response.Headers.Append("WWW-Authenticate",
        $"Bearer authorization_uri={baseUrl}/organizations/,resource_id={baseUrl}/");
    context.Response.StatusCode = 401;
    return Task.CompletedTask;
});

// Restore snapshot on startup
var snapshotService = app.Services.GetRequiredService<ISnapshotService>();
if (snapshotService is SnapshotService snapshotServiceImpl)
{
    snapshotServiceImpl.RestoreSnapshot();
}

app.Run();

// Make the Program class public for testing
#pragma warning disable S1118 // Utility classes should not have public constructors
public partial class Program
{
}
#pragma warning restore S1118