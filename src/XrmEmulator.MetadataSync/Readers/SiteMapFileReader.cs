using System.Xml.Linq;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class SiteMapFileReader
{
    public static SiteMapDefinition Parse(string filePath, string uniqueName)
    {
        var doc = XDocument.Load(filePath);
        var root = doc.Root
            ?? throw new InvalidOperationException($"Invalid AppModuleSiteMap XML: no root element in {filePath}");

        // Extract the display name from AppModuleSiteMap > SiteMap or use uniqueName
        var siteMapElement = root.Descendants("SiteMap").FirstOrDefault()
            ?? root.Descendants("sitemap").FirstOrDefault();

        var name = root.Attribute("IntroducedVersion") != null
            ? uniqueName
            : uniqueName;

        // Try to extract a friendly name from the first Area title
        var firstAreaTitle = root.Descendants("Area").FirstOrDefault()
            ?.Descendants("Title").FirstOrDefault()
            ?.Attribute("description")?.Value
            ?? root.Descendants("Area").FirstOrDefault()
                ?.Attribute("Title")?.Value;

        var displayName = firstAreaTitle ?? uniqueName;

        // Extract <SiteMap> inner XML
        var siteMapXml = siteMapElement != null
            ? siteMapElement.ToString()
            : string.Concat(root.Nodes());

        return new SiteMapDefinition
        {
            UniqueName = uniqueName,
            Name = displayName,
            SiteMapXml = siteMapXml,
            SourceFilePath = filePath
        };
    }
}
