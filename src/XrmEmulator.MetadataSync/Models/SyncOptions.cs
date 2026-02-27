namespace XrmEmulator.MetadataSync.Models;

public record SyncOptions
{
    public required Guid SolutionId { get; init; }
    public required string SolutionUniqueName { get; init; }
    public required HashSet<string> SelectedEntities { get; init; }
    public required string OutputDirectory { get; init; }
    public bool IncludePlugins { get; init; }
    public bool IncludeWorkflows { get; init; }
    public bool IncludeSecurityRoles { get; init; }
    public bool IncludeOptionSets { get; init; }
    public bool IncludeOrganizationData { get; init; }
}
