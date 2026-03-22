using System.Xml.Linq;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class AppActionFileReader
{
    /// <summary>
    /// Parse a single appaction.xml into a CommandBarDefinition.
    /// </summary>
    public static CommandBarDefinition Parse(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        var root = doc.Root
            ?? throw new InvalidOperationException($"Invalid appaction XML: no root element in {xmlPath}");

        var uniqueName = root.Attribute("uniquename")?.Value
            ?? throw new InvalidOperationException($"Missing uniquename attribute in {xmlPath}");

        var name = root.Element("name")?.Value;
        var contextValue = root.Element("contextvalue")?.Value;
        var appModuleUniqueName = root.Element("appmoduleid")?.Element("uniquename")?.Value;

        // buttonlabeltext has a "default" attribute with the label text
        var label = root.Element("buttonlabeltext")?.Attribute("default")?.Value;

        int? location = null;
        if (int.TryParse(root.Element("location")?.Value, out var loc))
            location = loc;

        var functionName = root.Element("onclickeventjavascriptfunctionname")?.Value;

        // Web resource ID (Guid) — we store it for reference but the writer resolves by name
        var webResourceIdStr = root.Element("onclickeventjavascriptwebresourceid")?
            .Element("webresourceid")?.Value;

        var fontIcon = root.Element("fonticon")?.Value;

        bool? hidden = null;
        var hiddenStr = root.Element("hidden")?.Value;
        if (hiddenStr != null)
            hidden = hiddenStr != "0";

        decimal? sequence = null;
        if (decimal.TryParse(root.Element("sequence")?.Value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var seq))
            sequence = seq;

        var parameters = root.Element("onclickeventjavascriptparameters")?.Value;

        return new CommandBarDefinition
        {
            UniqueName = uniqueName,
            Name = name,
            AppModuleUniqueName = appModuleUniqueName,
            EntityLogicalName = contextValue,
            Location = location,
            Label = label,
            FunctionName = functionName,
            FontIcon = fontIcon,
            Hidden = hidden,
            Sequence = sequence,
            Parameters = parameters
        };
    }

    /// <summary>
    /// Scan the appactions/ folder in a solution export and return all definitions.
    /// </summary>
    public static List<CommandBarDefinition> ScanAll(string solutionExportDir)
    {
        // Find the solution folder (first non-hidden, non-underscore dir)
        var solutionFolder = Directory.GetDirectories(solutionExportDir)
            .FirstOrDefault(d =>
            {
                var dirName = Path.GetFileName(d);
                return !dirName.StartsWith('.') && !dirName.StartsWith('_');
            });

        if (solutionFolder == null)
            return [];

        var appActionsDir = Path.Combine(solutionFolder, "appactions");
        if (!Directory.Exists(appActionsDir))
            return [];

        var results = new List<CommandBarDefinition>();
        foreach (var xmlFile in Directory.GetFiles(appActionsDir, "appaction.xml", SearchOption.AllDirectories))
        {
            try
            {
                results.Add(Parse(xmlFile));
            }
            catch
            {
                // Skip malformed files
            }
        }
        return results;
    }

    /// <summary>
    /// Find a specific appaction by matching on the Name element or UniqueName attribute.
    /// </summary>
    public static (CommandBarDefinition? Definition, string? XmlPath) FindByName(
        string solutionExportDir, string buttonName)
    {
        var solutionFolder = Directory.GetDirectories(solutionExportDir)
            .FirstOrDefault(d =>
            {
                var dirName = Path.GetFileName(d);
                return !dirName.StartsWith('.') && !dirName.StartsWith('_');
            });

        if (solutionFolder == null)
            return (null, null);

        var appActionsDir = Path.Combine(solutionFolder, "appactions");
        if (!Directory.Exists(appActionsDir))
            return (null, null);

        foreach (var xmlFile in Directory.GetFiles(appActionsDir, "appaction.xml", SearchOption.AllDirectories))
        {
            try
            {
                var def = Parse(xmlFile);
                if (string.Equals(def.Name, buttonName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(def.UniqueName, buttonName, StringComparison.OrdinalIgnoreCase) ||
                    (def.UniqueName?.StartsWith(buttonName + "!", StringComparison.OrdinalIgnoreCase) == true))
                {
                    return (def, xmlFile);
                }
            }
            catch
            {
                // Skip malformed files
            }
        }

        return (null, null);
    }
}
