namespace XrmEmulator.Models.CrmMetadata;

public record CrmForm
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string EntityName { get; init; }
    public string FormType { get; init; } = "main";
    public int FormPresentation { get; init; }
    public List<CrmTab> Tabs { get; init; } = [];
    public List<CrmFormEvent> Events { get; init; } = [];
    public List<string> Libraries { get; init; } = [];
}

public record CrmFormEvent
{
    public required string EventName { get; init; }
    public required string FunctionName { get; init; }
    public required string LibraryName { get; init; }
    public bool PassExecutionContext { get; init; }
    public bool Enabled { get; init; } = true;
}

public record CrmTab
{
    public string? Name { get; init; }
    public required string Label { get; init; }
    public List<CrmFormColumn> Columns { get; init; } = [];
}

public record CrmFormColumn
{
    public string Width { get; init; } = "100%";
    public List<CrmSection> Sections { get; init; } = [];
}

public record CrmSection
{
    public string? Name { get; init; }
    public string? Label { get; init; }
    public bool ShowLabel { get; init; } = true;
    public List<CrmFormField> Fields { get; init; } = [];
}

public record CrmFormField
{
    public string? DataFieldName { get; init; }
    public string? Label { get; init; }
    public string? ControlClassId { get; init; }
    public bool IsHidden { get; init; }
    public bool IsSubgrid { get; init; }
    public string? SubgridEntityType { get; init; }
    public Guid? SubgridViewId { get; init; }
    public string? SubgridRelationshipName { get; init; }
}
