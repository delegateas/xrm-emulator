namespace XrmEmulator.MetadataSync.Models;

public record SolutionComponentDefinition
{
    /// <summary>Entity logical name (e.g. "lead"). Used to resolve the attribute MetadataId at commit time.</summary>
    public required string EntityLogicalName { get; init; }

    /// <summary>Attribute logical name (e.g. "kf_existingcustomer"). Used to resolve the attribute MetadataId.</summary>
    public required string AttributeLogicalName { get; init; }

    /// <summary>Component type name: "attribute"</summary>
    public required string ComponentType { get; init; }

    /// <summary>Display label for pending list</summary>
    public string? DisplayName { get; init; }

    /// <summary>Target solution to add the component to</summary>
    public required string SolutionUniqueName { get; init; }
}
