namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Stages a full-entity metadata delete (DeleteEntityRequest). This drops the table,
/// its attributes, and its relationships. The entity must have no remaining references
/// from other tables or solution components outside its own row.
/// </summary>
public record EntityMetadataDeleteDefinition
{
    public required string EntityLogicalName { get; init; }
    public string? SolutionUniqueName { get; init; }
}
