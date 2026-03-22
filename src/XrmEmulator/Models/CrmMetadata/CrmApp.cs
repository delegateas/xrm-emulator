namespace XrmEmulator.Models.CrmMetadata;

public record CrmApp
{
    public required string UniqueName { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = "";
    public List<string> EntityNames { get; init; } = [];
    public List<Guid> ViewIds { get; init; } = [];
    public List<Guid> FormIds { get; init; } = [];
    public string? SiteMapUniqueName { get; init; }
    public string? MetadataRootPath { get; init; }
}
