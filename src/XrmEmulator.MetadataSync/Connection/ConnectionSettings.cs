namespace XrmEmulator.MetadataSync.Connection;

public enum AuthMode
{
    ConnectionString,
    ClientSecret,
    InteractiveBrowser
}

public record ConnectionSettings
{
    public required string Url { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? ConnectionString { get; init; }
    public required AuthMode AuthMode { get; init; }
}
