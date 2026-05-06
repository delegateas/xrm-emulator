using System.Text.Json;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class PluginManagedIdentityFileReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static PluginManagedIdentityDefinition Parse(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<PluginManagedIdentityDefinition>(json, Options)
            ?? throw new InvalidOperationException($"Failed to parse {filePath}");
    }
}
