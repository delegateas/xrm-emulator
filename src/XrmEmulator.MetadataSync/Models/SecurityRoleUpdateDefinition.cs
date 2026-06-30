namespace XrmEmulator.MetadataSync.Models;

public record SecurityRoleUpdateDefinition
{
    public required string RoleName { get; init; }
    public required List<PrivilegeEntry> Privileges { get; init; }
}

public record PrivilegeEntry
{
    /// <summary>
    /// Entity logical name, e.g. "kf_partnerrelation". Empty for task-based
    /// (miscellaneous) privileges that are not tied to an entity — set <see cref="Privilege"/> instead.
    /// </summary>
    public string Entity { get; init; } = "";

    /// <summary>
    /// Access type: Read, Write, Create, Delete, Append, AppendTo, Assign, Share.
    /// Empty for task-based privileges — set <see cref="Privilege"/> instead.
    /// </summary>
    public string Access { get; init; } = "";

    /// <summary>
    /// Raw privilege name for task-based / miscellaneous privileges that have no entity
    /// + access mapping, e.g. "prvSearchAvailability". When set, the writer resolves this
    /// name directly and ignores <see cref="Entity"/> / <see cref="Access"/>.
    /// </summary>
    public string? Privilege { get; init; }

    /// <summary>Depth: Basic, Local, Deep, Global</summary>
    public string Depth { get; init; } = "Global";
}
