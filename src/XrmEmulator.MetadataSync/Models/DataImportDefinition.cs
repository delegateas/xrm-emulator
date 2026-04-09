using System.Text.Json;

namespace XrmEmulator.MetadataSync.Models;

public record DataImportDefinition
{
    public required string Table { get; init; }
    public required List<string> MatchOn { get; init; }

    /// <summary>
    /// Optional field type overrides for ambiguous types.
    /// Values: "int", "optionset", "decimal", "money", "string", "bool", "lookup", "multiselect"
    /// Numbers default to "optionset" if not specified (most common in CRM custom entities).
    /// </summary>
    public Dictionary<string, string>? FieldTypes { get; init; }

    public required List<Dictionary<string, JsonElement?>> Rows { get; init; }
}
