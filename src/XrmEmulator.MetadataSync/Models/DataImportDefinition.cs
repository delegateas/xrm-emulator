using System.Text.Json;

namespace XrmEmulator.MetadataSync.Models;

public record DataImportDefinition
{
    public required string Table { get; init; }
    public required List<string> MatchOn { get; init; }

    /// <summary>
    /// Optional human-readable description of what this file imports, shown instead of the bare
    /// table name in the pending list and the commit selection prompt. Several files importing the
    /// same table are otherwise indistinguishable there — "account (214 row(s))" four times over —
    /// which matters most when an operator who did not author the files has to select them all.
    /// </summary>
    public string? Label { get; init; }

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

    /// <summary>
    /// When true, a row whose <c>lookup:&lt;table&gt;:&lt;field&gt;</c> value has no match in the target
    /// environment is skipped and counted instead of failing the file.
    /// Set it only where the referenced record is somebody else's to create — a migration that points at
    /// users the target environment has not been given yet — and where the rest of the file is worth
    /// having in the meantime. Rows are upserted on their match key, so re-running the file once the
    /// records exist adds exactly the skipped rows and leaves the others untouched.
    /// It is not a way to make a file "pass": every skipped row is logged with its key and counted in
    /// the file's summary, because a quietly short import is worse than a loud failure.
    /// An ambiguous lookup is never skipped — see <see cref="Writers.LookupNotFoundException"/>.
    /// </summary>
    public bool? SkipRowsWithUnresolvedLookups { get; init; }

    public required List<Dictionary<string, JsonElement?>> Rows { get; init; }
}
