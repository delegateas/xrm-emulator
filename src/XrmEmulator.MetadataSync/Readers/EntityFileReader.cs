using System.Xml.Linq;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class EntityFileReader
{
    public static EntityDefinition Parse(string filePath)
    {
        var doc = XDocument.Load(filePath);
        var root = doc.Root
            ?? throw new InvalidOperationException($"Invalid Entity XML: no root element in {filePath}");

        var entityName = root.Element("Name")?.Value
            ?? root.Element("name")?.Value
            ?? throw new InvalidOperationException($"Missing <Name> in {filePath}");

        var displayName = root.Element("EntityInfo")
            ?.Element("entity")
            ?.Element("displaynames")
            ?.Elements("displayname")
            .FirstOrDefault()
            ?.Attribute("description")?.Value
            ?? entityName;

        var attributes = ParseAttributes(root);

        return new EntityDefinition
        {
            LogicalName = entityName.ToLowerInvariant(),
            DisplayName = displayName,
            Attributes = attributes,
            SourceFilePath = filePath
        };
    }

    private static List<AttributeDefinition> ParseAttributes(XElement root)
    {
        var attributes = new List<AttributeDefinition>();

        var entityInfo = root.Element("EntityInfo")?.Element("entity");
        if (entityInfo == null) return attributes;

        var attrElements = entityInfo.Element("attributes")?.Elements("attribute")
            ?? Enumerable.Empty<XElement>();

        foreach (var attr in attrElements)
        {
            var logicalName = attr.Element("LogicalName")?.Value
                ?? attr.Attribute("PhysicalName")?.Value
                ?? "unknown";

            var attrDisplayName = attr.Element("displaynames")
                ?.Elements("displayname")
                .FirstOrDefault()
                ?.Attribute("description")?.Value ?? logicalName;

            var type = attr.Element("Type")?.Value ?? "unknown";
            var isCustomField = attr.Element("IsCustomField")?.Value == "1";

            var description = attr.Element("Descriptions")
                ?.Elements("Description")
                .FirstOrDefault()
                ?.Attribute("description")?.Value;

            var requiredLevel = attr.Element("RequiredLevel")?.Value;
            var maxLength = ParseInt(attr.Element("MaxLength")?.Value);
            var minValue = ParseInt(attr.Element("MinValue")?.Value);
            var maxValue = ParseInt(attr.Element("MaxValue")?.Value);
            var accuracy = ParseInt(attr.Element("Accuracy")?.Value);
            var minValueDecimal = ParseDecimal(attr.Element("MinValue")?.Value);
            var maxValueDecimal = ParseDecimal(attr.Element("MaxValue")?.Value);
            var format = attr.Element("Format")?.Value;

            var options = ParseOptions(attr);

            attributes.Add(new AttributeDefinition
            {
                LogicalName = logicalName,
                DisplayName = attrDisplayName,
                Type = type,
                Description = description,
                RequiredLevel = requiredLevel,
                MaxLength = maxLength,
                MinValue = minValue,
                MaxValue = maxValue,
                Accuracy = accuracy,
                MinValueDecimal = minValueDecimal,
                MaxValueDecimal = maxValueDecimal,
                Format = format,
                IsCustomField = isCustomField,
                Options = options.Count > 0 ? options : null
            });
        }

        return attributes;
    }

    private static List<OptionDefinition> ParseOptions(XElement attr)
    {
        var options = new List<OptionDefinition>();

        var optionSet = attr.Element("optionset");
        if (optionSet == null) return options;

        foreach (var opt in optionSet.Elements("option"))
        {
            var valueStr = opt.Attribute("value")?.Value;
            if (valueStr == null || !int.TryParse(valueStr, out var value)) continue;

            var label = opt.Elements("labels")
                .Elements("label")
                .FirstOrDefault()
                ?.Attribute("description")?.Value ?? value.ToString();

            options.Add(new OptionDefinition { Value = value, Label = label });
        }

        return options;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, out var result) ? result : null;
}
