namespace XrmEmulator.MetadataSync.Models;

public record EntityDefinition
{
    public required string LogicalName { get; init; }
    public required string DisplayName { get; init; }
    public required List<AttributeDefinition> Attributes { get; init; }
    public required string SourceFilePath { get; init; }
}

public record AttributeDefinition
{
    public required string LogicalName { get; init; }
    public required string DisplayName { get; init; }
    public required string Type { get; init; }
    public string? Description { get; init; }
    public string? RequiredLevel { get; init; }
    public int? MaxLength { get; init; }
    public int? MinValue { get; init; }
    public int? MaxValue { get; init; }
    public int? Accuracy { get; init; }
    public decimal? MinValueDecimal { get; init; }
    public decimal? MaxValueDecimal { get; init; }
    public string? Format { get; init; }
    public bool IsCustomField { get; init; }
    public List<OptionDefinition>? Options { get; init; }
}

public record OptionDefinition
{
    public required int Value { get; init; }
    public required string Label { get; init; }
}
