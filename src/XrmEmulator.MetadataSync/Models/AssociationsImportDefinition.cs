namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Imports N:N (many-to-many) associations between existing records.
/// Records are resolved by the given match-on attribute on each side,
/// then associated via <c>AssociateRequest</c> with the named relationship.
/// Idempotent — pairs that already exist are skipped.
/// </summary>
public record AssociationsImportDefinition
{
    /// <summary>Schema name of the N:N relationship (e.g. "kf_leaddistributionregion_postnummer").</summary>
    public required string Relationship { get; init; }

    public required AssociationSide Entity1 { get; init; }
    public required AssociationSide Entity2 { get; init; }

    /// <summary>Each pair has keys "entity1" and "entity2" with the match-on values for each side.</summary>
    public required List<Dictionary<string, string>> Pairs { get; init; }
}

public record AssociationSide
{
    /// <summary>Entity logical name (e.g. "kf_leaddistributionregion").</summary>
    public required string Table { get; init; }

    /// <summary>Attribute on the entity used to look up records by value (e.g. "kf_name", "kf_zipcode").</summary>
    public required string MatchOn { get; init; }
}
