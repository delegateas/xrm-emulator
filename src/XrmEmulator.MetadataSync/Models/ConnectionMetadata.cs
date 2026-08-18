using System.Text.Json.Serialization;

namespace XrmEmulator.MetadataSync.Models;

public record ConnectionMetadata
{
    public required EnvironmentMetadata Environment { get; init; }
    public SolutionMetadata? Solution { get; init; }

    /// <summary>
    /// How to sign in, recorded when a folder was created by a full sync so later runs reconnect the
    /// same way. Optional on purpose: a folder can name the target environment without deciding the
    /// identity, and then the operator is asked at run time. That is the case for a folder handed to
    /// someone else, where whoever prepared it cannot know which identity the runner has.
    /// </summary>
    public string? AuthMode { get; init; }

    public string? ClientId { get; init; }

    /// <summary>
    /// Azure AD tenant of the app registration, needed by ClientSecret auth. Recorded here because
    /// it is not a secret and does not change between runs — unlike the client secret, which is
    /// deliberately absent from this file and supplied per run.
    /// </summary>
    public string? TenantId { get; init; }

    public DateTimeOffset? SyncedAt { get; init; }
}

public record EnvironmentMetadata
{
    public required string Url { get; init; }
}

public record SolutionMetadata
{
    public required Guid Id { get; init; }
    public required string UniqueName { get; init; }
    public required string FriendlyName { get; init; }
}
