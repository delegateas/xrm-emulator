namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Pending file model for creating/updating Dataverse Environment Variables.
/// Pending file extension: *.envvar.json
/// Pending folder: _pending/EnvironmentVariables/
/// </summary>
public record EnvironmentVariableFileDefinition
{
    /// <summary>Unique name of the solution to add new definitions to (e.g. "KFPartner").</summary>
    public required string SolutionUniqueName { get; init; }

    public required List<EnvironmentVariableEntry> Variables { get; init; }
}

public record EnvironmentVariableEntry
{
    /// <summary>Schema name, e.g. "kf_SfmcBaseUrl". Must be unique across the org.</summary>
    public required string SchemaName { get; init; }

    /// <summary>Human-readable display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Variable type: String, Number, Boolean, JSON, DataSource, Secret.
    /// Maps to option set values on environmentvariabledefinition.type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Default value stored on the definition (may be empty).</summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Current environment-specific value stored on environmentvariablevalue.
    /// Omit or set null to skip creating/updating the value record.
    /// </summary>
    public string? CurrentValue { get; init; }
}

/// <summary>Commit item payload for a single environment variable (one per SelectMany expansion).</summary>
public record EnvironmentVariableSingleItem(string SolutionUniqueName, EnvironmentVariableEntry Entry);
