namespace XrmEmulator.MetadataSync.Models;

public record WorkflowActivationDefinition
{
    /// <summary>Workflow name (case-sensitive). Must be unique across the org.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// Optional — solution to add the BPF backing entity to (category 4 only).
    /// If omitted, defaults to the env's SolutionUniqueName at commit time.
    /// </summary>
    public string? SolutionUniqueName { get; init; }
}
