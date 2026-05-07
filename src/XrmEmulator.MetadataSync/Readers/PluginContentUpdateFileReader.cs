using System.Text.Json;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class PluginContentUpdateFileReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static PluginContentUpdateDefinition Parse(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<PluginContentUpdateDefinition>(json, Options)
            ?? throw new InvalidOperationException($"Failed to parse {filePath}");
    }
}
