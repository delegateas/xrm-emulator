using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

/// <summary>
/// In-place patch of pluginassembly.content + version. No solution / type / step changes.
/// </summary>
public static class PluginContentUpdateWriter
{
    public record ApplyResult(Guid PluginAssemblyId, string PreviousVersion, string NewVersion);

    public static ApplyResult Apply(IOrganizationService service, PluginContentUpdateDefinition def, string baseDir)
    {
        var dllAbsPath = Path.IsPathRooted(def.AssemblyPath)
            ? def.AssemblyPath
            : Path.GetFullPath(Path.Combine(baseDir, def.AssemblyPath));
        if (!File.Exists(dllAbsPath))
            throw new InvalidOperationException($"Plug-in DLL not found at {dllAbsPath}");

        var asmQuery = new QueryExpression("pluginassembly")
        {
            ColumnSet = new ColumnSet("pluginassemblyid", "name", "version"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, def.AssemblyName)
                }
            },
            TopCount = 1
        };
        var asmResults = service.RetrieveMultiple(asmQuery);
        if (asmResults.Entities.Count == 0)
            throw new InvalidOperationException(
                $"Plug-in assembly '{def.AssemblyName}' not found in CRM. Use plugin register/update for first-time deploys.");

        var existing = asmResults.Entities[0];
        var previousVersion = existing.GetAttributeValue<string>("version") ?? "<unknown>";

        var bytes = File.ReadAllBytes(dllAbsPath);
        var update = new Entity("pluginassembly", existing.Id)
        {
            ["content"] = Convert.ToBase64String(bytes),
            ["version"] = def.Version
        };
        service.Update(update);

        return new ApplyResult(existing.Id, previousVersion, def.Version);
    }
}
