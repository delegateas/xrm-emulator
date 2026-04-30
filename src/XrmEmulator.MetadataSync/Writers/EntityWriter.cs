using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class EntityWriter
{
    public static List<string> UpdateChangedAttributes(
        IOrganizationService service,
        string entityLogicalName,
        EntityDefinition pending,
        EntityDefinition snapshot)
    {
        var changes = new List<string>();
        var snapshotLookup = snapshot.Attributes
            .Where(a => a.IsCustomField)
            .ToDictionary(a => a.LogicalName, StringComparer.OrdinalIgnoreCase);

        // Dataverse rejects labels whose LCID isn't provisioned on the target org.
        // Resolve the org's base language once and tag every label we emit with it.
        var baseLcid = GetOrgBaseLcid(service);

        foreach (var attr in pending.Attributes.Where(a => a.IsCustomField))
        {
            if (!snapshotLookup.TryGetValue(attr.LogicalName, out var original))
                continue;

            if (!HasChanges(attr, original))
                continue;

            var metadata = BuildAttributeMetadata(attr, entityLogicalName, baseLcid);
            if (metadata == null) continue;

            var request = new UpdateAttributeRequest
            {
                EntityName = entityLogicalName,
                Attribute = metadata
            };

            service.Execute(request);
            changes.Add(attr.LogicalName);
        }

        return changes;
    }

    private static int GetOrgBaseLcid(IOrganizationService service)
    {
        var query = new QueryExpression("organization")
        {
            ColumnSet = new ColumnSet("languagecode"),
            TopCount = 1
        };
        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.GetAttributeValue<int?>("languagecode") ?? 1033;
    }

    private static bool HasChanges(AttributeDefinition pending, AttributeDefinition original)
    {
        return pending.DisplayName != original.DisplayName
            || pending.Description != original.Description
            || pending.RequiredLevel != original.RequiredLevel
            || pending.MaxLength != original.MaxLength
            || pending.MinValue != original.MinValue
            || pending.MaxValue != original.MaxValue
            || pending.Accuracy != original.Accuracy
            || pending.MinValueDecimal != original.MinValueDecimal
            || pending.MaxValueDecimal != original.MaxValueDecimal
            || pending.Format != original.Format
            || OptionsChanged(pending.Options, original.Options);
    }

    private static bool OptionsChanged(List<OptionDefinition>? pending, List<OptionDefinition>? original)
    {
        if (pending == null && original == null) return false;
        if (pending == null || original == null) return true;
        if (pending.Count != original.Count) return true;

        for (var i = 0; i < pending.Count; i++)
        {
            if (pending[i].Value != original[i].Value || pending[i].Label != original[i].Label)
                return true;
        }

        return false;
    }

    private static AttributeMetadata? BuildAttributeMetadata(AttributeDefinition attr, string entityLogicalName, int lcid)
    {
        var requiredLevel = ParseRequiredLevel(attr.RequiredLevel);

        return attr.Type.ToLowerInvariant() switch
        {
            "nvarchar" or "ntext" or "memo" => new StringAttributeMetadata
            {
                LogicalName = attr.LogicalName,
                MaxLength = attr.MaxLength,
                FormatName = ParseStringFormat(attr.Format),
                RequiredLevel = requiredLevel,
                DisplayName = CreateLabel(attr.DisplayName, lcid),
                Description = CreateLabel(attr.Description, lcid)
            },
            "bit" => new BooleanAttributeMetadata
            {
                LogicalName = attr.LogicalName,
                RequiredLevel = requiredLevel,
                DisplayName = CreateLabel(attr.DisplayName, lcid),
                Description = CreateLabel(attr.Description, lcid)
            },
            "int" or "integer" => new IntegerAttributeMetadata
            {
                LogicalName = attr.LogicalName,
                MinValue = attr.MinValue,
                MaxValue = attr.MaxValue,
                RequiredLevel = requiredLevel,
                DisplayName = CreateLabel(attr.DisplayName, lcid),
                Description = CreateLabel(attr.Description, lcid)
            },
            "decimal" => new DecimalAttributeMetadata
            {
                LogicalName = attr.LogicalName,
                MinValue = attr.MinValueDecimal,
                MaxValue = attr.MaxValueDecimal,
                Precision = attr.Accuracy,
                RequiredLevel = requiredLevel,
                DisplayName = CreateLabel(attr.DisplayName, lcid),
                Description = CreateLabel(attr.Description, lcid)
            },
            "picklist" or "state" or "status" => new PicklistAttributeMetadata
            {
                LogicalName = attr.LogicalName,
                RequiredLevel = requiredLevel,
                DisplayName = CreateLabel(attr.DisplayName, lcid),
                Description = CreateLabel(attr.Description, lcid)
            },
            "datetime" => new DateTimeAttributeMetadata
            {
                LogicalName = attr.LogicalName,
                Format = ParseDateTimeFormat(attr.Format),
                RequiredLevel = requiredLevel,
                DisplayName = CreateLabel(attr.DisplayName, lcid),
                Description = CreateLabel(attr.Description, lcid)
            },
            "lookup" or "customer" or "owner" => new LookupAttributeMetadata
            {
                LogicalName = attr.LogicalName,
                RequiredLevel = requiredLevel,
                DisplayName = CreateLabel(attr.DisplayName, lcid),
                Description = CreateLabel(attr.Description, lcid)
            },
            _ => null
        };
    }

    private static AttributeRequiredLevelManagedProperty ParseRequiredLevel(string? level)
    {
        var parsed = level?.ToLowerInvariant() switch
        {
            "required" or "systemrequired" => AttributeRequiredLevel.SystemRequired,
            "recommended" => AttributeRequiredLevel.Recommended,
            _ => AttributeRequiredLevel.None
        };
        return new AttributeRequiredLevelManagedProperty(parsed);
    }

    private static StringFormatName? ParseStringFormat(string? format)
    {
        if (string.IsNullOrEmpty(format)) return null;
        return format.ToLowerInvariant() switch
        {
            "email" => StringFormatName.Email,
            "url" => StringFormatName.Url,
            "phone" => StringFormatName.Phone,
            "text" => StringFormatName.Text,
            _ => null
        };
    }

    private static DateTimeFormat? ParseDateTimeFormat(string? format)
    {
        if (string.IsNullOrEmpty(format)) return null;
        return format.ToLowerInvariant() switch
        {
            "dateonly" => DateTimeFormat.DateOnly,
            "dateandtime" => DateTimeFormat.DateAndTime,
            _ => null
        };
    }

    private static Label CreateLabel(string? text, int lcid)
    {
        if (string.IsNullOrEmpty(text))
            return new Label();

        return new Label(text, lcid);
    }

    public static void PublishAll(IOrganizationService service)
    {
        service.Execute(new PublishAllXmlRequest());
    }
}
