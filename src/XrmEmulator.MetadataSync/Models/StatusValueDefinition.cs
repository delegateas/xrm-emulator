namespace XrmEmulator.MetadataSync.Models;

public record StatusValueDefinition
{
    public required string EntityLogicalName { get; init; }
    public required string SolutionUniqueName { get; init; }

    /// <summary>
    /// Rename existing status reason labels. Key = current integer value, Value = new label.
    /// </summary>
    public Dictionary<int, string>? RenameStatusCodes { get; init; }

    /// <summary>
    /// New status reason values to add. Each entry specifies the state it belongs to.
    /// </summary>
    public List<NewStatusValue>? AddStatusCodes { get; init; }
}

public record NewStatusValue
{
    public required string Label { get; init; }
    public required int StateCode { get; init; }
    public int? Value { get; init; }  // null = let CRM assign
    public string? Description { get; init; }
}
