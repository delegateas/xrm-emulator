# Web Resources

Upload or edit web resources (JavaScript, CSS, HTML, XML, SVG).

## Commands

```bash
# Stage a new web resource for upload
dotnet run --project src/XrmEmulator.MetadataSync -- webresource new <webresource-name> <file-path> [--type js]

# Checkout an existing web resource for editing
dotnet run --project src/XrmEmulator.MetadataSync -- webresource checkout <webresource-name>
```

## Resource Types

| `--type` value | Dataverse type code | File type |
|----------------|-------------------|-----------|
| `html` | 1 | HTML |
| `xml` | 2 | XML |
| `js` (default) | 3 | JavaScript |
| `css` | 4 | CSS |
| `svg` | 11 | SVG |

## Staging Flow

- **`new`** — Copies the source file to `_pending/WebResources/<safename>.<ext>` and creates a JSON metadata marker at `_pending/WebResources/<safename>.json`. On commit: creates or updates the web resource in CRM and adds to the solution.
- **`checkout`** — Copies the existing web resource from `SolutionExport/` to `_pending/WebResources/` for editing.

## Recipes

### Upload a new JavaScript web resource
```bash
dotnet run --project src/XrmEmulator.MetadataSync -- webresource new prefix_/js/formhelper.js ./src/formhelper.js --type js
# Edit _pending/WebResources/ files if needed
# Tell the human to review and commit
```

### Edit an existing web resource
```bash
dotnet run --project src/XrmEmulator.MetadataSync -- webresource checkout prefix_/js/formhelper.js
# Edit _pending/WebResources/formhelper.js
# Tell the human to review and commit
```
