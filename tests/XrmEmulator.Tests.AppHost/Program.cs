using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Use XrmMockup's own test metadata which includes all required intersect entities.
var metadataPath = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", "..", "external", "XrmMockup", "tests", "XrmMockup365Test", "Metadata"));

builder.AddProject<Projects.XrmEmulator>("xrm-emulator")
    .WithEnvironment("XrmMockup__MetadataDirectoryPath", metadataPath)
    .WithoutSnapshotPersistence()
    .DisableIPv6()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
