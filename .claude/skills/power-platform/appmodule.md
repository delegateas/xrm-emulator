# AppModule Configuration

Commands for managing which entities, views, and forms appear in a Dataverse model-driven app.

## Commands

```bash
# Add an entity to an AppModule (required before views/forms can be configured)
dotnet run --project src/XrmEmulator.MetadataSync -- appmodule entity add <entity-logical-name> [--app <appmodule-name>] [--all-views]

# Configure which views appear in an AppModule (interactive multi-select)
dotnet run --project src/XrmEmulator.MetadataSync -- appmodule views <entity-logical-name> [--app <appmodule-name>]

# Configure which forms appear in an AppModule (interactive multi-select)
dotnet run --project src/XrmEmulator.MetadataSync -- appmodule forms <entity-logical-name> [--app <appmodule-name>]

# List AppModule components (entities, views, forms, sitemaps)
dotnet run --project src/XrmEmulator.MetadataSync -- appmodule list [--app <appmodule-name>]
```

## Entity Registration Flow

1. **`appmodule entity add <entity> [--app <name>] [--all-views]`** — Checks if entity is already in AppModule (type=1 in AppModule.xml). If not, stages a JSON marker to `_pending/AppModuleEntities/<app>_<entity>.json`. With `--all-views`, also stages an AppModuleViews JSON with all local views.
2. On commit: calls `AddAppComponents` with component type 1 (Entity). Processed **before** AppModuleViews so views can reference the entity.

## View Configuration Flow

1. **`appmodule views <entity> [--app <name>]`** — Scans local solution exports and `_pending/` for views matching the entity. Shows interactive multi-select with currently configured views pre-selected. Stages JSON marker to `_pending/AppModuleViews/<app>_<entity>.json`. **Auto-adds the entity** if not already present.
2. On commit: diffs against current AppModule.xml, calls `AddAppComponents`/`RemoveAppComponents` for view component type 26.

## Form Configuration Flow

1. **`appmodule forms <entity> [--app <name>]`** — Scans local solution exports and `_pending/` for forms (FormXml/main/*.xml) matching the entity. Shows interactive multi-select with currently configured forms pre-selected. Stages JSON marker to `_pending/AppModuleForms/<app>_<entity>.json`. **Auto-adds the entity** if not already present.
2. On commit: diffs against current AppModule.xml, calls `AddAppComponents`/`RemoveAppComponents` for form component type 60.
