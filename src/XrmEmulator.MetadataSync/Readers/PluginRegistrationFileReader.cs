using System.Text.Json;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class PluginRegistrationFileReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static PluginRegistrationDefinition Parse(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<PluginRegistrationDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse plugin registration file: {filePath}");
    }
}
