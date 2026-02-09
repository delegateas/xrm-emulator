# XrmEmulator.Aspire.Hosting.Dataverse

.NET Aspire hosting integration for [XrmEmulator](https://github.com/delegateas/xrm-emulator), a local Dataverse emulator powered by XrmMockup.

## Usage

Add the package to your Aspire AppHost project:

```bash
dotnet add package XrmEmulator.Aspire.Hosting.Dataverse
```

### Container resource

Run XrmEmulator as a container from the GitHub Container Registry:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var emulator = builder.AddXrmEmulatorContainer("dataverse")
    .WithMetadataFolder("../Metadata")
    .WithSnapshotPersistence();

builder.Build().Run();
```

### Project resource

Reference the XrmEmulator project directly in your AppHost:

```csharp
var emulator = builder.AddProject<Projects.XrmEmulator>("dataverse")
    .WithSnapshotPersistence();
```

## Extension methods

| Method | Description |
|--------|-------------|
| `AddXrmEmulatorContainer(name, tag)` | Add the emulator as a container resource |
| `WithMetadataFolder(path)` | Bind mount a local metadata directory into the container |
| `WithSnapshotPersistence(interval, dataPath)` | Enable save/restore of database state across restarts |
| `WithoutSnapshotPersistence()` | Explicitly disable snapshots (useful for tests) |
| `WithLicenseKey(key)` | Configure a license key via environment variable |
| `WithLicenseFile(path)` | Configure a license key from a `.lic` file |
| `DisableIPv6()` | Avoid slow localhost connections on Windows |

## Metadata

Entity metadata files are required for the emulator to function. Use the [XrmEmulator.MetadataSync](https://www.nuget.org/packages/XrmEmulator.MetadataSync) CLI tool to export metadata from a Dataverse environment:

```bash
dnx XrmEmulator.MetadataSync
```

This will interactively guide you through authenticating with your Microsoft tenant, selecting a Dataverse environment, and choosing which entities to sync. The exported metadata files can then be mounted into the emulator using `WithMetadataFolder`.
