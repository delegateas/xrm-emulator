# Commit Flow & Pending Queue

How changes get from `_pending/` to CRM.

## List Pending Changes

```bash
# Show all staged items in _pending/
dotnet run --project src/XrmEmulator.MetadataSync -- pending
```

Displays a table of all pending items with type, description, and file path.

## Commit

```bash
# Interactive review and push to CRM
dotnet run --project src/XrmEmulator.MetadataSync -- commit [--debug]
```

The commit command:
1. Discovers all pending items in `_pending/`
2. Shows interactive multi-select for the human to choose which items to push
3. Resolves variable references (see below)
4. Pushes selected changes to CRM
5. Publishes customizations
6. Re-exports the solution to update `SolutionExport/`
7. Verifies round-trip (pending vs re-exported)
8. Cleans up verified items from `_pending/`; mismatched items are kept for review
9. Archives markers to `_committed/`

Use `--debug` to write detailed logs to `.metadatasync/logs/commit-<timestamp>.log`.

## All Component Types

The commit pipeline handles these 15 component types:

| Type | Pending path pattern | What it does |
|------|---------------------|-------------|
| **SavedQuery** (view) | `_pending/**/SavedQueries/*.xml` | Create or update view |
| **SystemForm** (form) | `_pending/**/FormXml/*.xml` | Create or update form |
| **Entity** | `_pending/**/Entities/*/Entity.xml` | Update entity metadata |
| **NewAttribute** | `_pending/**/Attributes/*.attribute.json` | Create new field |
| **Deprecate** | `_pending/**/*.deprecate.json` | Prefix field with "ZZ " |
| **Delete** | `_pending/**/Deletes/*.delete.json` | Delete view, form, or other component |
| **BusinessRule** (workflow) | `_pending/**/Workflows/*.xaml.data.xml` | Create or update business rule |
| **AppModuleEntity** | `_pending/**/AppModuleEntities/*.json` | Add entity to AppModule |
| **AppModuleView** | `_pending/**/AppModuleViews/*.json` | Configure views in AppModule |
| **AppModuleForm** | `_pending/**/AppModuleForms/*.json` | Configure forms in AppModule |
| **AppModuleSiteMap** | `_pending/**/AppModuleSiteMaps/*/AppModuleSiteMap.xml` | Update sitemap |
| **WebResource** | `_pending/**/WebResources/*.json` | Upload or update web resource |
| **Icon** | `_pending/**/Icons/*.json` | Upload icon / assign to entity |
| **AppAction** (command bar) | `_pending/**/appactions/*.xml` | Create or update command bar button |
| **RibbonWorkbench** | `_pending/**/RibbonWorkbench/*.json` | Hide ribbon button via solution import |

## Commit Order

The pipeline processes types in dependency order:
1. Entities and attributes first (schema must exist before components reference it)
2. AppModuleEntities before AppModuleViews/Forms (entity must be in app before views)
3. Variable references resolve producers before consumers (topological sort)

## Variable Replacement (Chained Commits)

When a pending file depends on another (e.g., a form subgrid needs a view's GUID), use variable references.

**Syntax:** `{{_pending/<relative-path>#<property>}}`

**Example:**
```xml
<ViewId>{{_pending/Entities/Account/SavedQueries/new_my-view.xml#id}}</ViewId>
```

The commit engine:
1. Scans all selected pending files for `{{_pending/...#...}}` patterns
2. Builds a dependency graph and topologically sorts
3. Commits producers first, captures their assigned GUIDs
4. Resolves references in dependent files before committing them
5. Circular dependencies fail with a clear error

**Output properties:**

| Component | Property | Value |
|-----------|----------|-------|
| SavedQuery (view) | `id` | `savedqueryid` assigned by CRM |
| SystemForm (form) | `id` | `formid` assigned by CRM |

Variable references also work in AppModuleViews and AppModuleForms JSON markers.

## Archived Artifacts

After commit, markers are moved to `_committed/` (organized by type). This serves as a history of what was pushed. The `_committed/` folder has subdirectories matching the pending structure: `AppModuleEntities/`, `AppModuleViews/`, `AppModuleForms/`, `Icons/`, `CommandBar/`, `Deletes/`, `Deprecates/`, `Attributes/`, `RibbonWorkbench/`, `WebResources/`, `appactions/`.
