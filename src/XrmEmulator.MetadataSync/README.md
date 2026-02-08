# xrm-metadata-sync

Interactive CLI tool to sync Dataverse metadata into XrmMockup format for use with [XrmEmulator](https://github.com/delegateas/xrm-emulator).

## Install

```bash
dotnet tool install --global XrmEmulator.MetadataSync
```

## Usage

```bash
xrm-metadata-sync
```

Or run directly without installing:

```bash
dnx XrmEmulator.MetadataSync
```

The tool will interactively guide you through:

1. **Connection setup** - Choose between connection string, client secret, or interactive browser authentication
2. **Solution selection** - Pick a Dataverse solution to sync metadata from
3. **Entity selection** - Multi-select which entities to include (default XrmMockup entities are always included)
4. **Scope selection** - Choose what to sync: entity metadata, plugins, workflows, security roles, option sets, currencies
5. **Output directory** - Where to write the metadata files (default: `./Metadata`)

### CLI Arguments

You can pre-populate connection settings via CLI arguments or user secrets:

```bash
xrm-metadata-sync \
  --environment-url https://myorg.crm.dynamics.com \
  --client-id 00000000-0000-0000-0000-000000000000 \
  --client-secret mysecret \
  --tenant-id 00000000-0000-0000-0000-000000000000
```

## Output

The tool produces files in XrmMockup's expected format:

- `Metadata.xml` - Serialized entity metadata (DataContractSerializer)
- `Workflows/*.xml` - Individual workflow definitions
- `SecurityRoles/*.xml` - Individual security role definitions
- `TypeDeclarations.cs` - Security role GUID constants
