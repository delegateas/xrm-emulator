using System.Text.Json.Serialization;

namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// A single ribbon workbench action staged for commit.
/// Multiple actions for the same entity are combined into one solution import.
/// </summary>
public record RibbonWorkbenchAction
{
    [JsonPropertyName("action")]
    public required string Action { get; init; } // "hide"

    [JsonPropertyName("entity")]
    public required string EntityLogicalName { get; init; }

    [JsonPropertyName("buttonId")]
    public required string ButtonId { get; init; }
}
