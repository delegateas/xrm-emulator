namespace XrmEmulator.MetadataSync.Models;

public record IconUploadDefinition
{
    public required string WebResourceName { get; init; }
    public required string DisplayName { get; init; }
    public required string SvgFile { get; init; }
    public string? EntityLogicalName { get; init; }
}

public record IconSetDefinition
{
    public required string EntityLogicalName { get; init; }
    public required string IconVectorName { get; init; }
}
