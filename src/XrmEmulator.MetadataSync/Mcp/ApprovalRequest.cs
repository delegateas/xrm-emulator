using System.Text.Json;
using System.Text.Json.Serialization;
using XrmEmulator.MetadataSync.Commit;

namespace XrmEmulator.MetadataSync.Mcp;

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Executing,
    Completed,
    Failed
}

public class ApprovalRequest
{
    [JsonPropertyName("approvalId")]
    public required string ApprovalId { get; init; }

    [JsonPropertyName("items")]
    public required List<ApprovalItemInfo> Items { get; init; }

    [JsonPropertyName("status")]
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("respondedAt")]
    public DateTimeOffset? RespondedAt { get; set; }

    [JsonPropertyName("commitResult")]
    public CommitResultInfo? CommitResultInfo { get; set; }

    public void Save(string approvalsDir)
    {
        Directory.CreateDirectory(approvalsDir);
        var path = Path.Combine(approvalsDir, $"{ApprovalId}.json");
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }

    public static ApprovalRequest? Load(string approvalsDir, string approvalId)
    {
        var path = Path.Combine(approvalsDir, $"{approvalId}.json");
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ApprovalRequest>(json);
    }

    public static List<ApprovalRequest> LoadAll(string approvalsDir)
    {
        if (!Directory.Exists(approvalsDir)) return [];
        var results = new List<ApprovalRequest>();
        foreach (var file in Directory.GetFiles(approvalsDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var req = JsonSerializer.Deserialize<ApprovalRequest>(json);
                if (req != null) results.Add(req);
            }
            catch { /* skip corrupt files */ }
        }
        return results;
    }
}

public record ApprovalItemInfo(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("filePath")] string FilePath);

public record CommitResultInfo(
    [property: JsonPropertyName("committedCount")] int CommittedCount,
    [property: JsonPropertyName("failedItem")] string? FailedItem,
    [property: JsonPropertyName("error")] string? Error);
