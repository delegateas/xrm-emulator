namespace XrmEmulator.MetadataSync.Models;

public record SiteMapDefinition
{
    public required string UniqueName { get; init; }
    public required string Name { get; init; }
    public required string SiteMapXml { get; init; }
    public required string SourceFilePath { get; init; }
}
