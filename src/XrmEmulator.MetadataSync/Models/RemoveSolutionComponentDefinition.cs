namespace XrmEmulator.MetadataSync.Models;

public record RemoveSolutionComponentDefinition
{
    /// <summary>Dataverse component type code (e.g. 60 = SystemForm, 1039 = SavedQuery, 29 = Workflow).</summary>
    public required int ComponentType { get; init; }

    /// <summary>ID of the component to remove from the solution.</summary>
    public required Guid ComponentId { get; init; }

    /// <summary>Human-readable label shown in the pending list.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Solution to remove the component from. If omitted, defaults to the env's
    /// SolutionUniqueName at commit time.
    /// </summary>
    public string? SolutionUniqueName { get; init; }
}
