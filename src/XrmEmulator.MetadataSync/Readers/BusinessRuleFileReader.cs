using System.Xml.Linq;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class BusinessRuleFileReader
{
    public static BusinessRuleDefinition Parse(string dataXmlFilePath)
    {
        var doc = XDocument.Load(dataXmlFilePath);
        var xamlPath = DeriveXamlPath(dataXmlFilePath);
        var xaml = File.Exists(xamlPath)
            ? File.ReadAllText(xamlPath)
            : throw new FileNotFoundException($"Companion XAML file not found: {xamlPath}");

        return ParseDocument(doc, xaml, dataXmlFilePath);
    }

    public static BusinessRuleDefinition ParseFromString(string dataXmlContent, string sourceFilePath)
    {
        var doc = XDocument.Parse(dataXmlContent);
        var xamlPath = DeriveXamlPath(sourceFilePath);
        var xaml = File.Exists(xamlPath)
            ? File.ReadAllText(xamlPath)
            : throw new FileNotFoundException($"Companion XAML file not found: {xamlPath}");

        return ParseDocument(doc, xaml, sourceFilePath);
    }

    private static BusinessRuleDefinition ParseDocument(XDocument doc, string xaml, string sourceFilePath)
    {
        var root = doc.Root
            ?? throw new InvalidOperationException($"Invalid workflow XML: no root element in {sourceFilePath}");

        var workflowElement = root.Name.LocalName == "Workflow"
            ? root
            : root.Element("Workflow")
              ?? throw new InvalidOperationException($"Missing <Workflow> element in {sourceFilePath}");

        // WorkflowId is optional — new rules scaffolded by "businessrules new" won't have one
        var workflowId = Guid.Empty;
        var idAttr = workflowElement.Attribute("WorkflowId");
        if (idAttr != null)
        {
            var idText = idAttr.Value.Trim('{', '}');
            workflowId = Guid.Parse(idText);
        }

        var name = workflowElement.Attribute("Name")?.Value ?? "Unknown";

        var description = workflowElement
            .Element("Descriptions")
            ?.Elements("Description")
            .FirstOrDefault()
            ?.Attribute("description")
            ?.Value;

        var primaryEntity = workflowElement.Element("PrimaryEntity")?.Value
            ?? throw new InvalidOperationException($"Missing <PrimaryEntity> element in {sourceFilePath}");

        var scope = 4; // Default: Organization
        var scopeElement = workflowElement.Element("Scope");
        if (scopeElement != null && int.TryParse(scopeElement.Value, out var parsedScope))
            scope = parsedScope;

        Guid? processTriggerFormId = null;
        var formIdElement = workflowElement.Element("ProcessTriggerFormId");
        if (formIdElement != null)
        {
            var formIdText = formIdElement.Value.Trim().Trim('{', '}');
            processTriggerFormId = Guid.Parse(formIdText);
        }

        int? processTriggerScope = null;
        var triggerScopeElement = workflowElement.Element("ProcessTriggerScope");
        if (triggerScopeElement != null && int.TryParse(triggerScopeElement.Value, out var parsedTriggerScope))
            processTriggerScope = parsedTriggerScope;

        return new BusinessRuleDefinition
        {
            WorkflowId = workflowId,
            Name = name,
            Description = description,
            PrimaryEntity = primaryEntity,
            Xaml = xaml,
            Scope = scope,
            ProcessTriggerFormId = processTriggerFormId,
            ProcessTriggerScope = processTriggerScope,
            SourceFilePath = sourceFilePath
        };
    }

    /// <summary>
    /// Derives the companion .xaml file path from a .xaml.data.xml file path.
    /// E.g., "BR-SET-PARTNER-TRUE-GUID.xaml.data.xml" → "BR-SET-PARTNER-TRUE-GUID.xaml"
    /// </summary>
    private static string DeriveXamlPath(string dataXmlFilePath)
    {
        // Remove ".data.xml" suffix to get the .xaml path
        if (dataXmlFilePath.EndsWith(".data.xml", StringComparison.OrdinalIgnoreCase))
            return dataXmlFilePath[..^".data.xml".Length];

        throw new InvalidOperationException(
            $"Expected file to end with '.data.xml': {dataXmlFilePath}");
    }
}
