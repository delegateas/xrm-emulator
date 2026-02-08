var builder = DistributedApplication.CreateBuilder(args);

// Add XRM Emulator as a container from GitHub Container Registry.
// Mount a local metadata folder with your Dataverse entity definitions.
var xrmEmulator = builder.AddXrmEmulatorContainer("xrm-emulator")
    .WithMetadataFolder("../MyProject/Metadata")
    .WithSnapshotPersistence()
    .DisableIPv6();

builder.Build().Run();
