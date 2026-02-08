namespace XrmEmulator.MetadataSync.Connection;

public enum AuthMode
{
    ConnectionString,
    ClientSecret,
    InteractiveBrowser
}

public record ConnectionSettings
{
    /// <summary>
    /// Microsoft's well-known public client ID for Dataverse sample applications.
    /// No client secret required — suitable for interactive/delegated auth.
    /// </summary>
    public const string MicrosoftPublicClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";


    public required string Url { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? ConnectionString { get; init; }
    public required AuthMode AuthMode { get; init; }
}
