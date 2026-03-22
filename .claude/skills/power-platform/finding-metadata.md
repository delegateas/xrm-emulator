# Finding Metadata

Search patterns for locating solution components in synced environment data.

All paths below use `data/<env>/<solution-folder>/` as the environment root.

## Entities
```
# List all entities in a solution
Read Model/solution.md

# Get entity columns, types, relationships
Read Model/entities/<logicalname>.md

# Find entity by display name or logical name
Grep pattern="<search term>" path="data/" glob="*/Model/solution.md"
```

## Views (SavedQueries)
```
# Find all views for an entity
Glob pattern="data/<env>/<folder>/SolutionExport/*/Entities/<EntityName>/SavedQueries/*.xml"

# Search for a specific view by name
Grep pattern="<view name>" path="data/<env>/<folder>/SolutionExport" glob="*/SavedQueries/*.xml"

# Find views across all environments
Grep pattern="<view name>" path="data/" glob="**/SavedQueries/*.xml"
```

## Forms
```
# Find main forms for an entity
Glob pattern="data/<env>/<folder>/SolutionExport/*/Entities/<EntityName>/FormXml/main/*.xml"

# Find Quick Create forms
Glob pattern="data/<env>/<folder>/SolutionExport/*/Entities/<EntityName>/FormXml/quickCreate/*.xml"

# Search for a form by name
Grep pattern="<form name>" path="data/<env>/<folder>/SolutionExport" glob="**/FormXml/**/*.xml"
```

## Sitemaps
```
Glob pattern="data/<env>/<folder>/SolutionExport/*/AppModuleSiteMaps/*/AppModuleSiteMap.xml"
```

## Security Roles
```
# Overview of all roles
Read Model/security-roles.md

# Find specific role XML
Glob pattern="data/<env>/<folder>/SecurityRoles/*.xml"
Grep pattern="<role name>" path="data/<env>/<folder>/SecurityRoles/"
```

## Plugins
```
Read Model/plugins.md
Grep pattern="<entity name>" path="data/<env>/<folder>/Model/plugins.md"
```

## Option Sets
```
Grep pattern="<optionset name>" path="data/<env>/<folder>/Model/global-optionsets.md"
```

## Web Resources
```
Glob pattern="data/<env>/<folder>/SolutionExport/*/WebResources/*"
Grep pattern="<resource name>" path="data/<env>/<folder>/SolutionExport" glob="*/WebResources/*"
```

## Command Bar Buttons (AppActions)
```
# Check committed appactions
Glob pattern="data/<env>/<folder>/SolutionExport/_committed/appactions/*.xml"
```

## Ribbon Definitions
```
# Per-entity ribbon XML (from RetrieveEntityRibbonRequest)
Glob pattern="data/<env>/<folder>/Ribbon/*.xml"

# Ribbon customizations in solution export
Glob pattern="data/<env>/<folder>/SolutionExport/*/Entities/*/RibbonDiff.xml"
```

## Business Rules (Workflows)
```
Glob pattern="data/<env>/<folder>/SolutionExport/*/Workflows/BR-*.xaml"
Glob pattern="data/<env>/<folder>/Workflows/*.xaml"
```

## Comparing Environments

1. Read both `Model/solution.md` files to see entity lists side by side
2. Compare entity columns by reading both `Model/entities/<name>.md` files
3. Compare views/forms by reading XML from both `SolutionExport/` directories

```
Glob pattern="data/**/connection_metadata.json"   # discover all environments
Read data/<env-a>/<folder>/Model/solution.md
Read data/<env-b>/<folder>/Model/solution.md
```
