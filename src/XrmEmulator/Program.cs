using System.Diagnostics;
using System.Net;
using System.Text;
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

// Determine XrmMockup metadata directory path
// Priority: 1) Explicit XrmMockup:MetadataDirectoryPath, 2) Build combined from SolutionExports, 3) Default "Metadata"
var explicitMetadataPath = builder.Configuration.GetValue<string>("XrmMockup:MetadataDirectoryPath");
var solutionExportsPathForMetadata = builder.Configuration.GetValue<string>("SolutionExports:Path");
string metadataDirectoryPath;

if (!string.IsNullOrEmpty(explicitMetadataPath))
{
    metadataDirectoryPath = explicitMetadataPath;
    Log.Information("XrmMockup: Using explicit metadata path: {Path}", metadataDirectoryPath);
}
else if (!string.IsNullOrEmpty(solutionExportsPathForMetadata))
{
    Log.Information("XrmMockup: Building combined metadata from solution exports at {Path}", solutionExportsPathForMetadata);

    // Comma-separated fully-qualified plugin type names to drop from the merged metadata —
    // for plugins that query Dataverse system tables XrmMockup doesn't model (e.g. "privilege").
    var excludedPluginTypeNames = builder.Configuration.GetValue<string>("SolutionExports:ExcludedPluginTypeNames")
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    metadataDirectoryPath = MetadataFolderBuilder.BuildCombinedMetadataFolder(solutionExportsPathForMetadata, excludedPluginTypeNames);
    Log.Information("XrmMockup: Combined metadata written to {Path}", metadataDirectoryPath);
}
else
{
    metadataDirectoryPath = "Metadata";
}

// Load the KF CRM plugin assembly (built separately for net462) so plugins actually execute
var pluginAssemblyPath = builder.Configuration.GetValue<string>("PluginAssembly:Path");
var basePluginTypes = XrmEmulator.Services.PluginAssemblyBridge.TryLoadBasePluginTypes(pluginAssemblyPath, Log.Logger);

// CodeActivity-derived types (e.g. local no-op stubs for ISV workflow activities) from the
// same plugin assembly, so classic workflows with custom code-activity steps can execute.
var codeActivityInstanceTypes = XrmEmulator.Services.PluginAssemblyBridge.TryLoadCodeActivityTypes(Log.Logger);

// Independently reads the same combined Metadata.xml for the /plugins dev-tool route
builder.Services.AddSingleton(new PluginRegistrationService(metadataDirectoryPath));

