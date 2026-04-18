namespace XrmEmulator.MetadataSync.Models;

public record WorkflowRemoveFromSolutionDefinition
{
    /// <summary>Workflow name (case-sensitive). Must be unique across the org.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// Solution to remove the workflow from. If omitted, defaults to the env's
    /// SolutionUniqueName at commit time.
    /// </summary>
    public string? SolutionUniqueName { get; init; }
}
