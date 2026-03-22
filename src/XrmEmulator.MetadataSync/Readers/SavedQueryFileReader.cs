using System.Xml.Linq;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class SavedQueryFileReader
{
    public static SavedQueryDefinition Parse(string filePath)
    {
        var doc = XDocument.Load(filePath);
        return ParseDocument(doc, filePath);
    }

    public static SavedQueryDefinition ParseFromString(string xmlContent, string sourceFilePath)
    {
        var doc = XDocument.Parse(xmlContent);
        return ParseDocument(doc, sourceFilePath);
    }

    private static SavedQueryDefinition ParseDocument(XDocument doc, string sourceFilePath)
    {
        var root = doc.Root
            ?? throw new InvalidOperationException($"Invalid savedquery XML: no root element in {sourceFilePath}");

        // Handle both <savedquery> as root and <savedqueries><savedquery> wrapper
        var queryElement = root.Name.LocalName == "savedquery"
            ? root
            : root.Element("savedquery")
              ?? throw new InvalidOperationException($"Missing <savedquery> element in {sourceFilePath}");

        // savedqueryid is optional — new views scaffolded by "views new" won't have one
        var savedQueryId = Guid.Empty;
        var idElement = queryElement.Element("savedqueryid");
        if (idElement != null)
        {
            var idText = idElement.Value.Trim().Trim('{', '}');
            savedQueryId = Guid.Parse(idText);
        }

        var name = queryElement
            .Element("LocalizedNames")
            ?.Elements("LocalizedName")
            .FirstOrDefault()
            ?.Attribute("description")
            ?.Value ?? "Unknown";

        var fetchXml = ExtractInnerXml(queryElement.Element("fetchxml"));
        var layoutXml = ExtractInnerXml(queryElement.Element("layoutxml"));
        var returnedTypeCode = queryElement.Element("returnedtypecode")?.Value;

        return new SavedQueryDefinition
        {
            SavedQueryId = savedQueryId,
            Name = name,
            FetchXml = fetchXml,
            LayoutXml = layoutXml,
            ReturnedTypeCode = returnedTypeCode,
            SourceFilePath = sourceFilePath
        };
    }

    private static string? ExtractInnerXml(XElement? element)
    {
        if (element == null) return null;
        // The inner XML contains the actual <fetch> or <grid> element
        var innerContent = string.Concat(element.Nodes());
        return string.IsNullOrWhiteSpace(innerContent) ? null : innerContent.Trim();
    }
}