// Register XrmMockup365 instance
builder.Services.AddSingleton<XrmMockup365>(provider =>
{
    var settings = new XrmMockupSettings
    {
        BasePluginTypes = basePluginTypes,
        BaseCustomApiTypes = [],
        CodeActivityInstanceTypes = codeActivityInstanceTypes,
        EnableProxyTypes = false,
        IncludeAllWorkflows = true,
        MetadataDirectoryPath = metadataDirectoryPath,
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

// Initialize BU hierarchy and teams from OrgStructure.json after snapshot restore
builder.Services.AddHostedService<OrgStructureInitializer>();

// Add XML serialization services for SOAP controller
builder.Services.AddScoped<IRequestMapper, RequestMapper>();
builder.Services.AddScoped<IXmlRequestDeserializer, XmlRequestDeserializer>();
builder.Services.AddScoped<IXmlResponseSerializer, XmlResponseSerializer>();

// Configure plugin execution history persistence
builder.Services.Configure<PluginExecutionHistoryOptions>(options =>
{
    options.FilePath = builder.Configuration.GetValue<string>("PluginExecutionHistory:FilePath")
        ?? "./xrm-emulator-plugin-executions.jsonl";
    options.MaxEntries = builder.Configuration.GetValue<int>("PluginExecutionHistory:MaxEntries", 500);
});
builder.Services.AddSingleton<PluginExecutionHistoryStore>();

// Configure Custom API execution history persistence (manual-trigger dev tool, see CustomApiController)
builder.Services.Configure<CustomApiExecutionHistoryOptions>(options =>
{
    options.FilePath = builder.Configuration.GetValue<string>("CustomApiExecutionHistory:FilePath")
        ?? "./xrm-emulator-customapi-executions.jsonl";
    options.MaxEntries = builder.Configuration.GetValue<int>("CustomApiExecutionHistory:MaxEntries", 500);
});
builder.Services.AddSingleton<CustomApiExecutionHistoryStore>();

// Register solution metadata service and per-app organization service resolver
var solutionExportsPath = builder.Configuration.GetValue<string>("SolutionExports:Path");
builder.Services.AddSingleton(new SolutionMetadataService(solutionExportsPath));
builder.Services.AddSingleton(provider =>
    new OrganizationServiceResolver(
        provider.GetRequiredService<IOrganizationServiceAsync>(),
        provider.GetService<XrmMockup365>()));
builder.Services.AddSingleton<CustomApiExecutionService>();

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

// Serve static files before any other middleware to avoid route conflicts
app.UseStaticFiles();

// Add comprehensive request/response logging middleware
app.UseMiddleware<RequestResponseLoggingMiddleware>();

// Add authentication middleware (we'll implement this)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Map health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive");

// Welcome dashboard — tile overview linking to every page the emulator exposes.
app.MapGet("/", (SolutionMetadataService solutionMetadata) =>
    Results.Content(
        BuildWelcomePage(solutionMetadata.IsConfigured, app.Environment.IsDevelopment(), solutionMetadata.CustomApis.Count > 0),
        "text/html"));

app.MapGet("/XRMServices/2011/Organization.svc/web", (HttpContext context) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    context.Response.Headers.Append("WWW-Authenticate",
        $"Bearer authorization_uri={baseUrl}/organizations/,resource_id={baseUrl}/");
    context.Response.StatusCode = 401;
    return Task.CompletedTask;
});

// Eagerly construct so any on-disk history is loaded up front. (The live PluginExecutionAudit
// subscription is disabled — see PluginExecutionHistoryStore's file header.)
app.Services.GetRequiredService<PluginExecutionHistoryStore>();

// Restore snapshot on startup
var snapshotService = app.Services.GetRequiredService<ISnapshotService>();
if (snapshotService is SnapshotService snapshotServiceImpl)
{
    snapshotServiceImpl.RestoreSnapshot();
}

app.Run();

// Renders the "/" welcome dashboard: a tile per page the emulator exposes, matching the
// /crm app-picker's visual style (shares wwwroot/crm/styles.css) so the two feel like one app.
static string BuildWelcomePage(bool crmConfigured, bool swaggerEnabled, bool customApisConfigured)
{
    var tiles = new[]
    {
        ("Data Browser", "/debug/data", "Browse all entities and records as HTML tables.", true),
        ("CRM Emulator UI", "/crm", "Browse app modules with real forms, views, and sitemaps.", crmConfigured),
        ("Setup", "/debug/setup", "Create test users, teams, and seed data.", true),
        ("Custom APIs", "/customapis", "List and manually trigger Custom APIs — a stand-in for scheduled Cloud Flows that call them.", customApisConfigured),
        ("Swagger / API Docs", "/swagger", "Explore and call the Dataverse Fake API directly.", swaggerEnabled),
    };

    var html = new StringBuilder();
    html.AppendLine("<!DOCTYPE html>");
    html.AppendLine("<html>");
    html.AppendLine("<head>");
    html.AppendLine("<title>XRM Emulator</title>");
    html.AppendLine("<meta charset='utf-8'>");
    html.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1'>");
    html.AppendLine("<link rel='stylesheet' href='/crm/styles.css'>");
    html.AppendLine("</head>");
    html.AppendLine("<body>");
    html.AppendLine("<div class='app-picker'>");
    html.AppendLine("<h1>XRM Emulator</h1>");
    html.AppendLine("<p>Local Dataverse stand-in, powered by XrmMockup.</p>");
    html.AppendLine("<div class='app-grid'>");

    foreach (var (title, href, description, enabled) in tiles)
    {
        html.AppendLine($"<div class='app-card{(enabled ? "" : " app-card-disabled")}'>");
        if (enabled)
            html.AppendLine($"<a href='{href}'>");
        html.AppendLine($"<h2>{WebUtility.HtmlEncode(title)}</h2>");
        html.AppendLine($"<p>{WebUtility.HtmlEncode(description)}</p>");
        if (!enabled)
            html.AppendLine("<span class='entity-count'>Not configured</span>");
        if (enabled)
            html.AppendLine("</a>");
        html.AppendLine("</div>");
    }

    html.AppendLine("</div>");
    html.AppendLine("</div>");
    html.AppendLine("</body>");
    html.AppendLine("</html>");
    return html.ToString();
}

// Make the Program class public for testing
#pragma warning disable S1118 // Utility classes should not have public constructors
public partial class Program
{
}
#pragma warning restore S1118