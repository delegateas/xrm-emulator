# Entities, Fields, Icons & Sitemaps

Checkout entity metadata, add new fields, deprecate fields, manage icons, and edit sitemaps.

## Entity Checkout

```bash
# Checkout entity definition for editing (display names, descriptions, etc.)
dotnet run --project src/XrmEmulator.MetadataSync -- entity <logical-name>
```

Copies `SolutionExport/.../Entities/<Name>/Entity.xml` → `_pending/Entities/<Name>/Entity.xml`. Edit custom field display names, descriptions, required level, max length.

## Add New Fields

```bash
dotnet run --project src/XrmEmulator.MetadataSync -- entity attribute add <entity> <attr-name> \
  --type <type> [--target <entity>] [--display-name <name>] \
  [--relationship <schema>] [--max-length <n>] [--required <level>]
```

| Flag | Required | Description |
|------|----------|-------------|
| `--type` | Yes | `lookup`, `string`, `memo`, `int`, `decimal`, `boolean`, `datetime` |
| `--target` | For lookups | Target entity logical name |
| `--display-name` | No | Display name (derived from attr name if omitted) |
| `--relationship` | No | Custom relationship schema name |
| `--max-length` | No | String max length |
| `--required` | No | `none` (default), `required`, `system`, `application` |

The attribute name must include the publisher prefix (e.g., `prefix_myfield`).

Stages: `_pending/Attributes/<entity>_<attrname>.attribute.json`

### Examples
```bash
# Add a string field
dotnet run --project src/XrmEmulator.MetadataSync -- entity attribute add account prefix_taxid --type string --max-length 20

# Add a lookup field
dotnet run --project src/XrmEmulator.MetadataSync -- entity attribute add contact prefix_parentaccount --type lookup --target account
```

## Deprecate Fields

```bash
dotnet run --project src/XrmEmulator.MetadataSync -- deprecate <entity> <attribute>
```

Prefixes the field's display name with "ZZ " so it sorts last and appears clearly deprecated.

Stages: `_pending/Deletes/<entity>_<attribute>.deprecate.json`

## Icons

```bash
# Upload a new SVG icon (optionally assign to entity)
dotnet run --project src/XrmEmulator.MetadataSync -- icon new <webresource-name> <svg-file-path> [--entity <logical-name>]

# Assign an existing web resource as entity icon
dotnet run --project src/XrmEmulator.MetadataSync -- icon set <entity-logical-name> <webresource-name>
```

- **`icon new`** — Copies SVG to `_pending/Icons/` + creates JSON marker. On commit: uploads as web resource (type 11). If `--entity` provided, also sets `IconVectorName` on entity metadata.
- **`icon set`** — Creates JSON marker in `_pending/Icons/`. On commit: sets `IconVectorName` (no upload — references existing web resource).

## Sitemaps

```bash
# Checkout a sitemap for editing
dotnet run --project src/XrmEmulator.MetadataSync -- sitemap <appmodule-name>
```

Copies `AppModuleSiteMaps/<name>/AppModuleSiteMap.xml` → `_pending/AppModuleSiteMaps/<name>/AppModuleSiteMap.xml`.

Edit by adding/removing/reordering `<SubArea>`, `<Group>`, `<Area>` elements. See **xml-formats.md** for the sitemap XML structure.
