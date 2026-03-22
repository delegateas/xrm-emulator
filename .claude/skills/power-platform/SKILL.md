---
name: power-platform
description: >
  Use when working with Dataverse, Power Platform, CRM entities, views, forms,
  sitemaps, security roles, plugins, option sets, solutions, or MetadataSync.
  Also for comparing environments or modifying solution components.
allowed-tools: Read, Grep, Glob, Bash
---

# Power Platform / MetadataSync Skill

You have access to locally synced Dataverse solution metadata via MetadataSync. Match your task below and read the linked file for details.

| Task | Read |
|------|------|
| Find entities, views, forms, roles, plugins, option sets | finding-metadata.md |
| Create, edit, or delete views | views.md |
| Create, edit, or delete forms (main + Quick Create) | forms.md |
| Entity metadata, new fields, deprecation, icons, sitemaps | entities.md |
| Web resource upload or editing (JS, CSS, HTML, SVG) | webresources.md |
| Command bar buttons and ribbon hiding | commandbar.md |
| Business rules (field defaults, conditions, visibility) | business-rules.md |
| Configure AppModule entities, views, or forms | appmodule.md |
| Understand the commit flow, pending list, all component types | commit.md |
| XML format reference (views, sitemaps) | xml-formats.md |
| Setup: git-init, hooks, agent init, MCP server | tooling.md |

## Synced Environments

The `data/` folder is created by running a MetadataSync full sync (`dotnet run --project src/XrmEmulator.MetadataSync`) and selecting an output directory. The convention is `data/<env-alias>/<solution-folder>/`. Each environment root contains a `.metadatasync/connection_metadata.json` with CRM URL, solution name/ID, and auth settings.

**Before doing any metadata work**, check if environments are synced:
```
Glob pattern="data/**/connection_metadata.json"
```
If no results are found, ask the developer to sync an environment first using `dotnet run --project src/XrmEmulator.MetadataSync`. This requires Dataverse access credentials.

## Data Layout

Each environment has this structure:

```
├── .metadatasync/
│   └── connection_metadata.json      # CRM URL, solution name/ID, auth settings
├── Metadata.xml                      # ⚠ 40MB+ — NEVER read this file
├── Model/
│   ├── solution.md                   # ★ START HERE — entity summary table
│   ├── global-optionsets.md          # All global option set values
│   ├── plugins.md                    # Plugin step registrations
│   ├── security-roles.md            # Security role privilege matrix
│   └── entities/
│       └── <logicalname>.md          # ★ Per-entity: columns, types, relationships
├── SolutionExport/
│   └── <SolutionName>/
│       ├── Entities/<EntityName>/
│       │   ├── Entity.xml            # Full entity definition XML
│       │   ├── SavedQueries/{guid}.xml  # View definitions
│       │   ├── FormXml/              # Form definitions (card, main, quick, quickCreate)
│       │   └── RibbonDiff.xml        # Ribbon customizations
│       ├── AppModuleSiteMaps/<AppName>/AppModuleSiteMap.xml
│       ├── AppModules/
│       ├── OptionSets/
│       ├── WebResources/
│       └── Other/Relationships/
├── Ribbon/                           # Per-entity ribbon XML exports
├── SecurityRoles/                    # Individual security role XML files
└── Workflows/                        # Workflow definition XML files
```

## Reading Priority

Always start with the smallest, most readable file:

1. **`Model/solution.md`** — entity list and overview
2. **`Model/entities/<name>.md`** — columns, types, option sets, relationships
3. **`Model/global-optionsets.md`** — option set values
4. **`Model/plugins.md`** — plugin registrations
5. **`Model/security-roles.md`** — role privilege matrix
6. **`SolutionExport/` XML** — only when you need raw definitions

**NEVER read `Metadata.xml`** — it is 40MB+ and will flood context.

## Agent ↔ Human Workflow

MetadataSync uses a **pending queue** pattern for all changes to Dataverse/CRM:

1. **Agent stages changes** — use CLI commands to add items to `_pending/`. Then edit the pending files as needed.
2. **Human reviews and commits** — the developer runs `commit` to review, approve, and push changes to CRM.

The agent **never commits directly**. The CLI handles the deterministic write to the environment — this ensures correctness and lets the human verify before anything touches CRM. This pattern is tested and proven. Do not try to work around it (e.g., calling the Dataverse API directly, running commit yourself, or writing to `SolutionExport/`).

See **commit.md** for full details on the commit flow, pending list, and all supported component types.

## Safety Rules

**Hooks in `.claude/settings.json` enforce these rules:**

1. **`SolutionExport/` is READ-ONLY** — use checkout commands to copy to `_pending/` first
2. **`_pending/` is WRITE-PROTECTED from direct creation** — only MetadataSync commands create files there; you can edit existing files after checkout
3. **Never invent GUIDs** — new views/forms intentionally have no ID; Dataverse assigns it on commit

## Global CLI Flags

| Flag | Purpose |
|------|---------|
| `--help`, `-h` | Show command help (works at any level) |
| `--no-cache` | Force re-authentication (skip token cache) |
| `--debug` | Enable detailed logging (commit command) |

All commands are run from the environment directory via:
```bash
dotnet run --project src/XrmEmulator.MetadataSync -- <command> [args]
```
