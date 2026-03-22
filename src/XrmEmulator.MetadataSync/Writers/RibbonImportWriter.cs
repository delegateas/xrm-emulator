using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace XrmEmulator.MetadataSync.Writers;

public static class RibbonImportWriter
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public static void ImportHideActions(
        IOrganizationService service,
        string solutionUniqueName,
        string publisherPrefix,
        string entityLogicalName,
        string entityDisplayName,
        List<string> hideLocations,
        string? existingRibbonDiffXml,
        string solutionXmlContent)
    {
        var ribbonDiffDoc = string.IsNullOrWhiteSpace(existingRibbonDiffXml)
            ? CreateEmptyRibbonDiffXml()
            : XDocument.Parse(existingRibbonDiffXml);

        var mergedDoc = MergeHideActions(ribbonDiffDoc, publisherPrefix, hideLocations);

        var solutionXml = BuildMinimalSolutionXml(solutionXmlContent, entityLogicalName);
        var customizationsXml = BuildCustomizationsXml(entityDisplayName, mergedDoc);
        var contentTypesXml = BuildContentTypesXml();

        using var memoryStream = new MemoryStream();
        using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(zip, "[Content_Types].xml", contentTypesXml);
            AddZipEntry(zip, "solution.xml", solutionXml);
            AddZipEntry(zip, "customizations.xml", customizationsXml);
        }

        memoryStream.Position = 0;
        var zipBytes = memoryStream.ToArray();

        var importRequest = new ImportSolutionRequest
        {
            CustomizationFile = zipBytes,
            OverwriteUnmanagedCustomizations = true,
            PublishWorkflows = false
        };
        service.Execute(importRequest);
    }

    /// <summary>
    /// Retrieve the entity's RibbonDiffXml from CRM and search for buttons matching a pattern.
    /// </summary>
    public static string? RetrieveEntityRibbonXml(IOrganizationService service, string entityLogicalName)
    {
        var request = new RetrieveEntityRibbonRequest
        {
            EntityName = entityLogicalName,
            RibbonLocationFilter = RibbonLocationFilters.All
        };
        var response = (RetrieveEntityRibbonResponse)service.Execute(request);
        var compressed = response.CompressedEntityXml;

        using var ms = new MemoryStream(compressed);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault();
        if (entry == null) return null;

        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Hides OOTB ribbon buttons using HideCustomAction — the standard approach used by
    /// Ribbon Workbench and documented by Microsoft.
    /// The button IDs passed in are used directly as HideCustomAction Location values.
    /// Use RetrieveEntityRibbonRequest (or the Ribbon/ export folder) to discover button IDs.
    /// </summary>
    public static XDocument MergeHideActions(XDocument ribbonDiffDoc, string publisherPrefix, List<string> hideButtonNames)
    {
        var root = ribbonDiffDoc.Root!;
        var customActions = root.Element("CustomActions");
        if (customActions == null)
        {
            customActions = new XElement("CustomActions");
            root.AddFirst(customActions);
        }

        var existingIds = new HashSet<string>(
            customActions.Elements("HideCustomAction")
                .Select(e => e.Attribute("HideActionId")?.Value ?? ""),
            StringComparer.OrdinalIgnoreCase);

        foreach (var buttonId in hideButtonNames)
        {
            var hideActionId = $"{publisherPrefix}.{buttonId}.Hide";
            if (existingIds.Contains(hideActionId))
                continue;

            customActions.Add(new XElement("HideCustomAction",
                new XAttribute("HideActionId", hideActionId),
                new XAttribute("Location", buttonId)));
        }

        return ribbonDiffDoc;
    }


    internal static string BuildMinimalSolutionXml(string solutionXmlContent, string entityLogicalName)
    {
        var doc = XDocument.Parse(solutionXmlContent);
        var manifest = doc.Root!.Element("SolutionManifest")!;

        // Keep only the RootComponent for the target entity (type=1).
        // CRM requires the entity to be declared as a root component for ribbon import.
        var rootComponents = manifest.Element("RootComponents");
        if (rootComponents != null)
        {
            var entityComponent = rootComponents.Elements("RootComponent")
                .FirstOrDefault(rc =>
                    rc.Attribute("type")?.Value == "1" &&
                    string.Equals(rc.Attribute("schemaName")?.Value, entityLogicalName, StringComparison.OrdinalIgnoreCase));

            rootComponents.RemoveNodes();
            if (entityComponent != null)
                rootComponents.Add(entityComponent);
        }
        else
        {
            manifest.Add(new XElement("RootComponents",
                new XElement("RootComponent",
                    new XAttribute("type", "1"),
                    new XAttribute("schemaName", entityLogicalName),
                    new XAttribute("behavior", "0"))));
        }

        // Keep MissingDependencies as empty node (CRM NullRefs if absent)
        var missingDeps = manifest.Element("MissingDependencies");
        if (missingDeps != null)
            missingDeps.RemoveNodes();
        else
            manifest.Add(new XElement("MissingDependencies"));

        return doc.Declaration != null
            ? doc.Declaration + Environment.NewLine + doc.Root
            : doc.ToString();
    }

    internal static string BuildCustomizationsXml(string entityDisplayName, XDocument ribbonDiffDoc)
    {
        // Structure matches what Ribbon Workbench generates for ribbon-only imports
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("ImportExportXml",
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
                new XElement("Entities",
                    new XElement("Entity",
                        new XElement("Name",
                            new XAttribute("LocalizedName", entityDisplayName),
                            new XAttribute("OriginalName", ""),
                            entityDisplayName),
                        new XElement("EntityInfo",
                            new XElement("entity",
                                new XAttribute("Name", entityDisplayName),
                                new XAttribute("unmodified", "1"),
                                new XElement("attributes"))),
                        new XElement("RibbonDiffXml", ribbonDiffDoc.Root!.Elements()))),
                new XElement("Roles"),
                new XElement("Workflows"),
                new XElement("FieldSecurityProfiles"),
                new XElement("Templates"),
                new XElement("EntityMaps"),
                new XElement("EntityRelationships"),
                new XElement("OrganizationSettings"),
                new XElement("optionsets"),
                new XElement("CustomControls"),
                new XElement("EntityDataProviders"),
                new XElement("Languages",
                    new XElement("Language", "1033"))));

        return doc.Declaration + Environment.NewLine + doc.Root;
    }

    internal static string BuildContentTypesXml()
    {
        var doc = new XDocument(
            new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Types",
                new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/octet-stream"))));
        return doc.ToString();
    }

    internal static XDocument CreateEmptyRibbonDiffXml()
    {
        return new XDocument(
            new XElement("RibbonDiffXml",
                new XElement("CustomActions"),
                new XElement("Templates",
                    new XElement("RibbonTemplates", new XAttribute("Id", "Mscrm.Templates"))),
                new XElement("CommandDefinitions"),
                new XElement("RuleDefinitions",
                    new XElement("TabDisplayRules"),
                    new XElement("DisplayRules"),
                    new XElement("EnableRules")),
                new XElement("LocLabels")));
    }

    private static void AddZipEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
