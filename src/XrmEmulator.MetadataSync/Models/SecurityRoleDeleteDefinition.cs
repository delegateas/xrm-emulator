namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Stages a security role delete. Deletes the role from the root business unit;
/// Dataverse automatically removes all child-BU copies and user assignments.
/// Fails if the role does not exist — logs a warning instead of throwing.
/// </summary>
public record SecurityRoleDeleteDefinition
{
    public required string RoleName { get; init; }
}
