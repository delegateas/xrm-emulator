namespace XrmEmulator.MetadataSync.Models;

public record DirectSolutionComponentDefinition
{
    /// <summary>Dataverse ComponentType integer (e.g. 2 = attribute, 1 = entity, 26 = view)</summary>
    public required int ComponentType { get; init; }

    /// <summary>The objectid of the component in Dataverse (resolved from live CRM, never guessed)</summary>
    public required Guid ComponentId { get; init; }

    /// <summary>Target solution unique name</summary>
    public required string SolutionUniqueName { get; init; }

    /// <summary>Human-readable label for pending list display</summary>
    public string? DisplayName { get; init; }
}
