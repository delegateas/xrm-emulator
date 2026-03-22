using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Writers;

public static class CommandBarWriter
{
    // Known FunctionParameterType values (discovered from Command Designer exports)
    private static readonly Dictionary<string, int> ParameterTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PrimaryControl"] = 5,
        ["SelectedEntityTypeName"] = 8,
        ["SelectedControl"] = 12,
    };

    /// <summary>
    /// Converts named parameter tokens (e.g. "PrimaryControl,SelectedControl")
    /// into the JSON array format expected by onclickeventjavascriptparameters.
    /// </summary>
    public static string? ResolveParameterNames(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        // Already JSON array — pass through
        if (input.TrimStart().StartsWith("[")) return input;

        var names = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var entries = new List<string>();
        foreach (var name in names)
        {
            if (!ParameterTypeMap.TryGetValue(name, out var typeVal))
                throw new InvalidOperationException(
                    $"Unknown parameter type '{name}'. Known types: {string.Join(", ", ParameterTypeMap.Keys)}");
            entries.Add($"{{\"type\":{typeVal}}}");
        }
        return $"[{string.Join(",", entries)}]";
    }

    /// <summary>
    /// Validate the appaction XML before committing to CRM.
    /// </summary>
    public static void ValidateXml(XDocument doc, string filePath)
    {
        var root = doc.Root
            ?? throw new InvalidOperationException($"Invalid appaction XML: no root element in {filePath}");

        var uniqueName = root.Attribute("uniquename")?.Value;
        if (string.IsNullOrEmpty(uniqueName))
            throw new InvalidOperationException($"Missing uniquename attribute in {filePath}");

        var appModule = root.Element("appmoduleid")?.Element("uniquename")?.Value;
        if (string.IsNullOrEmpty(appModule))
            throw new InvalidOperationException(
                $"CommandBar '{uniqueName}': missing <appmoduleid><uniquename> in {filePath}");

        var entityLogicalName = root.Element("contextvalue")?.Value;
        if (string.IsNullOrEmpty(entityLogicalName))
            throw new InvalidOperationException(
                $"CommandBar '{uniqueName}': missing <contextvalue> (entity logical name) in {filePath}");

        // Validate JS handler consistency: if onclickeventtype=2 (JS), both function and webresource must be set
        var onClickType = ParseInt(root, "onclickeventtype");
        if (onClickType == 2)
        {
            var functionName = root.Element("onclickeventjavascriptfunctionname")?.Value;
            var wrElement = root.Element("onclickeventjavascriptwebresourceid");
            var wrName = wrElement?.Element("name")?.Value;
            var wrId = wrElement?.Element("webresourceid")?.Value;

            if (string.IsNullOrEmpty(functionName))
                throw new InvalidOperationException(
                    $"CommandBar '{uniqueName}': onclickeventtype is JavaScript (2) but " +
                    "<onclickeventjavascriptfunctionname> is missing.");

            if (string.IsNullOrEmpty(wrName) && string.IsNullOrEmpty(wrId))
                throw new InvalidOperationException(
                    $"CommandBar '{uniqueName}': onclickeventtype is JavaScript (2) but " +
                    "<onclickeventjavascriptwebresourceid> has no <name> or <webresourceid>.");
        }

        // Check for TODO placeholders
        var allText = doc.ToString();
        if (allText.Contains("TODO:"))
            throw new InvalidOperationException(
                $"CommandBar '{uniqueName}': XML still contains TODO placeholders. " +
                "Edit the file to fill in all values before committing.");
    }

    /// <summary>
    /// Create or update an appaction from its XML definition.
    /// For edits: finds existing by name+appmodule, then updates.
    /// For new buttons: creates with solution association.
    /// </summary>
    public static Guid CreateOrUpdateFromXml(
        IOrganizationService service,
        XDocument doc,
        string solutionUniqueName)
    {
        ValidateXml(doc, "(pending)");

        var root = doc.Root!;
        var uniqueName = root.Attribute("uniquename")!.Value;
        var name = root.Element("name")?.Value;
        var appModuleUniqueName = root.Element("appmoduleid")!.Element("uniquename")!.Value;
        var appModuleId = AppModuleWriter.GetAppModuleId(service, appModuleUniqueName);

        // Try to find existing:
        // 1. By uniquename + appmodule (app-scoped match)
        // 2. By uniquename only (table/global-scoped records have no appmoduleid)
        // 3. By name + appmodule (OOTB overrides)
        // 4. By name only (table/global-scoped OOTB)
        var existingId = FindExistingAppAction(service, uniqueName, appModuleId)
            ?? FindExistingAppActionByUniqueName(service, uniqueName)
            ?? FindExistingAppActionByName(service, name, appModuleId)?.Id
            ?? (name != null ? FindExistingAppActionByNameOnly(service, name)?.Id : null);

        var entity = BuildEntityFromXml(root, appModuleId, service);

        if (existingId.HasValue)
        {
            entity.Id = existingId.Value;
            service.Update(entity);
            return existingId.Value;
        }

        // New record
        entity["uniquename"] = uniqueName;
        entity["origin"] = new OptionSetValue(ParseInt(root, "origin") ?? 0);

        var createRequest = new CreateRequest { Target = entity };
        if (!string.IsNullOrEmpty(solutionUniqueName))
            createRequest.Parameters["SolutionUniqueName"] = solutionUniqueName;

        return ((CreateResponse)service.Execute(createRequest)).id;
    }

    /// <summary>
    /// Read hideLegacyButtons element from appaction XML (MetadataSync extension, not sent to CRM).
    /// </summary>
    public static List<string>? ReadHideLegacyButtons(XDocument doc)
    {
        var element = doc.Root?.Element("hideLegacyButtons");
        if (element == null) return null;

        var buttons = element.Elements("button")
            .Select(b => b.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        return buttons.Count > 0 ? buttons : null;
    }

    private static Entity BuildEntityFromXml(XElement root, Guid appModuleId, IOrganizationService service)
    {
        var entity = new Entity("appaction");
        entity["appmoduleid"] = new EntityReference("appmodule", appModuleId);

        // Simple string fields
        SetStringIfPresent(entity, root, "name", "name");
        SetStringIfPresent(entity, root, "contextvalue", "contextvalue");
        SetStringIfPresent(entity, root, "onclickeventjavascriptfunctionname", "onclickeventjavascriptfunctionname");
        SetStringIfPresent(entity, root, "grouptitle", "grouptitle");
        SetStringIfPresent(entity, root, "buttontooltiptitle", "buttontooltiptitle");
        SetStringIfPresent(entity, root, "buttontooltipdescription", "buttontooltipdescription");
        SetStringIfPresent(entity, root, "buttonaccessibilitytext", "buttonaccessibilitytext");

        // buttonlabeltext — stored in "default" attribute
        var labelElement = root.Element("buttonlabeltext");
        if (labelElement != null)
        {
            var labelText = labelElement.Attribute("default")?.Value ?? labelElement.Value;
            if (!string.IsNullOrEmpty(labelText))
                entity["buttonlabeltext"] = labelText;
        }

        // OptionSetValue fields
        SetOptionSetIfPresent(entity, root, "context", "context");
        SetOptionSetIfPresent(entity, root, "location", "location");
        SetOptionSetIfPresent(entity, root, "onclickeventtype", "onclickeventtype");
        SetOptionSetIfPresent(entity, root, "type", "type");

        // Boolean fields (0/1 in XML)
        SetBoolIfPresent(entity, root, "hidden", "hidden");
        SetBoolIfPresent(entity, root, "isdisabled", "isdisabled");
        SetBoolIfPresent(entity, root, "isgrouptitlehidden", "isgrouptitlehidden");

        // Decimal
        var seqStr = root.Element("sequence")?.Value;
        if (!string.IsNullOrEmpty(seqStr) && decimal.TryParse(seqStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var seq))
            entity["sequence"] = seq;

        // Font icon — ensure $clientsvg: prefix
        var fontIcon = root.Element("fonticon")?.Value;
        if (!string.IsNullOrEmpty(fontIcon))
        {
            if (!fontIcon.StartsWith("$clientsvg:"))
                fontIcon = $"$clientsvg:{fontIcon}";
            entity["fonticon"] = fontIcon;
        }

        // Context entity lookup (resolve via entity metadata)
        var contextEntityLogical = root.Element("contextentity")?.Element("logicalname")?.Value;
        if (!string.IsNullOrEmpty(contextEntityLogical))
        {
            var entityMetadataId = GetEntityMetadataId(service, contextEntityLogical);
            entity["contextentity"] = new EntityReference("entity", entityMetadataId);
        }

        // Web resource lookup (resolve by name or GUID)
        var wrElement = root.Element("onclickeventjavascriptwebresourceid");
        if (wrElement != null)
        {
            var wrName = wrElement.Element("name")?.Value;
            var wrIdStr = wrElement.Element("webresourceid")?.Value;

            Guid? wrId = null;
            if (!string.IsNullOrEmpty(wrName))
            {
                wrId = ResolveWebResourceId(service, wrName);
            }
            else if (!string.IsNullOrEmpty(wrIdStr) && Guid.TryParse(wrIdStr, out var parsed))
            {
                wrId = parsed;
            }

            if (wrId.HasValue)
                entity["onclickeventjavascriptwebresourceid"] = new EntityReference("webresource", wrId.Value);
        }

        // JS parameters — resolve named tokens
        var jsParams = root.Element("onclickeventjavascriptparameters")?.Value;
        if (!string.IsNullOrEmpty(jsParams))
        {
            var resolved = ResolveParameterNames(jsParams);
            if (!string.IsNullOrEmpty(resolved))
                entity["onclickeventjavascriptparameters"] = resolved;
        }

        return entity;
    }

    private static Guid GetEntityMetadataId(IOrganizationService service, string entityLogicalName)
    {
        var request = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Entity
        };
        var response = (RetrieveEntityResponse)service.Execute(request);
        return response.EntityMetadata.MetadataId
            ?? throw new InvalidOperationException($"Entity '{entityLogicalName}' has no MetadataId.");
    }

    private static Guid? FindExistingAppAction(IOrganizationService service, string uniqueName, Guid appModuleId)
    {
        var query = new QueryExpression("appaction")
        {
            ColumnSet = new ColumnSet("appactionid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("uniquename", ConditionOperator.Equal, uniqueName),
                    new ConditionExpression("appmoduleid", ConditionOperator.Equal, appModuleId)
                }
            }
        };
        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.Id;
    }

    private static Guid? FindExistingAppActionByUniqueName(IOrganizationService service, string uniqueName)
    {
        var query = new QueryExpression("appaction")
        {
            ColumnSet = new ColumnSet("appactionid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("uniquename", ConditionOperator.Equal, uniqueName)
                }
            }
        };
        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.Id;
    }

    private static Entity? FindExistingAppActionByName(IOrganizationService service, string name, Guid appModuleId)
    {
        var query = new QueryExpression("appaction")
        {
            ColumnSet = new ColumnSet("appactionid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, name),
                    new ConditionExpression("appmoduleid", ConditionOperator.Equal, appModuleId)
                }
            }
        };
        return service.RetrieveMultiple(query).Entities.FirstOrDefault();
    }

    private static Entity? FindExistingAppActionByNameOnly(IOrganizationService service, string name)
    {
        var query = new QueryExpression("appaction")
        {
            ColumnSet = new ColumnSet("appactionid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, name)
                }
            }
        };
        return service.RetrieveMultiple(query).Entities.FirstOrDefault();
    }

    private static Guid ResolveWebResourceId(IOrganizationService service, string webResourceName)
    {
        var wrQuery = new QueryExpression("webresource")
        {
            ColumnSet = new ColumnSet("webresourceid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, webResourceName)
                }
            }
        };
        var wrResult = service.RetrieveMultiple(wrQuery).Entities.FirstOrDefault()
            ?? throw new InvalidOperationException($"Web resource '{webResourceName}' not found in CRM.");
        return wrResult.Id;
    }

    private static int? ParseInt(XElement root, string elementName)
    {
        var val = root.Element(elementName)?.Value;
        return !string.IsNullOrEmpty(val) && int.TryParse(val, out var i) ? i : null;
    }

    private static void SetStringIfPresent(Entity entity, XElement root, string xmlElement, string crmAttribute)
    {
        var val = root.Element(xmlElement)?.Value;
        if (!string.IsNullOrEmpty(val))
            entity[crmAttribute] = val;
    }

    private static void SetOptionSetIfPresent(Entity entity, XElement root, string xmlElement, string crmAttribute)
    {
        var val = ParseInt(root, xmlElement);
        if (val.HasValue)
            entity[crmAttribute] = new OptionSetValue(val.Value);
    }

    private static void SetBoolIfPresent(Entity entity, XElement root, string xmlElement, string crmAttribute)
    {
        var val = root.Element(xmlElement)?.Value;
        if (val != null)
            entity[crmAttribute] = val != "0";
    }
}
