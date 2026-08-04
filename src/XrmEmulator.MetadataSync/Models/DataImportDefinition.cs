using System.Text.Json;

namespace XrmEmulator.MetadataSync.Models;

public record DataImportDefinition
{
    public required string Table { get; init; }
    public required List<string> MatchOn { get; init; }

    /// <summary>
    /// Optional field type overrides for ambiguous types.
    /// Simple types: "int", "optionset", "decimal", "money", "string", "bool", "multiselect"
    /// Lookup (raw GUID): "lookup" — value must be "entityname:guid"
    /// Lookup (by name):  "lookup:tablename:fieldname" — value is the match-field value;
    ///   the CLI resolves it to an EntityReference by querying CRM. Results are cached
    ///   per import file, so repeated references to the same parent are free.
    ///   Example: "parentaccountid": "lookup:account:name" with value "Acme Corp"
    /// Numbers default to "optionset" if not specified (most common in CRM custom entities).
    /// </summary>
    public Dictionary<string, string>? FieldTypes { get; init; }

    /// <summary>
    /// Optional user to run this import's writes as (sets CallerId on the connection for the
    /// duration of this file only). Accepts a systemuser GUID or an exact fullname.
    /// Use when the target entity's plugins must execute under a specific user's context — e.g. a
    /// virtual-table / Dynamics BFF call that authorizes against the caller's MIA permission.
    /// </summary>
    public string? Impersonate { get; init; }

    public required List<Dictionary<string, JsonElement?>> Rows { get; init; }
}
