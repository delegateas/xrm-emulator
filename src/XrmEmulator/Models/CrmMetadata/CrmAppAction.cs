namespace XrmEmulator.Models.CrmMetadata;

public record CrmAppAction
{
    public required string UniqueName { get; init; }
    public required string Label { get; init; }
    public string? FontIcon { get; init; }
    public required int Location { get; init; }
    public required string EntityLogicalName { get; init; }
    public string? AppModuleUniqueName { get; init; }
    public decimal Sequence { get; init; }
    public bool Hidden { get; init; }
    public int OnClickEventType { get; init; }
    public string? JsFunctionName { get; init; }

    // Location constants
    public const int LocationForm = 0;
    public const int LocationMainGrid = 1;
    public const int LocationSubGrid = 2;
    public const int LocationAssociatedGrid = 3;
}
