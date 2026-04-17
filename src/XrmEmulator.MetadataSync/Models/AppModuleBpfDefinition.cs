namespace XrmEmulator.MetadataSync.Models;

public record AppModuleBpfDefinition
{
    public required string AppModuleUniqueName { get; init; }
    public required string BpfName { get; init; }
    public string? PrimaryEntity { get; init; }
    public string Action { get; init; } = "add"; // "add" or "remove"
}
