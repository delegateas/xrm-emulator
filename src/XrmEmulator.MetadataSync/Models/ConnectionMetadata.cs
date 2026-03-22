using System.Text.Json.Serialization;

namespace XrmEmulator.MetadataSync.Models;

public record ConnectionMetadata
{
    public required EnvironmentMetadata Environment { get; init; }
    public required SolutionMetadata Solution { get; init; }
    public required string AuthMode { get; init; }
    public string? ClientId { get; init; }
    public DateTimeOffset SyncedAt { get; init; }
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
