using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XrmEmulator.MetadataSync.Connection;

public static class TokenCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetCacheFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".xrm-emulator", "token-cache.json");
    }

    public static TokenCacheEntry? Load(string dataverseUrl, string clientId)
    {
        var path = GetCacheFilePath();
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<TokenCacheFile>(json, JsonOptions);
            if (file?.Entries == null)
                return null;

            var key = ComputeCacheKey(dataverseUrl, clientId);
            return file.Entries.GetValueOrDefault(key);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string dataverseUrl, string clientId, string tenantId, string refreshToken)
    {
        var path = GetCacheFilePath();
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        TokenCacheFile file;
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                file = JsonSerializer.Deserialize<TokenCacheFile>(existing, JsonOptions) ?? new TokenCacheFile();
            }
            else
            {
                file = new TokenCacheFile();
            }
        }
        catch
        {
            file = new TokenCacheFile();
        }

        file.Entries ??= new Dictionary<string, TokenCacheEntry>();

        var key = ComputeCacheKey(dataverseUrl, clientId);
        file.Entries[key] = new TokenCacheEntry
        {
            DataverseUrl = dataverseUrl,
            ClientId = clientId,
            TenantId = tenantId,
            RefreshToken = refreshToken,
            CachedAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(file, JsonOptions);
        File.WriteAllText(path, json);

        SetOwnerOnlyPermissions(path);
    }

    public static void Clear(string dataverseUrl, string clientId)
    {
        var path = GetCacheFilePath();
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<TokenCacheFile>(json, JsonOptions);
            if (file?.Entries == null)
                return;

            var key = ComputeCacheKey(dataverseUrl, clientId);
            if (file.Entries.Remove(key))
            {
                var updated = JsonSerializer.Serialize(file, JsonOptions);
                File.WriteAllText(path, updated);
                SetOwnerOnlyPermissions(path);
            }
        }
        catch
        {
            // Best effort — if the file is corrupt, ignore
        }
    }

    private static string ComputeCacheKey(string dataverseUrl, string clientId)
    {
        var input = $"{dataverseUrl.ToLowerInvariant().TrimEnd('/')}|{clientId.ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    private static void SetOwnerOnlyPermissions(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

public class TokenCacheFile
{
    [JsonPropertyName("entries")]
    public Dictionary<string, TokenCacheEntry>? Entries { get; set; }
}

public class TokenCacheEntry
{
    [JsonPropertyName("dataverseUrl")]
    public required string DataverseUrl { get; set; }

    [JsonPropertyName("clientId")]
    public required string ClientId { get; set; }

    [JsonPropertyName("tenantId")]
    public required string TenantId { get; set; }

    [JsonPropertyName("refreshToken")]
    public required string RefreshToken { get; set; }

    [JsonPropertyName("cachedAt")]
    public DateTimeOffset CachedAt { get; set; }
}
