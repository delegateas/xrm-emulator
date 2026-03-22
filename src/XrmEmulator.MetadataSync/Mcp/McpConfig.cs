using System.Text.Json.Serialization;

namespace XrmEmulator.MetadataSync.Mcp;

public record McpConfig
{
    [JsonPropertyName("graphClientId")]
    public required string GraphClientId { get; init; }

    [JsonPropertyName("graphTenantId")]
    public required string GraphTenantId { get; init; }

    [JsonPropertyName("approverEmail")]
    public required string ApproverEmail { get; init; }

    [JsonPropertyName("hmacSigningKey")]
    public required string HmacSigningKey { get; init; }

    [JsonPropertyName("refreshToken")]
    public required string RefreshToken { get; init; }

    [JsonPropertyName("devtunnelId")]
    public string? DevtunnelId { get; init; }

    public static string GetConfigPath(string baseDir) =>
        Path.Combine(baseDir, ".metadatasync", "mcp-config.json");

    public static string GetApprovalsDir(string baseDir) =>
        Path.Combine(baseDir, ".metadatasync", "approvals");
}
