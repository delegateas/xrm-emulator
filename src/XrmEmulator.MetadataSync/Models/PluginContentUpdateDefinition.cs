namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Pending file for an in-place plug-in assembly content patch.
///
/// Looks up the existing pluginassembly by name and PATCHes only its `content` (DLL
/// bytes) and `version` columns. Does NOT touch solution membership, plug-in types,
/// or step registrations — meant for hot-fixing an existing assembly's bytes when a
/// solution import can't update them (e.g. because the importing solution payload's
/// managedidentityid PATCH is rejected due to unsigned existing bytes — chicken/egg
/// resolved by patching bytes first, then re-importing the solution).
/// </summary>
public record PluginContentUpdateDefinition
{
    public required string AssemblyName { get; init; }
    public required string AssemblyPath { get; init; }  // relative to env baseDir
    public required string Version { get; init; }
}
