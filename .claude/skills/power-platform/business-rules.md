# Business Rules

Create or modify Dataverse business rules — field default values, conditional field logic, field visibility, or field requirement levels.

## Commands

```bash
# Scaffold a new business rule
dotnet run --project src/XrmEmulator.MetadataSync -- businessrules new <entity> --name "<rule name>"

# Checkout an existing business rule for editing
dotnet run --project src/XrmEmulator.MetadataSync -- businessrules <workflow-guid>

# Human reviews and commits
dotnet run --project src/XrmEmulator.MetadataSync -- commit
```

## How It Works

Business rules are `workflow` entities with `category=2`. They're exported as two files:

- **`.xaml.data.xml`** — metadata (name, entity, category, scope, triggers, state)
- **`.xaml`** — XAML workflow definition (the actual logic)

The `new` command scaffolds both files to `_pending/Workflows/` with a template. You then edit the `.xaml` file for the specific use case.

## XAML Structure

The XAML uses these key activities:

| Activity | Purpose |
|----------|---------|
| `GetEntityProperty` | Read a field value from the record |
| `EvaluateCondition` | Evaluate a condition (NotNull, Equal, etc.) |
| `ConditionBranch` | If/else branching based on condition result |
| `EvaluateExpression` | Create a typed CRM value (Boolean, OptionSetValue, etc.) |
| `SetEntityProperty` | Set a field value on the record |
| `SetAttributeValue` | Commit the field change (must follow SetEntityProperty) |

### Key patterns in the XAML

- `InputEntities("primaryEntity")` — the triggering record
- `CreatedEntities("primaryEntity#Temp")` — temp entity for updates
- `ConditionOperator` values: `NotNull`, `Null`, `Equal`, `NotEqual`, `GreaterThan`, etc.
- `WorkflowPropertyType` values: `Boolean`, `OptionSetValue`, `String`, `Decimal`, etc.

## Example Recipes

### Set a boolean field to true (unconditional)

Edit the XAML template:
- `GetEntityProperty`: set `Attribute="name"` (any field to check)
- `EvaluateCondition`: set `ConditionOperator` to `NotNull`
- `SetEntityProperty`: set `Attribute="your_boolean_field"`
- `EvaluateExpression Parameters`: `WorkflowPropertyType.Boolean, "1"`

### Set field based on a condition

- `GetEntityProperty`: set `Attribute="field_to_check"`
- `EvaluateCondition`: set operator and parameters
- In the `Then` branch: set the target field value
- Optionally add an `Else` branch (replace `<x:Null x:Key="Else" />`)

## Real Working Example

Look for existing business rule XAML files in `data/<env>/<solution-folder>/SolutionExport/<SolutionName>/Workflows/` — files named `BR-*.xaml` are business rules. These show the full XAML structure with real field names and conditions.

For example, a rule that checks if `name` is not null and sets a boolean field to `true` demonstrates the most common pattern: GetEntityProperty → EvaluateCondition → EvaluateExpression → SetEntityProperty → SetAttributeValue.

## Important Notes

- The `new` command creates a template; **you must edit the XAML** for the specific use case
- Replace `ATTRIBUTE_TO_CHECK` and `ATTRIBUTE_TO_SET` placeholders in the template
- The `EntityName` attribute in XAML activities must match the entity logical name
- After commit, business rules need to be **activated** in CRM to take effect
