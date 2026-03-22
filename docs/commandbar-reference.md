# Command Bar Reference (appaction)

Modern command bar buttons in Dataverse are stored as `appaction` records. This document covers the entity schema, field values, scoping rules, and how they relate to the classic ribbon system.

## appaction XML Structure

```xml
<appaction uniquename="prefix__MyButton!appmodule!entity!location">
  <appmoduleid>
    <uniquename>myApp</uniquename>            <!-- App scope (omit for table/global) -->
  </appmoduleid>
  <name>MyButton</name>                        <!-- Display/matching name -->
  <buttonlabeltext default="Click Me">         <!-- Visible label -->
    <label description="Click Me" languagecode="1033" />
  </buttonlabeltext>
  <context>1</context>                          <!-- 0=All, 1=Entity -->
  <contextentity>
    <logicalname>account</logicalname>          <!-- Target entity -->
  </contextentity>
  <contextvalue>account</contextvalue>          <!-- Entity logical name (string) -->
  <location>2</location>                        <!-- Where the button appears -->
  <type>0</type>                                <!-- Button type -->
  <hidden>0</hidden>                            <!-- 0=visible, 1=hidden -->
  <sequence>10.0000000000</sequence>            <!-- Sort order (lower = further left) -->
  <fonticon>$clientsvg:Add</fonticon>           <!-- Fluent UI icon -->
  <onclickeventtype>2</onclickeventtype>        <!-- Action type -->
  <onclickeventjavascriptwebresourceid>
    <webresourceid>{guid}</webresourceid>        <!-- JS library GUID -->
  </onclickeventjavascriptwebresourceid>
  <onclickeventjavascriptfunctionname>Lib.onClick</onclickeventjavascriptfunctionname>
  <onclickeventjavascriptparameters>[{"type":5}]</onclickeventjavascriptparameters>
  <origin>0</origin>                            <!-- How the record was created -->
  <visibilitytype>0</visibilitytype>            <!-- Visibility rule type -->
  <statecode>0</statecode>                      <!-- 0=Active, 1=Inactive -->
  <statuscode>1</statuscode>                    <!-- 1=Active, 2=Inactive -->
</appaction>
```

## Field Reference

### location — Where the Button Appears

| Value | Label | Description |
|-------|-------|-------------|
| 0 | **Form** | Main form command bar (top of the record form) |
| 1 | **Main Grid** | Homepage grid (full-page list from left nav) |
| 2 | **Sub Grid** | Subgrid embedded in a form (e.g. related contacts on account form) |
| 3 | **Associated Grid** | Associated view (Form > Related tab > select related table) |
| 4 | **Quick Form** | Quick create form |
| 5 | **Global Header** | Global app header bar |
| 6 | **Dashboard** | Dashboard command bar |

### type — Button Type

| Value | Label | Description |
|-------|-------|-------------|
| 0 | **Standard Button** | A regular clickable button |
| 1 | **Dropdown Button** | A button that shows a dropdown menu of child actions |
| 2 | **Split Button** | A button with a default action + dropdown arrow |
| 3 | **Group** | A visual grouping container for other buttons |

### context — Scope Context

| Value | Label | Description |
|-------|-------|-------------|
| 0 | **All** | Global scope — applies to all entities (no `contextentity`/`contextvalue` needed) |
| 1 | **Entity** | Entity scope — applies to a specific entity (requires `contextentity` + `contextvalue`) |

### onclickeventtype — Action Type

| Value | Label | Description |
|-------|-------|-------------|
| 0 | **None** | No custom action. Used for system buttons that have built-in behavior. |
| 1 | **Formula** | Power Fx formula (requires component library fields) |
| 2 | **JavaScript** | JavaScript function (requires `webresourceid` + `functionname`) |

### origin — How the Record Was Created

| Value | Label | Description |
|-------|-------|-------------|
| 0 | **Default** | Created by a user/customizer (custom button or API-created) |
| 1 | **Migrated** | Auto-migrated from classic ribbon by the system when first customized in Command Designer |
| 2 | **Enhanced Migrated** | Enhanced migration variant |

### visibilitytype — Visibility Rule Type

| Value | Label | Description |
|-------|-------|-------------|
| 0 | **None** | Always visible (no visibility rule) |
| 1 | **Formula** | Visibility controlled by Power Fx formula |
| 2 | **Classic Rules** | Visibility controlled by classic ribbon enable/display rules |

### hidden — Visibility

| Value | Meaning |
|-------|---------|
| 0 | Visible |
| 1 | Hidden |

### isdisabled — Disabled Flag

| Value | Meaning |
|-------|---------|
| 0 (false) | Modern command is active — the modern button renders |
| 1 (true) | Modern command is disabled — falls back to the classic ribbon equivalent |

### JavaScript Parameters (onclickeventjavascriptparameters)

A JSON array of parameter type objects passed to the JS function on click:

```json
[{"type":5}, {"type":12}]
```

| Type Value | Name | Description |
|-----------|------|-------------|
| 5 | **PrimaryControl** | The form context (for form-location buttons) |
| 8 | **SelectedEntityTypeName** | The entity logical name of selected records |
| 12 | **SelectedControl** | The grid/subgrid control context |

### fonticon — Icon

Must be prefixed with `$clientsvg:` to render correctly. Common values:

