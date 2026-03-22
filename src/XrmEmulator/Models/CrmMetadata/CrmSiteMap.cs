namespace XrmEmulator.Models.CrmMetadata;

public record CrmSiteMap
{
    public required string UniqueName { get; init; }
    public List<CrmArea> Areas { get; init; } = [];
}

public record CrmArea
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public List<CrmGroup> Groups { get; init; } = [];
}

public record CrmGroup
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public List<CrmSubArea> SubAreas { get; init; } = [];
}

public record CrmSubArea
{
    public required string Id { get; init; }
    public string? Entity { get; init; }
    public string? Title { get; init; }
}
