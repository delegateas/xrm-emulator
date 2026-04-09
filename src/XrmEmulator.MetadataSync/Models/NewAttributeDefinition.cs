namespace XrmEmulator.MetadataSync.Models;

public record NewAttributeDefinition
{
    public required string EntityLogicalName { get; init; }      // e.g. "lead"
    public required string AttributeLogicalName { get; init; }   // e.g. "cr_partner" (logical, lowercase)
    public required string AttributeSchemaName { get; init; }    // e.g. "cr_Partner" (PascalCase after prefix)
    public required string DisplayName { get; init; }            // e.g. "Partner"
    public required string AttributeType { get; init; }          // "lookup", "string", "int", "decimal", "boolean", "datetime", "memo", "picklist"
    public string? TargetEntityLogicalName { get; init; }        // For lookups: target entity (e.g. "account")
    public string? RelationshipSchemaName { get; init; }         // For lookups: e.g. "cr_lead_Partner_account"
    public int? MaxLength { get; init; }                         // For string/memo
    public string? OptionSetName { get; init; }                  // For picklist: global option set name (e.g. "kf_yesnoinherited")
    public string? RequiredLevel { get; init; }                  // "none", "recommended", "required"
    public string? Description { get; init; }
    public required string SolutionUniqueName { get; init; }     // Solution to add the component to
    public string? Action { get; init; }                         // "create" (default) or "update" — update modifies an existing attribute
}
