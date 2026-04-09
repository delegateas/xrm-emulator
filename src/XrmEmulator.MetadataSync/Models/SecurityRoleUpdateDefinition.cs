namespace XrmEmulator.MetadataSync.Models;

public record SecurityRoleUpdateDefinition
{
    public required string RoleName { get; init; }
    public required List<PrivilegeEntry> Privileges { get; init; }
}

public record PrivilegeEntry
{
    /// <summary>Entity logical name, e.g. "kf_partnerrelation"</summary>
    public required string Entity { get; init; }

    /// <summary>Access type: Read, Write, Create, Delete, Append, AppendTo, Assign, Share</summary>
    public required string Access { get; init; }

    /// <summary>Depth: Basic, Local, Deep, Global</summary>
    public string Depth { get; init; } = "Global";
}
