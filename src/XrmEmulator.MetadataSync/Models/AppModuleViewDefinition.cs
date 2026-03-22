namespace XrmEmulator.MetadataSync.Models;

public record AppModuleViewDefinition
{
    public required string AppModuleUniqueName { get; init; }
    public required string EntityLogicalName { get; init; }
    public required List<Guid> ViewIds { get; init; }
}
