namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Stages removal of specific privileges from a security role.
/// Uses RemovePrivilegeRoleRequest — only the listed privileges are removed;
/// all other privileges on the role are left untouched.
/// </summary>
public record SecurityRolePrivilegeRemoveDefinition
{
    public required string RoleName { get; init; }
    public List<PrivilegeEntry> Privileges { get; init; } = new();
}
