namespace XrmEmulator.MetadataSync.Models;

public record CommandBarDefinition
{
    public required string UniqueName { get; init; }
    public string? Name { get; init; }
    public string? AppModuleUniqueName { get; init; }
    public string? EntityLogicalName { get; init; }
    public int? Location { get; init; }
    public string? Label { get; init; }
    public string? WebResourceName { get; init; }
    public string? FunctionName { get; init; }
    public string? Parameters { get; init; }
    public decimal? Sequence { get; init; }
    public bool? Hidden { get; init; }
    public string? FontIcon { get; init; }
    public List<string>? HideLegacyButtons { get; init; }
}
