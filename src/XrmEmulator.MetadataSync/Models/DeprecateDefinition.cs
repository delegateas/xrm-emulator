namespace XrmEmulator.MetadataSync.Models;

public record DeprecateDefinition
{
    public required string EntityLogicalName { get; init; }    // e.g. "lead"
    public required string AttributeLogicalName { get; init; } // e.g. "cr_department"
    public required string OriginalDisplayName { get; init; }  // e.g. "Department"
    public required string NewDisplayName { get; init; }       // e.g. "(Deprecated) Department"
}
