var builder = DistributedApplication.CreateBuilder(args);

// Add XRM Emulator as a container from GitHub Container Registry.
// Mount a local metadata folder with your Dataverse entity definitions.
var xrmEmulator = builder.AddXrmEmulatorContainer("xrm-emulator")
    .WithMetadataFolder("../MyProject/Metadata")
    .WithSnapshotPersistence()
    .DisableIPv6();

// Optional: Add a license key to enable premium features (snapshots, plugins, multi-org).
// The license key can come from configuration, environment, or a .lic file.
// Without a license, core features (CRUD, OData, SOAP) still work.
var licenseKey = builder.Configuration["XrmEmulator:LicenseKey"];
if (!string.IsNullOrEmpty(licenseKey))
{
    xrmEmulator.WithLicenseKey(licenseKey);
}

builder.Build().Run();
