namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Stages a relationship metadata delete (DeleteRelationshipRequest). Works for both
/// 1:N and N:N relationships. For N:N, the intersect entity is dropped automatically.
/// </summary>
public record RelationshipDeleteDefinition
{
    public required string SchemaName { get; init; }
    public string? SolutionUniqueName { get; init; }
}
