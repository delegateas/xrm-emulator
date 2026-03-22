namespace XrmEmulator.MetadataSync.Models;

public record AppModuleEntityDefinition
{
    public required string AppModuleUniqueName { get; init; }
    public required string EntityLogicalName { get; init; }
    public bool IncludeAllViews { get; init; }
}
