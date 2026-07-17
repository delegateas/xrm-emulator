using System.Runtime.Serialization;
using DG.Tools.XrmMockup;

namespace XrmEmulator.Services;

/// <summary>
/// Independently re-reads the combined Metadata.xml that XrmMockup365 itself loads at startup, purely
/// to list registered plugin steps for the `/plugins` dev-tool route. PluginManager's own registration
/// table is internal to XrmMockup365 and not exposed, so this reads the same file from disk instead of
/// reaching into the running instance.
/// </summary>
public class PluginRegistrationService
{
    private readonly List<MetaPlugin> _plugins;

    public PluginRegistrationService(string? metadataDirectoryPath)
    {
        _plugins = Load(metadataDirectoryPath);
    }

    private static List<MetaPlugin> Load(string? metadataDirectoryPath)
    {
        if (string.IsNullOrEmpty(metadataDirectoryPath))
            return [];

        var path = Path.Combine(metadataDirectoryPath, "Metadata.xml");
        if (!File.Exists(path))
            return [];

        try
        {
            var serializer = new DataContractSerializer(typeof(MetadataSkeleton));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var skeleton = (MetadataSkeleton)serializer.ReadObject(stream)!;
            return skeleton.Plugins ?? [];
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "PluginRegistrationService: failed to read plugin metadata from {Path}", path);
            return [];
        }
    }

    public IReadOnlyList<MetaPlugin> GetForEntity(string entityLogicalName) =>
        _plugins.Where(p => string.Equals(p.PrimaryEntity, entityLogicalName, StringComparison.OrdinalIgnoreCase)).ToList();
}
