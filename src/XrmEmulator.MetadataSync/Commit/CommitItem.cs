namespace XrmEmulator.MetadataSync.Commit;

public enum CommitItemType
{
    SavedQuery,
    SystemForm,
    SiteMap,
    Entity,
    IconUpload,
    IconSet,
    AppModuleEntity,
    AppModuleView,
    AppModuleForm,
    AppModuleBpf,
    BusinessRule,
    Delete,
    Deprecate,
    NewEntity,
    NewAttribute,
    WebResourceUpload,
    CommandBar,
    RibbonWorkbench,
    PluginRegistration,
    RelationshipUpdate,
    NewManyToManyRelationship,
    DataImport,
    AssociationsImport,
    SecurityRoleUpdate,
    SecurityRoleAssignment,
    WorkflowActivation,
    WorkflowRemoveFromSolution,
    PcfControl,
    OptionSetValue,
    StatusValue,
    AddSolutionComponent,
    EnableChangeTracking,
    CustomApiRegistration,
    SlaItem,
    SlaKpi,
    RelationshipDelete,
    EntityMetadataDelete
}

public record CommitItem(CommitItemType Type, string DisplayName, string FilePath, object ParsedData);
