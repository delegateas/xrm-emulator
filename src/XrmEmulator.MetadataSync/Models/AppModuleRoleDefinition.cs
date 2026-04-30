namespace XrmEmulator.MetadataSync.Models;

public record AppModuleRoleDefinition
{
    public required string AppModuleUniqueName { get; init; }

    /// <summary>
    /// Security role name. Resolved to the role record in the root business unit at commit time —
    /// AppModuleRoleMaps store concrete role record IDs, and the root-BU copy is the canonical one
    /// for custom roles without a role template.
    /// </summary>
    public required string RoleName { get; init; }
}
