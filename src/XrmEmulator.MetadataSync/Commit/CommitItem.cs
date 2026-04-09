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
    BusinessRule,
    Delete,
    Deprecate,
    NewAttribute,
    WebResourceUpload,
    CommandBar,
    RibbonWorkbench,
    PluginRegistration,
    RelationshipUpdate,
    DataImport,
    SecurityRoleUpdate,
    PcfControl,
    OptionSetValue,
    StatusValue
}

public record CommitItem(CommitItemType Type, string DisplayName, string FilePath, object ParsedData);
