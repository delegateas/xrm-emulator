namespace XrmEmulator.MetadataSync.Models;

public record RelationshipUpdateDefinition
{
    public required string SchemaName { get; init; }
    public string? DeleteBehavior { get; init; }      // Cascade, RemoveLink, Restrict, NoCascade
    public string? AssignBehavior { get; init; }
    public string? ShareBehavior { get; init; }
    public string? UnshareBehavior { get; init; }
    public string? ReparentBehavior { get; init; }
    public string? MergeBehavior { get; init; }
}
