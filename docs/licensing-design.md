# XrmEmulator Licensing System - Architecture Design

## Overview

Offline license validation using Ed25519 asymmetric cryptography. A private key (kept by the project maintainers) signs license payloads. The public key is embedded in the emulator to verify licenses. No call-home, no shared infrastructure.

## Design Decisions

### Algorithm: Ed25519
- 64-byte signatures (vs RSA's 256+ bytes) = shorter license keys
- Built into .NET via `System.Security.Cryptography.Ed25519`  (.NET 10+)
- Deterministic signing, no padding oracle attacks
- Fast verification

### License Format
A license key is a single string: `base64url(json_payload).base64url(signature)`

**Payload Schema:**
```json
{
  "lid": "a1b2c3d4-...",          // License ID (GUID)
  "sub": "Contoso Corp",          // Licensee / subject
  "iat": "2025-06-01T00:00:00Z",  // Issued at
  "exp": "2026-06-01T00:00:00Z",  // Expiry (null = perpetual)
  "features": ["core", "snapshots", "multi-org"],  // Licensed features
  "seats": 0,                     // 0 = unlimited
  "meta": {}                      // Optional custom metadata
}
```

**Example license string** (shortened):
```
eyJsaWQiOiI...payload...fQ.c2lnbmF0dXJl...signature...
```

### Feature Tiers (Initial)
| Feature Key      | Description                        | Free | Licensed |
|------------------|------------------------------------|------|----------|
| `core`           | Basic CRUD, OData, SOAP endpoints  | Yes  | Yes      |
| `snapshots`      | Snapshot save/restore              | No   | Yes      |
| `multi-org`      | Multiple organization instances    | No   | Yes      |
| `plugins`        | Plugin execution support           | No   | Yes      |

> Features are extensible - new feature keys can be added without changing the format.
> The `core` feature is always available even without a license (open core model).

### No Machine Binding
Licenses are tied to an organization/licensee, not a machine. This avoids friction for dev teams.

## Project Structure

### New Projects

```
src/
├── XrmEmulator.Licensing/              # Shared validation library
│   ├── XrmEmulator.Licensing.csproj
│   ├── License.cs                      # License model
│   ├── LicenseValidator.cs             # Ed25519 verification + parsing
│   ├── ILicenseProvider.cs             # Interface for DI
│   ├── LicenseProvider.cs              # Implementation (resolves + caches license)
│   ├── LicenseFeatures.cs              # Feature key constants
│   └── Keys/
│       └── public.key                  # Embedded resource - Ed25519 public key
│
├── XrmEmulator.LicenseGenerator/       # CLI tool (dotnet tool)
│   ├── XrmEmulator.LicenseGenerator.csproj
│   ├── Program.cs                      # CLI entry point
│   └── Commands/
│       ├── GenerateKeysCommand.cs      # One-time key pair generation
│       ├── CreateLicenseCommand.cs     # Sign a new license
│       ├── ValidateLicenseCommand.cs   # Verify an existing license
│       └── ListFeaturesCommand.cs      # List available feature keys
```

### Modified Projects

```
src/XrmEmulator/
├── Program.cs                          # Add ILicenseProvider to DI
├── Middleware/
│   └── LicenseMiddleware.cs            # NEW: Check license on feature-gated endpoints
└── XrmEmulator.csproj                  # Reference XrmEmulator.Licensing

src/Aspire.Hosting.XrmEmulator/
├── XrmEmulatorExtensions.cs            # Add WithLicenseKey() extension
└── Aspire.Hosting.XrmEmulator.csproj   # Reference XrmEmulator.Licensing
```

## Component Design

### 1. XrmEmulator.Licensing (Shared Library)

**License.cs** - The license model:
```csharp
namespace XrmEmulator.Licensing;

public sealed record License
{
    public required Guid LicenseId { get; init; }
    public required string Subject { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required IReadOnlyList<string> Features { get; init; }
    public int Seats { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow;
    public bool HasFeature(string feature) => Features.Contains(feature, StringComparer.OrdinalIgnoreCase);
}
```

**LicenseValidator.cs** - Stateless verification:
```csharp
namespace XrmEmulator.Licensing;

public static class LicenseValidator
{
    // Public key loaded from embedded resource at static init
    private static readonly byte[] PublicKey = LoadEmbeddedPublicKey();

    public static LicenseValidationResult Validate(string licenseKey)
    {
        // 1. Split on '.'
        // 2. Base64url-decode payload and signature
        // 3. Verify Ed25519 signature against payload bytes using PublicKey
        // 4. Deserialize JSON payload
        // 5. Check expiry
        // Return License or error
    }
}

public sealed record LicenseValidationResult
{
    public bool IsValid { get; init; }
    public License? License { get; init; }
    public string? Error { get; init; }
}
```

**ILicenseProvider.cs** - DI interface:
```csharp
namespace XrmEmulator.Licensing;

public interface ILicenseProvider
{
    License? CurrentLicense { get; }
    bool IsFeatureLicensed(string feature);
    LicenseValidationResult ValidationResult { get; }
}
```

**LicenseProvider.cs** - Resolves license from multiple sources:
```csharp
// Resolution order:
// 1. XRMEMULATOR_LICENSE environment variable
// 2. XRMEMULATOR_LICENSE_FILE environment variable (path to .lic file)
// 3. Configuration key "License:Key"
// 4. ./xrm-emulator.lic file in working directory

// Caches the validated license at startup
// Logs license status (licensed to, features, expiry)
```

**LicenseFeatures.cs** - Constants:
```csharp
namespace XrmEmulator.Licensing;

public static class LicenseFeatures
{
    public const string Core = "core";
    public const string Snapshots = "snapshots";
    public const string MultiOrg = "multi-org";
    public const string Plugins = "plugins";
}
```

### 2. XrmEmulator.LicenseGenerator (CLI Tool)

**Packaged as dotnet tool** (matching MetadataSync pattern):
```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>xrm-license</ToolCommandName>
<PackageId>XrmEmulator.LicenseGenerator</PackageId>
```

**Uses Spectre.Console** (matching MetadataSync pattern) for interactive CLI.

**Commands:**

```
xrm-license generate-keys [--output <dir>]
  → Generates Ed25519 key pair
  → Saves private.key and public.key to specified directory
  → Warns if keys already exist
  → Sets restrictive file permissions on private key

xrm-license create --licensee <name> --features <f1,f2,...> [--expires <date>] [--private-key <path>]
  → Reads private key from file (default: ./private.key)
  → Creates and signs license payload
  → Outputs license string to stdout
  → Optionally writes to .lic file

xrm-license validate <license-key> [--public-key <path>]
  → Validates a license key
  → Shows payload details (licensee, features, expiry)
  → Reports valid/invalid/expired

xrm-license list-features
  → Lists all known feature keys with descriptions
```

### 3. XrmEmulator Integration

**Program.cs additions:**
```csharp
// Register license provider
builder.Services.AddSingleton<ILicenseProvider, LicenseProvider>();

// After build:
var license = app.Services.GetRequiredService<ILicenseProvider>();
logger.Information("License: {Status}", license.CurrentLicense != null
    ? $"Licensed to {license.CurrentLicense.Subject} ({string.Join(", ", license.CurrentLicense.Features)})"
    : "Unlicensed (core features only)");
```

**Feature gating in controllers** (example):
```csharp
// In SnapshotController:
public class SnapshotController(ILicenseProvider license, ...) : ControllerBase
{
    [HttpPost("save")]
    public IActionResult Save()
    {
        if (!license.IsFeatureLicensed(LicenseFeatures.Snapshots))
            return StatusCode(402, new { error = "Feature 'snapshots' requires a license" });
        // ... existing logic
    }
}
```

**No middleware blocker** - individual endpoints check features. This keeps the core always accessible.

### 4. Aspire Hosting Extension

```csharp
// New extension method:
public static IResourceBuilder<T> WithLicenseKey<T>(
    this IResourceBuilder<T> builder,
    string licenseKey) where T : IResourceWithEnvironment
{
    return builder.WithEnvironment("XRMEMULATOR_LICENSE", licenseKey);
}

// Also support reading from file:
public static IResourceBuilder<T> WithLicenseFile<T>(
    this IResourceBuilder<T> builder,
    string licenseFilePath) where T : IResourceWithEnvironment
{
    var key = File.ReadAllText(licenseFilePath).Trim();
    return builder.WithEnvironment("XRMEMULATOR_LICENSE", key);
}
```

**Updated sample AppHost:**
```csharp
var xrmEmulator = builder.AddXrmEmulatorContainer("xrm-emulator")
    .WithMetadataFolder("../MyProject/Metadata")
    .WithLicenseKey(builder.Configuration["XrmEmulator:LicenseKey"]!)
    .WithSnapshotPersistence()
    .DisableIPv6();
```

### 5. Docker Support

License passed via environment variable - already supported by the design:
```bash
docker run -e XRMEMULATOR_LICENSE=<key> ghcr.io/delegateas/xrm-emulator
```

No Dockerfile changes needed.

## Security Considerations

1. **Private key storage**: The generator tool saves with `600` permissions. Never committed to repo.
2. **Public key is not secret**: Embedding it in the binary is fine - it can only verify, not create licenses.
3. **Tampering resistance**: Modifying the payload invalidates the signature. Replacing the public key requires rebuilding from source (acceptable for OSS).
4. **Clock manipulation**: Expiry checks use UTC. Users could bypass by changing system clock - acceptable for a dev tool (not DRM).
5. **No obfuscation**: This is a licensing system, not DRM. It's meant to be honest enforcement for paying customers, not tamper-proof.

## Graceful Degradation

| Scenario                    | Behavior                                          |
|-----------------------------|---------------------------------------------------|
| No license                  | Core features work. Licensed features return 402. |
| Valid license               | All licensed features enabled.                    |
| Expired license             | Same as no license + warning log on startup.      |
| Invalid/tampered license    | Same as no license + error log on startup.        |
| License with unknown feature| Ignored - forward compatible.                     |

## Project License: LGPL v3 + Commercial Dual License

Following the [Hangfire licensing model](https://github.com/HangfireIO/Hangfire), XrmEmulator uses a dual-license approach that provides legal enforcement against unauthorized modification (e.g., stripping license checks).

### How It Works

**LGPL v3 (Free/Open Source path):**
- Anyone can USE the emulator as-is, even commercially
- If you MODIFY the library code (e.g., remove license checks), you MUST publish your modifications under LGPL v3
- This creates a legal deterrent: removing license checks = public fork showing you did that = embarrassing + legally actionable
- Combined Works (your app using XrmEmulator as a dependency) remain under your own license

**Commercial License (Paid path):**
- Grants the right to make private modifications (including forks)
- But commercial licensees also get a real license key, so no need to strip checks
- Covers redistribution as part of proprietary products

### License Files Structure

```
Repository Root:
├── LICENSE.md              # Multi-license chooser (short overview)
├── COPYING                 # GNU GPL v3 full text (required by LGPL)
├── COPYING.LESSER          # GNU LGPL v3 full text (the open-source license)
└── LICENSE_COMMERCIAL      # Commercial EULA for XrmEmulator
```

### Key Legal Protections

1. **LGPL copyleft on modifications**: Any modification to XrmEmulator source must be released under LGPL. Private forks are a copyright violation.
2. **Commercial EULA Section (Prohibited Uses)**: Cannot redistribute as a development tool/library. Cannot remove copyright notices or license checks.
3. **Commercial EULA Section (Modifications)**: Source modifications are permitted only under the commercial license terms.

### .csproj License Changes

All project files switch from MIT to LGPL-3.0-or-later:
```xml
<PackageLicenseExpression>LGPL-3.0-or-later</PackageLicenseExpression>
```

### What This Means for Users

| Use Case | License Required |
|----------|-----------------|
| Use XrmEmulator as-is in your project | Free (LGPL) |
| Use XrmEmulator container in CI/CD | Free (LGPL) |
| Report bugs, contribute PRs | Free (LGPL) |
| Fork and modify privately | Commercial license required |
| Remove/bypass license checks | Commercial license required (or LGPL violation) |
| Redistribute as part of a competing product | Commercial license required |

## Implementation Order

1. Project license files (LGPL v3 + Commercial EULA)
2. `XrmEmulator.Licensing` library (validation, models, provider)
3. `XrmEmulator.LicenseGenerator` CLI tool (key gen, license creation)
4. Integration into `XrmEmulator` (DI, feature checks)
5. Integration into `Aspire.Hosting.XrmEmulator` (extension methods)
6. Update sample AppHost