| Value | Icon |
|-------|------|
| `$clientsvg:Add` | Plus/add icon |
| `$clientsvg:Edit` | Pencil/edit icon |
| `$clientsvg:Delete` | Trash/delete icon |
| `$clientsvg:Save` | Save icon |
| `$clientsvg:Refresh` | Refresh icon |
| `$clientsvg:Org` | Organization/hierarchy icon |

## Scoping Rules

Commands have three possible scopes. **The narrowest scope wins** when multiple commands share the same `<name>`.

### App Scope (narrowest)
- Has `<appmoduleid>` — bound to a specific app
- Has `<contextentity>` + `<contextvalue>` — bound to a specific entity
- Only visible in that one app for that entity
- **This is the default** when creating via Command Designer

### Table Scope
- **No** `<appmoduleid>` — not bound to any app
- Has `<contextentity>` + `<contextvalue>` — bound to a specific entity
- Visible in **all apps** that use the entity
- Created by removing `<appmoduleid>` from exported XML

### Global Scope (broadest)
- **No** `<appmoduleid>`
- **No** `<contextentity>` or `<contextvalue>`
- `<context>0</context>` (All)
- Visible in **all apps** for **all entities** at the specified location

### Override Hierarchy

```
Global → overridden by → Table → overridden by → App
```

To override a broader command at a narrower scope, ensure both commands have the **same `<name>` value**. The system matches by name and renders the most specific scope.

## OOTB Button Names

When the Command Designer "migrates" an OOTB button, it creates `appaction` records with predictable `<name>` values:

| Name Pattern | Location | Description |
|-------------|----------|-------------|
| `Mscrm.SubGrid.{entity}.NewRecord` | 2 (SubGrid) | "New" button on subgrids |
| `Mscrm.SubGrid.{entity}.NewRecord` | 3 (Associated) | "New" button on associated views |
| `Mscrm.HomepageGrid.{entity}.NewRecord` | 1 (MainGrid) | "New" button on homepage grids |
| `Mscrm.Form.{entity}.MainTab.Save` | 0 (Form) | "Save" button on forms |
| `Mscrm.Form.{entity}.MainTab.Delete` | 0 (Form) | "Delete" button on forms |
| `Mscrm.Form.{entity}.Deactivate` | 0 (Form) | "Deactivate" button on forms |

Note: The same `<name>` can appear with different `<location>` values. For example, `Mscrm.SubGrid.account.NewRecord` with `location=2` is the subgrid "New" button, while `location=3` is the associated view "New" button.

## Sequence and Ordering

The `<sequence>` field controls button order within a command bar location. **Lower values appear further left** (or first in the bar).

- OOTB migrated buttons typically have very high sequence values (e.g. `100100010`)
- Custom buttons with lower sequences (e.g. `10`, `100`) will appear before OOTB buttons
- To place a custom button after OOTB buttons, use a higher sequence value

## Uniquename Conventions

The `uniquename` attribute must be globally unique across the environment. Common patterns:

| Pattern | Used By |
|---------|---------|
| `msdyn_Mscrm.SubGrid.{entity}.NewRecord!{entity}!{location}` | System-migrated OOTB buttons |
| `{prefix}__{name}!{appguid}!{app}!{entity}!{location}` | Command Designer-created buttons |
| `{prefix}__{entity}_{functionname}` | MetadataSync custom buttons |

## MetadataSync Extensions

MetadataSync adds one non-standard XML element that is **not sent to CRM** but is processed by the commit pipeline:

```xml
<hideLegacyButtons>
  <button>Mscrm.SubGrid.account.NewRecord</button>
</hideLegacyButtons>
```

This triggers a RibbonDiffXml import to hide the classic ribbon equivalent of the button. Used when creating a new custom button that replaces an OOTB one.

## Related Entities

| Entity | Relationship | Description |
|--------|-------------|-------------|
| `appmodule` | `appmoduleid` lookup | Which model-driven app this button belongs to |
| `entity` | `contextentity` lookup | Which Dataverse entity (resolved via metadata ID, not logical name) |
| `webresource` | `onclickeventjavascriptwebresourceid` lookup | JavaScript library for the click action |
| `webresource` | `iconwebresourceid` lookup | Custom SVG icon (alternative to `fonticon`) |
| `appaction` | `parentappactionid` self-lookup | Parent button (for dropdown/split button children) |
| `canvasapp` | `onclickeventformulacomponentlibraryid` | Power Fx component library for formula actions |

## Classic Ribbon vs Modern Command Bar

| Aspect | Classic Ribbon (RibbonDiffXml) | Modern Command Bar (appaction) |
|--------|-------------------------------|-------------------------------|
| Storage | XML in solution `RibbonDiff.xml` | `appaction` table records |
| Customization | XML manipulation + solution import | Create/Update via API or Command Designer |
| Actions | JavaScript only | JavaScript or Power Fx |
| Visibility | Enable/Display rules (XML) | Power Fx formulas or classic rules |
| Scoping | Entity-level only | Global / Table / App scoping |
| Precedence | Modern commands take priority when both exist | — |

When both a classic ribbon customization and a modern appaction exist for the same command, the **modern appaction takes precedence**. Setting `isdisabled=true` on the appaction will fall back to the classic ribbon behavior.
