# Forms (SystemForms)

Create, edit, or delete Dataverse forms — both main forms and Quick Create forms.

## Commands

```bash
# Scaffold a new main form
dotnet run --project src/XrmEmulator.MetadataSync -- forms main new <entity> --name "<name>" [--copy-from <guid>]

# Checkout an existing main form
dotnet run --project src/XrmEmulator.MetadataSync -- forms main edit <form-guid>

# Scaffold a new Quick Create form
dotnet run --project src/XrmEmulator.MetadataSync -- forms quickcreate new <entity> --name "<name>" [--copy-from <guid>]

# Checkout an existing Quick Create form
dotnet run --project src/XrmEmulator.MetadataSync -- forms quickcreate edit <form-guid>

# Delete a form from CRM
dotnet run --project src/XrmEmulator.MetadataSync -- forms delete <form-guid>

# Backward compat: same as `forms main edit <guid>`
dotnet run --project src/XrmEmulator.MetadataSync -- forms <form-guid>
```

## Form Types

| Subcommand | FormType | Stored in |
|------------|----------|-----------|
| `main` | 2 | `FormXml/main/` |
| `quickcreate` | 7 | `FormXml/quickCreate/` |

## Checkout Flow

- **`new`** — Scaffolds a form XML in `_pending/Entities/<Name>/FormXml/{main|quickCreate}/new_<safename>.xml`. With `--copy-from`, clones an existing form (strips formid + ancestor, replaces name). Without, creates a minimal single-tab form. New forms have **no formid** — Dataverse assigns it on commit.
- **`edit`** — Copies from `SolutionExport/` → `_pending/`
- **`delete`** — Stages delete marker: `_pending/Deletes/systemform_<guid>.delete.json`

## Editing Guide

After checkout, edit the XML in `_pending/`:

**Add/remove tabs, sections, rows, cells** — these are the structural building blocks:
```xml
<tab name="tab_general">
  <labels><label description="General" languagecode="1030" /></labels>
  <columns><column width="100%">
    <sections><section name="section_info">
      <labels><label description="Information" languagecode="1030" /></labels>
      <rows>
        <row><cell><control id="fieldname" classid="{4273EDBD-AC1D-40D3-9FB2-095C621B552D}" datafieldname="fieldname" /></cell></row>
      </rows>
    </section></sections>
  </column></columns>
</tab>
```

**Add a subgrid control:**
```xml
<cell>
  <control id="subgrid_related" classid="{E7A81278-8635-4D9E-8D4D-59480B391C5B}">
    <parameters>
      <TargetEntityType>relatedentity</TargetEntityType>
      <ViewId>{view-guid}</ViewId>
      <RelationshipName>entity_relatedentity</RelationshipName>
    </parameters>
  </control>
</cell>
```

## Recipes

### Create a form by copying an existing one
```
1. Find source form:
   Glob pattern="data/<env>/<folder>/SolutionExport/*/Entities/<Entity>/FormXml/main/*.xml"
2. Scaffold: dotnet run --project src/XrmEmulator.MetadataSync -- forms main new account --name "Custom Form" --copy-from {source-guid}
3. Edit _pending/Entities/Account/FormXml/main/new_custom-form.xml
4. Tell the human to review and commit
```

### Create a Quick Create form
```
1. Scaffold: dotnet run --project src/XrmEmulator.MetadataSync -- forms quickcreate new account --name "Quick Account"
2. Edit _pending/Entities/Account/FormXml/quickCreate/new_quick-account.xml
3. Tell the human to review and commit
```
