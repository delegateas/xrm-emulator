namespace XrmEmulator.MetadataSync.Models;

public record SecurityRoleAssignmentDefinition
{
    public required string RoleName { get; init; }

    /// <summary>
    /// systemuser identifier — domain name, full name, application user full name,
    /// or the systemuserid GUID as a string. Resolved at commit time.
    /// </summary>
    public required string User { get; init; }
}
