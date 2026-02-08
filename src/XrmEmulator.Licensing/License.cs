using System.Text.Json.Serialization;

namespace XrmEmulator.Licensing;

public sealed record License
{
    [JsonPropertyName("lid")]
    public required Guid LicenseId { get; init; }

    [JsonPropertyName("sub")]
    public required string Subject { get; init; }

    [JsonPropertyName("iat")]
    public required DateTimeOffset IssuedAt { get; init; }

    [JsonPropertyName("exp")]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonPropertyName("features")]
    public required IReadOnlyList<string> Features { get; init; }

    [JsonPropertyName("seats")]
    public int Seats { get; init; }

    [JsonPropertyName("meta")]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow;

    public bool HasFeature(string feature) => Features.Contains(feature, StringComparer.OrdinalIgnoreCase);
}
