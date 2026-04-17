namespace XrmEmulator.MetadataSync.Models;

public record EnableChangeTrackingDefinition
{
    public required string EntityLogicalName { get; init; }
    public required string SolutionUniqueName { get; init; }
}
