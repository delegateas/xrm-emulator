using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using XrmEmulator.MetadataSync.Writers;

namespace XrmEmulator.MetadataSync.Readers;

/// <summary>
/// Exports full merged ribbon XML for each entity in the solution.
/// Uses RetrieveEntityRibbonRequest to get the complete ribbon definition
/// (not just the diff), which is useful for discovering button IDs for HideCustomAction.
/// </summary>
public static class RibbonExporter
{
    /// <summary>
    /// Export ribbon XML for all entities found in the solution export folder.
    /// Writes per-entity XML files to a Ribbon/ folder and a summary index.
    /// </summary>
    public static int Export(
        IOrganizationService service,
        string solutionExportDir,
        string solutionUniqueName,
        string outputDirectory,
        Action<string>? log = null)
    {
        var ribbonDir = Path.Combine(outputDirectory, "Ribbon");
        if (Directory.Exists(ribbonDir))
            Directory.Delete(ribbonDir, recursive: true);
        Directory.CreateDirectory(ribbonDir);

        var entitiesDir = Path.Combine(solutionExportDir, solutionUniqueName, "Entities");
        if (!Directory.Exists(entitiesDir))
        {
            log?.Invoke("No Entities folder found in solution export — skipping ribbon export.");
            return 0;
        }

        var entityFolders = Directory.GetDirectories(entitiesDir)
            .Select(d => Path.GetFileName(d))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var summaryLines = new List<string>();
        summaryLines.Add("# Ribbon Button Reference");
        summaryLines.Add("");
        summaryLines.Add("Full merged ribbon XML for each entity, retrieved via `RetrieveEntityRibbonRequest`.");
        summaryLines.Add("Use this to discover button IDs for `HideCustomAction` entries.");
        summaryLines.Add("");

        var exportedCount = 0;

        foreach (var folderName in entityFolders)
        {
            // Derive logical name from Entity.xml
            var entityXmlPath = Path.Combine(entitiesDir, folderName, "Entity.xml");
            var logicalName = ResolveLogicalName(entityXmlPath, folderName);

            log?.Invoke($"  Retrieving ribbon for {logicalName}...");

            try
            {
                var ribbonXml = RibbonImportWriter.RetrieveEntityRibbonXml(service, logicalName);
                if (string.IsNullOrWhiteSpace(ribbonXml))
                {
                    log?.Invoke($"    (empty ribbon for {logicalName})");
                    continue;
                }

                var outputPath = Path.Combine(ribbonDir, $"{logicalName}.xml");
                File.WriteAllText(outputPath, ribbonXml);
                exportedCount++;

                // Extract button IDs for the summary
                var buttons = ExtractButtonIds(ribbonXml);
                if (buttons.Count > 0)
                {
                    summaryLines.Add($"## {folderName} (`{logicalName}`)");
                    summaryLines.Add("");
                    summaryLines.Add("| Location | Button ID |");
                    summaryLines.Add("|----------|-----------|");
                    foreach (var (location, id) in buttons)
                    {
                        summaryLines.Add($"| {location} | `{id}` |");
                    }
                    summaryLines.Add("");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"    WARNING: Failed to retrieve ribbon for {logicalName}: {ex.Message}");
            }
        }

        // Write summary
        var summaryPath = Path.Combine(ribbonDir, "ribbon-buttons.md");
        File.WriteAllText(summaryPath, string.Join(Environment.NewLine, summaryLines));

        return exportedCount;
    }

    /// <summary>
    /// Extract button control IDs from the full ribbon XML.
    /// Finds CommandUIDefinition elements that define buttons (controls with Id attributes).
    /// </summary>
    internal static List<(string Location, string Id)> ExtractButtonIds(string ribbonXml)
    {
        var results = new List<(string, string)>();

        try
        {
            var doc = XDocument.Parse(ribbonXml);

            // Look for Button, FlyoutAnchor, SplitButton elements with Id attributes
            // These are the control IDs used as HideCustomAction Location values
            var controlElements = doc.Descendants()
                .Where(e => e.Name.LocalName is "Button" or "FlyoutAnchor" or "SplitButton"
                    && e.Attribute("Id") != null);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in controlElements)
            {
                var id = el.Attribute("Id")!.Value;
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                    continue;

                // Derive location category from the ID pattern
                var location = CategorizeButtonId(id);
                results.Add((location, id));
            }
        }
        catch
        {
            // If parsing fails, return empty — the raw XML file is still available
        }

        return results.OrderBy(r => r.Item1).ThenBy(r => r.Item2).ToList();
    }

    private static string CategorizeButtonId(string id)
    {
        if (id.Contains(".SubGrid.", StringComparison.OrdinalIgnoreCase))
            return "SubGrid";
        if (id.Contains(".HomepageGrid.", StringComparison.OrdinalIgnoreCase))
            return "HomepageGrid";
        if (id.Contains(".Form.", StringComparison.OrdinalIgnoreCase))
            return "Form";
        return "Other";
    }

    private static string ResolveLogicalName(string entityXmlPath, string folderName)
    {
        if (!File.Exists(entityXmlPath))
            return folderName.ToLowerInvariant();

        try
        {
            var doc = XDocument.Load(entityXmlPath);
            // Entity.xml has <Entity><Name>logicalname</Name>...</Entity>
            // or <entity Name="..."> depending on the format
            var nameElement = doc.Descendants("Name").FirstOrDefault();
            if (nameElement != null && !string.IsNullOrWhiteSpace(nameElement.Value))
                return nameElement.Value.ToLowerInvariant();

            var nameAttr = doc.Descendants("entity").FirstOrDefault()?.Attribute("Name");
            if (nameAttr != null && !string.IsNullOrWhiteSpace(nameAttr.Value))
                return nameAttr.Value.ToLowerInvariant();
        }
        catch
        {
            // Fall through to folder name
        }

        return folderName.ToLowerInvariant();
    }
}
