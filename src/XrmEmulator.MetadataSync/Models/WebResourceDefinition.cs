namespace XrmEmulator.MetadataSync.Models;

public record WebResourceUploadDefinition
{
    public required string WebResourceName { get; init; }
    public required string DisplayName { get; init; }
    public required string ResourceFile { get; init; }
    public required int WebResourceType { get; init; }
}
