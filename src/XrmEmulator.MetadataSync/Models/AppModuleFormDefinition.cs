namespace XrmEmulator.MetadataSync.Models;

public record AppModuleFormDefinition
{
    public required string AppModuleUniqueName { get; init; }
    public required string EntityLogicalName { get; init; }
    public required List<Guid> FormIds { get; init; }
}
