# Command Bar & Ribbon

Manage command bar buttons (appactions) and hide ribbon buttons via solution import.

## Command Bar Buttons

```bash
# Stage a new command bar button
dotnet run --project src/XrmEmulator.MetadataSync -- commandbar <app> add <entity>

# Edit an existing command bar button (OOTB or custom)
dotnet run --project src/XrmEmulator.MetadataSync -- commandbar <app> edit <uniquename>
```

- `<app>` — AppModule name (e.g., the model-driven app name)
- `<entity>` — Target entity logical name
- `<uniquename>` — OOTB constant (e.g., `Mscrm.SubGrid.account.NewRecord`) or custom appaction uniquename

### Staging Flow

- **`add`** — Generates an appaction XML template at `_pending/appactions/<uniqueName>.xml` with contextentity (required for rendering), context=1 (entity), type=0, location=2.
- **`edit`** — Checks out existing appaction from solution export or by OOTB name to `_pending/appactions/<uniqueName>.xml`.

### Important Notes

- `fonticon` values must be prefixed with `$clientsvg:` (e.g., `$clientsvg:Add`, not just `Add`)
- `contextentity` lookup (to `entity` table) is **required** — the button won't render without it
- API-created appactions don't appear in the Command Designer UI but render at runtime
- Migrated appactions (origin=1) **cannot be deleted** — set `isdisabled=1` instead

## Ribbon Hiding (RibbonWorkbench)

```bash
# Hide a ribbon button via HideCustomAction
dotnet run --project src/XrmEmulator.MetadataSync -- ribbonworkbench hide <entity> <button-id>
```

- `<entity>` — Entity logical name
- `<button-id>` — Ribbon button ID (e.g., `Mscrm.SubGrid.account.AddNewStandard`)

The command checks if the button exists in `Ribbon/<entity>.xml` and warns if not found. It also checks if already hidden in `RibbonDiff.xml`.

Stages: `_pending/RibbonWorkbench/<entity>_hide_<safeid>.json`

On commit: imports a solution XML with `<HideCustomAction>` to hide the button.
