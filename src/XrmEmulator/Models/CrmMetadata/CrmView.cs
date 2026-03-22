namespace XrmEmulator.Models.CrmMetadata;

public record CrmView
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string EntityName { get; init; }
    public string FetchXml { get; init; } = "";
    public List<CrmViewColumn> Columns { get; init; } = [];
}

public record CrmViewColumn
{
    public required string Name { get; init; }
    public int Width { get; init; } = 100;
}
