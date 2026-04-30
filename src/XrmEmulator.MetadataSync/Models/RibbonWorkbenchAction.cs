using System.Text.Json.Serialization;

namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// A single ribbon workbench action staged for commit.
/// Multiple actions for the same entity are combined into one solution import.
/// action="hide"     — adds a HideCustomAction for buttonId
/// action="override" — imports the companion .xml file as the full RibbonDiffXml
/// </summary>
public record RibbonWorkbenchAction
{
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("entity")]
    public required string EntityLogicalName { get; init; }

    [JsonPropertyName("buttonId")]
    public string? ButtonId { get; init; }
}
