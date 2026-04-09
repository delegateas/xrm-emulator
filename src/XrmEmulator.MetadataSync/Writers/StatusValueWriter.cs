using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class StatusValueWriter
{
    public static void Apply(
        IOrganizationService service,
        StatusValueDefinition def,
        Action<string>? log = null)
    {
        // Retrieve current statuscode metadata to check existing values
        var retrieveReq = new RetrieveAttributeRequest
        {
            EntityLogicalName = def.EntityLogicalName,
            LogicalName = "statuscode"
        };
        var retrieveResp = (RetrieveAttributeResponse)service.Execute(retrieveReq);
        var statusAttr = (StatusAttributeMetadata)retrieveResp.AttributeMetadata;

        var existingValues = statusAttr.OptionSet.Options
            .ToDictionary(o => o.Value ?? 0, o => o.Label?.UserLocalizedLabel?.Label ?? "");

        // Rename existing status codes
        if (def.RenameStatusCodes != null)
        {
            foreach (var (value, newLabel) in def.RenameStatusCodes)
            {
                if (!existingValues.ContainsKey(value))
                {
                    log?.Invoke($"  SKIP rename: status value {value} does not exist on {def.EntityLogicalName}");
                    continue;
                }

                if (string.Equals(existingValues[value], newLabel, StringComparison.Ordinal))
                {
                    log?.Invoke($"  SKIP rename: status value {value} already labeled '{newLabel}'");
                    continue;
                }

                var updateReq = new UpdateOptionValueRequest
                {
                    EntityLogicalName = def.EntityLogicalName,
                    AttributeLogicalName = "statuscode",
                    Value = value,
                    Label = new Label(newLabel, 1030), // Danish
                    MergeLabels = true
                };

                if (!string.IsNullOrEmpty(def.SolutionUniqueName))
                    updateReq.SolutionUniqueName = def.SolutionUniqueName;

                service.Execute(updateReq);
                log?.Invoke($"  Renamed status {value}: '{existingValues[value]}' → '{newLabel}'");
            }
        }

        // Add new status codes
        if (def.AddStatusCodes != null)
        {
            foreach (var entry in def.AddStatusCodes)
            {
                // Skip if value already exists
                if (entry.Value.HasValue && existingValues.ContainsKey(entry.Value.Value))
                {
                    log?.Invoke($"  SKIP add: status value {entry.Value} already exists");
                    continue;
                }

                // Skip if label already exists under the same state
                var labelExists = statusAttr.OptionSet.Options
                    .OfType<StatusOptionMetadata>()
                    .Any(o => o.State == entry.StateCode &&
                              string.Equals(o.Label?.UserLocalizedLabel?.Label, entry.Label, StringComparison.OrdinalIgnoreCase));

                if (labelExists)
                {
                    log?.Invoke($"  SKIP add: label '{entry.Label}' already exists under state {entry.StateCode}");
                    continue;
                }

                var insertReq = new InsertStatusValueRequest
                {
                    EntityLogicalName = def.EntityLogicalName,
                    AttributeLogicalName = "statuscode",
                    Label = new Label(entry.Label, 1030), // Danish
                    StateCode = entry.StateCode
                };

                if (entry.Value.HasValue)
                    insertReq.Value = entry.Value;

                if (!string.IsNullOrEmpty(entry.Description))
                    insertReq.Description = new Label(entry.Description, 1030);

                if (!string.IsNullOrEmpty(def.SolutionUniqueName))
                    insertReq.SolutionUniqueName = def.SolutionUniqueName;

                var response = (InsertStatusValueResponse)service.Execute(insertReq);
                log?.Invoke($"  Added status '{entry.Label}' = {response.NewOptionValue} under state {entry.StateCode}");
            }
        }

        // Publish entity changes
        service.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{def.EntityLogicalName}</entity></entities></importexportxml>"
        });
        log?.Invoke($"  Published {def.EntityLogicalName}");
    }
}
