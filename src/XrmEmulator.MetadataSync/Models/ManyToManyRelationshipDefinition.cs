namespace XrmEmulator.MetadataSync.Models;

public record ManyToManyRelationshipDefinition
{
    public required string SchemaName { get; init; }
    public required string Entity1LogicalName { get; init; }
    public required string Entity2LogicalName { get; init; }

    /// <summary>
    /// Optional — the intersect entity logical name. Defaults to <see cref="SchemaName"/> if null.
    /// </summary>
    public string? IntersectEntityName { get; init; }

    /// <summary>
    /// Optional — overrides for the "Related" menu labels shown on each side.
    /// If null, Dataverse defaults to the other entity's display collection name.
    /// </summary>
    public string? Entity1MenuLabel { get; init; }
    public string? Entity2MenuLabel { get; init; }

    public string? SolutionUniqueName { get; init; }
}
