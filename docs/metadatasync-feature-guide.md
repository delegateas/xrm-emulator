# MetadataSync Feature Development Guide

How to add new commands and component types to MetadataSync. Follow these patterns exactly — they exist for crash-resilience, auditability, and consistency.

## Core Design Principle

**Everything goes through `_pending/`.** No command ever writes directly to CRM. The flow is always:

```
Command (checkout/new/stage) → _pending/ file → user edits → commit → CRM → _committed/
```

This gives users a chance to review, modify, and batch changes before pushing.

## Architecture Overview

```
Program.cs              — Command routing + commit orchestration (top-level static methods)
Readers/                — Parse files from disk into strongly-typed Definition records
Writers/                — Push Definition records to CRM via SDK
Models/                 — Immutable record types for pending file formats
Commit/                 — Variable resolution and dependency ordering
Git/                    — Git integration helpers
```

## Adding a New Component Type

### Step 1: Define the Model

Create `Models/<ComponentName>Definition.cs`:

```csharp
namespace XrmEmulator.MetadataSync.Models;

// For XML-based components (views, forms, sitemaps, entities):
public record MyComponentDefinition
{
    public Guid ComponentId { get; init; }          // Guid.Empty = new
    public required string Name { get; init; }
    // ... component-specific fields
    public required string SourceFilePath { get; init; }
}

// For JSON marker files (appmodule configs, icons):
public record MyMarkerDefinition
{
    public required string SomeKey { get; init; }
    // ... fields serialized as camelCase JSON
}
```

**Convention:** XML components carry `SourceFilePath`. JSON markers don't (they're simple data bags).

### Step 2: Create the Reader

Create `Readers/<ComponentName>FileReader.cs`:

```csharp
namespace XrmEmulator.MetadataSync.Readers;

public static class MyComponentFileReader
{
    // Primary: parse from file path
    public static MyComponentDefinition Parse(string filePath)
    {
        var doc = XDocument.Load(filePath);
        return ParseDocument(doc, filePath);
    }

    // For variable resolution: parse from string content
    public static MyComponentDefinition ParseFromString(string xmlContent, string sourceFilePath)
    {
        var doc = XDocument.Parse(xmlContent);
        return ParseDocument(doc, sourceFilePath);
    }

    private static MyComponentDefinition ParseDocument(XDocument doc, string sourceFilePath)
    {
        // Parse XML → Definition record
        // ID fields should be Guid.Empty when absent (new items)
    }
}
```

**Pattern:** Always static class. Always `Parse(string filePath)`. Add `ParseFromString` if the component supports variable references.

### Step 3: Create the Writer

Create `Writers/<ComponentName>Writer.cs`:

```csharp
using Microsoft.Crm.Sdk.Messages;  // Prefer typed SDK messages
using Microsoft.Xrm.Sdk;

namespace XrmEmulator.MetadataSync.Writers;

public static class MyComponentWriter
{
    public static Guid Create(IOrganizationService service, MyComponentDefinition def,
        string entityLogicalName, string? solutionUniqueName)
    {
        var entity = new Entity("myentity");
        entity["name"] = def.Name;
        // ... set fields

        var id = service.Create(entity);

        // Add to solution (separate call)
        if (!string.IsNullOrEmpty(solutionUniqueName))
        {
            service.Execute(new AddSolutionComponentRequest
            {
                ComponentId = id,
                ComponentType = <int>,  // CRM component type code
                SolutionUniqueName = solutionUniqueName
            });
        }

        return id;
    }

    public static void Update(IOrganizationService service, MyComponentDefinition def)
    {
        var entity = new Entity("myentity", def.ComponentId);
        // ... set fields
        service.Update(entity);
    }
}
```

**SDK rule:** Use typed SDK message classes from `Microsoft.Crm.Sdk.Messages` (e.g., `AddAppComponentsRequest`, `UpdateAttributeRequest`, `AddSolutionComponentRequest`). Never use untyped `OrganizationRequest` with string-keyed parameters.

### Step 4: Add CommitItemType

In the `CommitItemType` enum (bottom of Program.cs), add your new type:

```csharp
enum CommitItemType
{
    SavedQuery, SystemForm, SiteMap, Entity,
    IconUpload, IconSet,
    AppModuleEntity, AppModuleView, AppModuleForm,
    MyComponent  // ← add here
}
```

### Step 5: Wire into the Commit Flow

In `HandleCommitCommand()` in Program.cs, add three blocks:

#### 5a. File discovery (scan `_pending/`)

```csharp
var pendingMyFiles = Directory.GetFiles(pendingDir, "*.xml", SearchOption.AllDirectories)
    .Where(f => f.Contains("MyFolder", StringComparison.OrdinalIgnoreCase))
    .ToList();
```

Add to `totalPending` count.

#### 5b. Build commit items

```csharp
foreach (var f in pendingMyFiles)
{
    // For files that may contain variables, defer full parsing:
    if (PendingVariableResolver.HasVariables(f))
    {
        // Extract display info without deserializing GUIDs
        // Store null! as ParsedData — will re-parse from resolvedContent in the switch
        commitItems.Add(new CommitItem(CommitItemType.MyComponent, displayLabel, f, null!));
    }
    else
    {
        var parsed = MyComponentFileReader.Parse(f);
        commitItems.Add(new CommitItem(CommitItemType.MyComponent, displayLabel, f, parsed));
    }
}
```

