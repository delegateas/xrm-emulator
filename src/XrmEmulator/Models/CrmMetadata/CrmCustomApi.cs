namespace XrmEmulator.Models.CrmMetadata;

public record CrmCustomApi
{
    public required string UniqueName { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public bool IsFunction { get; init; }
    public int BindingType { get; init; }
    public string? BoundEntityLogicalName { get; init; }
    public string? PluginTypeName { get; init; }
    public required string SolutionName { get; init; }
    public List<CrmCustomApiParameter> RequestParameters { get; init; } = [];
    public List<CrmCustomApiParameter> ResponseProperties { get; init; } = [];

    // bindingtype constants
    public const int BindingGlobal = 0;
    public const int BindingEntity = 1;
    public const int BindingEntityCollection = 2;
}

public record CrmCustomApiParameter
{
    public required string UniqueName { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required int Type { get; init; }
    public bool IsOptional { get; init; }

    // CustomApiParameterType constants
    public const int TypeBoolean = 0;
    public const int TypeDateTime = 1;
    public const int TypeDecimal = 2;
    public const int TypeEntity = 3;
    public const int TypeEntityCollection = 4;
    public const int TypeEntityReference = 5;
    public const int TypeFloat = 6;
    public const int TypeInteger = 7;
    public const int TypeMoney = 8;
    public const int TypePicklist = 9;
    public const int TypeString = 10;
    public const int TypeStringArray = 11;
    public const int TypeGuid = 12;
}
