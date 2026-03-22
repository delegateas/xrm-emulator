namespace XrmEmulator.Models.CrmMetadata;

public record CrmEntity
{
    public required string LogicalName { get; init; }
    public required string DisplayName { get; init; }
    public string? PluralName { get; init; }
    public string? PrimaryIdAttribute { get; init; }
    public string? PrimaryNameAttribute { get; init; }
    public Dictionary<string, CrmAttribute> Attributes { get; init; } = new();
}

public record CrmAttribute
{
    public required string LogicalName { get; init; }
    public required string DisplayName { get; init; }
    public required string Type { get; init; }
}