#### 5c. Processing switch case

```csharp
case CommitItemType.MyComponent:
{
    // Re-parse with resolved variables if needed
    var def = resolvedContent != null
        ? MyComponentFileReader.ParseFromString(resolvedContent, item.FilePath)
        : (MyComponentDefinition)item.ParsedData;

    var isNew = def.ComponentId == Guid.Empty;
    if (isNew)
    {
        Log($"Creating: {item.DisplayName}");
        var newId = MyComponentWriter.Create(client, def, entityLogicalName, metadata.Solution.UniqueName);
        Log($"  Created OK. ID: {newId}");
        resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = newId.ToString() };
    }
    else
    {
        Log($"Updating: {item.DisplayName}");
        MyComponentWriter.Update(client, def);
        Log($"  Updated OK.");
        resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = def.ComponentId.ToString() };
    }
    break;
}
```

**Important:** The per-item archiving, `_outputs.json` write, and error handling happen *after* the switch — don't duplicate that logic inside the case.

### Step 6: Add the Command

In Program.cs, add your command handler:

**For checkout (existing component):**
```csharp
static void HandleMyComponentCheckout(string id, string baseDir, string pendingDir)
{
    // 1. Find file in SolutionExport/ snapshot
    // 2. Copy to _pending/ preserving relative path
    // 3. Print confirmation
}
```

**For new/scaffold:**
```csharp
static void HandleMyComponentNew(string entity, string name, string baseDir, string pendingDir)
{
    // 1. Generate minimal scaffold XML/JSON
    // 2. Write to _pending/<appropriate path>
    // 3. Print confirmation with edit instructions
}
```

**For JSON marker commands (like appmodule views/forms):**
```csharp
static void HandleMyMarkerCommand(...)
{
    // 1. Scan snapshot for available components
    // 2. Show interactive prompt (MultiSelectionPrompt)
    // 3. Build marker Definition record
    // 4. Serialize to JSON with camelCase policy
    // 5. Write to _pending/<MarkerFolder>/<name>.json
}
```

### Step 7: Add Verification (if applicable)

In the post-re-export verification section, add handling for your type. For types with snapshots (XML-based), compare `_committed/` against the re-exported snapshot. For JSON markers and new items, just confirm archived.

## File Format Conventions

| Component Type | Pending Format | Location in `_pending/` |
|----------------|---------------|------------------------|
| Views (SavedQuery) | XML | `Entities/<Entity>/SavedQueries/<guid-or-name>.xml` |
| Forms (SystemForm) | XML | `Entities/<Entity>/FormXml/main/<guid-or-name>.xml` |
| SiteMaps | XML | `AppModuleSiteMaps/<AppName>/AppModuleSiteMap.xml` |
| Entities | XML | `Entities/<Entity>/Entity.xml` |
| Icon uploads | JSON + SVG | `Icons/<name>.json` + `Icons/<name>.svg` |
| Icon assignments | JSON | `Icons/<entity>.icon.json` |
| AppModule entities | JSON | `AppModuleEntities/<app>_<entity>.json` |
| AppModule views | JSON | `AppModuleViews/<app>_<entity>.json` |
| AppModule forms | JSON | `AppModuleForms/<app>_<entity>.json` |

**XML components** mirror the SolutionExport folder structure so relative paths work for variable references.

**JSON markers** go in their own top-level folders under `_pending/`.

## Variable System

Any pending file can reference another pending file's output:

```
{{_pending/Entities/Account/SavedQueries/new_afdelinger.xml#id}}
```

The commit engine resolves these by:
1. Topological sort — dependencies committed first
2. After each CRM call, outputs (e.g., `id`) stored in `resolvedOutputs` dictionary
3. Dependent files have variables replaced before their CRM call

**To support variables in a new type:**
- Add `ParseFromString(string content, string filePath)` to the Reader
- In the commit switch: use `resolvedContent` when non-null
- For JSON types with `List<Guid>`: defer deserialization when `HasVariables` is true (extract display info with `JsonDocument` instead)

## Crash Resilience

The commit flow is crash-resilient:
- After each successful CRM call → file moved to `_committed/` immediately
- `_outputs.json` updated atomically (write `.tmp` + rename) after each item
- On resume: `_outputs.json` restores resolved outputs, files already in `_committed/` are skipped
- `_outputs.json` only deleted on full success — never on failure

**Never** delete `_outputs.json` unless all items succeeded.

## Checklist for New Features

- [ ] Model record in `Models/`
- [ ] Reader in `Readers/` with `Parse()` and optionally `ParseFromString()`
- [ ] Writer in `Writers/` using typed SDK messages
- [ ] `CommitItemType` enum value
- [ ] File discovery in commit flow
- [ ] Commit item building (handle variables with `HasVariables` check)
- [ ] Processing switch case (use `resolvedContent` for variable support)
- [ ] Command handler in Program.cs
- [ ] Verification handling in post-re-export section
- [ ] SKILL.md updated with new command documentation
- [ ] `--help` text for the new command
- [ ] `dotnet build` passes
