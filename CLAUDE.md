# Project Guidelines

## Planning

When asked to make a plan, use the plan tool (`EnterPlanMode`) instead of writing inline plans. This keeps plans structured and reviewable.

## MetadataSync Feature Development

When adding new commands or component types to MetadataSync, follow the patterns documented in **[docs/metadatasync-feature-guide.md](docs/metadatasync-feature-guide.md)**. Key rules:

- Everything goes through `_pending/` — no command writes directly to CRM
- Use the Reader/Writer/Model pattern for each component type
- Support crash-resilience: per-item archiving + `_outputs.json`
- Support variable references (`{{_pending/...#id}}`) for chained commits

## Dataverse SDK

When calling Dataverse organization service actions, prefer typed SDK message classes (e.g., `AddAppComponentsRequest`, `RemoveAppComponentsRequest`) from `Microsoft.Crm.Sdk.Messages` over untyped `OrganizationRequest` with string-keyed parameters. Typed messages enforce correct parameter types at compile time (e.g., `EntityReferenceCollection` vs `EntityCollection`) and prevent runtime type mismatch errors.
