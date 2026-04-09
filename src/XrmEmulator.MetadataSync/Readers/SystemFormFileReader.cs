using System.Xml.Linq;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class SystemFormFileReader
{
    public static SystemFormDefinition Parse(string filePath)
    {
        var doc = XDocument.Load(filePath);
        return ParseDocument(doc, filePath);
    }

    public static SystemFormDefinition ParseFromString(string xmlContent, string sourceFilePath)
    {
        var doc = XDocument.Parse(xmlContent);
        return ParseDocument(doc, sourceFilePath);
    }

    private static SystemFormDefinition ParseDocument(XDocument doc, string sourceFilePath)
    {
        var root = doc.Root
            ?? throw new InvalidOperationException($"Invalid systemform XML: no root element in {sourceFilePath}");

        // Handle both <systemform> as root and <forms><systemform> wrapper
        var formElement = root.Name.LocalName == "systemform"
            ? root
            : root.Element("systemform")
              ?? throw new InvalidOperationException($"Missing <systemform> element in {sourceFilePath}");

        // formid is optional — new forms scaffolded by "forms new" won't have one
        var formId = Guid.Empty;
        var idElement = formElement.Element("formid");
        if (idElement != null)
        {
            var idText = idElement.Value.Trim().Trim('{', '}');
            formId = Guid.Parse(idText);
        }

        var name = formElement
            .Element("LocalizedNames")
            ?.Elements("LocalizedName")
            .FirstOrDefault()
            ?.Attribute("description")
            ?.Value ?? "Unknown";

        // Solution export uses <form>, the systemform entity column "formxml" expects <form> as root
        var formXml = formElement.Element("form")?.ToString();
        var objectTypeCode = formElement.Element("objecttypecode")?.Value;

        // Detect form type from folder: quickCreate → 7, quick → 6 (Quick View), main → 2 (default)
        var formType = 2;
        if (sourceFilePath.Contains("/quickCreate/", StringComparison.OrdinalIgnoreCase)
            || sourceFilePath.Contains("\\quickCreate\\", StringComparison.OrdinalIgnoreCase))
        {
            formType = 7;
        }
        else if (sourceFilePath.Contains("/quick/", StringComparison.OrdinalIgnoreCase)
            || sourceFilePath.Contains("\\quick\\", StringComparison.OrdinalIgnoreCase))
        {
            formType = 6;
        }

        return new SystemFormDefinition
        {
            FormId = formId,
            Name = name,
            FormXml = formXml ?? "",
            ObjectTypeCode = objectTypeCode,
            FormType = formType,
            SourceFilePath = sourceFilePath
        };
    }

}
