namespace XrmEmulator.MetadataSync.Models;

public record CustomApiDefinition
{
    public required string UniqueName { get; init; }
    public required string Name { get; init; }
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsFunction { get; init; } = false;         // false = Action, true = Function
    public int BindingType { get; init; } = 0;              // 0 = Global, 1 = Entity, 2 = EntityCollection
    public string? BoundEntityLogicalName { get; init; }
    public int AllowedCustomProcessingStepType { get; init; } = 0; // 0 = None, 1 = AsyncOnly, 2 = SyncAndAsync
    public bool IsPrivate { get; init; } = false;
    public string? ExecutePrivilegeName { get; init; }
    public required string PluginTypeName { get; init; }    // Full type name in the plugin assembly
    public required string SolutionUniqueName { get; init; }
    public required List<CustomApiRequestParameterDefinition> RequestParameters { get; init; }
    public required List<CustomApiResponsePropertyDefinition> ResponseProperties { get; init; }
}

public record CustomApiRequestParameterDefinition
{
    public required string UniqueName { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public required int Type { get; init; }                 // See CustomApiParameterType enum below
    public bool IsOptional { get; init; } = false;
    public string? LogicalEntityName { get; init; }         // Required when Type = Entity/EntityReference/EntityCollection
}

public record CustomApiResponsePropertyDefinition
{
    public required string UniqueName { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public required int Type { get; init; }                 // See CustomApiParameterType enum below
    public string? LogicalEntityName { get; init; }         // Required when Type = Entity/EntityReference/EntityCollection
}

// Reference enum — matches Dataverse customapiparameter type values
// 0=Boolean, 1=DateTime, 2=Decimal, 3=Entity, 4=EntityCollection,
// 5=EntityReference, 6=Float, 7=Integer, 8=Money, 9=Picklist,
// 10=String, 11=StringArray, 12=Guid
