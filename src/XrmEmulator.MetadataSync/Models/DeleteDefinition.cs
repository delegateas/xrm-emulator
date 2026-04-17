namespace XrmEmulator.MetadataSync.Models;

public record DeleteDefinition
{
    public required string EntityType { get; init; }   // "savedquery", "systemform", "appaction", "slaitem", etc.
    public Guid ComponentId { get; init; }             // GUID if known
    public string? UniqueName { get; init; }           // For appaction lookup by uniquename at commit time
    public required string DisplayName { get; init; }  // For commit UI display
    public bool Cascade { get; init; }                 // When true, auto-delete restrict-cascade children before parent
}
