namespace XrmEmulator.MetadataSync.Models;

public record OptionSetValueDefinition
{
    public required string OptionSetName { get; init; }
    public required List<OptionSetValueEntry> Values { get; init; }
}

public record OptionSetValueEntry
{
    /// <summary>Display label for the option (e.g., "Rådgiver")</summary>
    public required string Label { get; init; }

    /// <summary>Integer value (e.g., 100000009). If null, CRM auto-assigns.</summary>
    public int? Value { get; init; }

    /// <summary>Optional description</summary>
    public string? Description { get; init; }
}
