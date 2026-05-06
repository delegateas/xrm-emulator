namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Pending file for binding a plug-in assembly to a Power Platform managed identity.
///
/// At commit time the writer:
///   1. Looks up the existing pluginassembly by name (must already be registered + Authenticode-signed).
///   2. Reuses or creates the corresponding managedidentity record (matched by ApplicationId).
///   3. PATCHes pluginassembly.managedidentityid to point at that record.
///
/// Idempotent — re-committing is a no-op when the link is already in place.
/// </summary>
public record PluginManagedIdentityDefinition
{
    public required string AssemblyName { get; init; }
    public required Guid ApplicationId { get; init; }
    public required Guid TenantId { get; init; }
}
