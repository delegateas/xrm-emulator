namespace XrmEmulator.MetadataSync.Models;

public record NewEntityDefinition
{
    public required string EntityLogicalName { get; init; }     // e.g. "kf_partnerformresponse"
    public required string EntitySchemaName { get; init; }      // e.g. "kf_PartnerFormResponse"
    public required string DisplayName { get; init; }           // e.g. "Partner Form Svar"
    public required string DisplayNamePlural { get; init; }     // e.g. "Partner Form Svar"
    public required string PrimaryAttributeSchemaName { get; init; }  // e.g. "kf_Name"
    public required string PrimaryAttributeDisplayName { get; init; } // e.g. "Navn"
    public int PrimaryAttributeMaxLength { get; init; } = 200;
    public string? Description { get; init; }
    public required string SolutionUniqueName { get; init; }
    public List<NewAttributeDefinition>? Attributes { get; init; }  // Additional fields to create
}
