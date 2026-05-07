namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Pending file for binding a plug-in assembly to a Power Platform managed identity.
///
/// At commit time the writer:
///   1. Reuses or creates a managedidentity record:
///        - If ManagedIdentityId is set, looks up by that id first (lets multiple envs
///          share a single row id so kf-dev solution exports import cleanly into kf-tst).
///        - Otherwise falls back to lookup by ApplicationId.
///   2. Looks up the pluginassembly by name. If found AND Authenticode-signed, PATCHes
///      managedidentityid. If not found, creates only the managedidentity row and skips
///      the link — useful for pre-staging a target env before the kf-dev solution lands.
///
/// Idempotent — re-committing is a no-op when the row + link are already in place.
/// </summary>
public record PluginManagedIdentityDefinition
{
    public required string AssemblyName { get; init; }
    public required Guid ApplicationId { get; init; }
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Optional. Pin the managedidentity row's primary key to a specific GUID so the
    /// same id is reused across environments. Without this, each env would generate
    /// its own row id and a kf-dev solution export would fail to import into kf-tst
    /// with "Entity 'managedidentity' With Id = ... Does Not Exist".
    /// </summary>
    public Guid? ManagedIdentityId { get; init; }
}
