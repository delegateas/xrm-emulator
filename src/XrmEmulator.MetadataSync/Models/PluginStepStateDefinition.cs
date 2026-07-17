namespace XrmEmulator.MetadataSync.Models;

public record PluginStepStateDefinition
{
    public required Guid StepId { get; init; }

    /// <summary>Resolved at stage-time for display in commit output — not used to identify the step.</summary>
    public string? StepName { get; init; }

    public required bool Enable { get; init; }
}
