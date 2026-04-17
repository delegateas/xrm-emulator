using System.Text.Json;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class CustomApiFileReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static CustomApiDefinition Parse(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<CustomApiDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse Custom API file: {filePath}");
    }
}
