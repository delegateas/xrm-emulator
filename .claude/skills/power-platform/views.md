# Views (SavedQueries)

Create, edit, or delete Dataverse views.

## Commands

```bash
# Checkout an existing view for editing
dotnet run --project src/XrmEmulator.MetadataSync -- views <savedquery-guid>

# Scaffold a new view
dotnet run --project src/XrmEmulator.MetadataSync -- views new <entity> --name "<view name>"

# Delete a view from CRM
dotnet run --project src/XrmEmulator.MetadataSync -- views delete <savedquery-guid>
```

## Checkout Flow

- **`views <guid>`** — Copies `SolutionExport/.../SavedQueries/{guid}.xml` → `_pending/Entities/<Name>/SavedQueries/{guid}.xml`
- **`views new`** — Scaffolds a new view XML in `_pending/Entities/<Name>/SavedQueries/new_<safename>.xml` with minimal fetchxml/layoutxml. New views have **no savedqueryid** — Dataverse assigns it on commit.
- **`views delete`** — Stages a delete marker: `_pending/Deletes/savedquery_<guid>.delete.json`

## Editing Guide

After checkout, edit the XML in `_pending/`:

**Add a column:**
1. Add `<cell name="columnlogicalname" width="150" />` to `<layoutxml>/<grid>/<row>`
2. Add `<attribute name="columnlogicalname" />` to `<fetchxml>/<entity>`

**Add a filter:**
```xml
<filter type="and">
  <condition attribute="statecode" operator="eq" value="0" />
</filter>
```

**Change sort order:**
```xml
<order attribute="createdon" descending="true" />
```

**Reorder columns:** Change the order of `<cell>` elements in `<row>`.

See **xml-formats.md** for the full SavedQuery XML structure.

## Recipes

### Add a column to an existing view
```
1. Find: Grep pattern="<view name>" path="data/<env>/<folder>/SolutionExport" glob="**/SavedQueries/*.xml"
2. Note the GUID from the filename
3. Checkout: dotnet run --project src/XrmEmulator.MetadataSync -- views <guid>
4. Edit _pending/.../SavedQueries/{guid}.xml (add cell + attribute)
5. Tell the human to review and commit
```

### Create a new view from scratch
```
1. Scaffold: dotnet run --project src/XrmEmulator.MetadataSync -- views new account --name "Active Accounts"
2. Edit _pending/Entities/Account/SavedQueries/new_active-accounts.xml
3. Tell the human to review and commit
```
