namespace XrmEmulator.MetadataSync.Models;

public record SavedQueryDefinition
{
    public required Guid SavedQueryId { get; init; }
    public required string Name { get; init; }
    public string? FetchXml { get; init; }
    public string? LayoutXml { get; init; }
    public string? ReturnedTypeCode { get; init; }
    public required string SourceFilePath { get; init; }
}
