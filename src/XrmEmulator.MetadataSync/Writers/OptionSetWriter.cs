using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class OptionSetWriter
{
    /// <summary>
    /// Add new values to a global option set in CRM.
    /// </summary>
    public static void AddValues(
        IOrganizationService service,
        OptionSetValueDefinition def,
        string? solutionUniqueName,
        Action<string>? log = null)
    {
        log?.Invoke($"Adding {def.Values.Count} value(s) to global option set '{def.OptionSetName}'");

        // Retrieve current option set to check for duplicates
        var retrieveRequest = new RetrieveOptionSetRequest { Name = def.OptionSetName };
        var retrieveResponse = (RetrieveOptionSetResponse)service.Execute(retrieveRequest);

        if (retrieveResponse.OptionSetMetadata is not OptionSetMetadata optionSet)
            throw new InvalidOperationException($"Global option set '{def.OptionSetName}' not found.");

        var existingValues = optionSet.Options
            .Select(o => o.Value ?? 0)
            .ToHashSet();

        var existingLabels = optionSet.Options
            .Select(o => o.Label?.UserLocalizedLabel?.Label ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in def.Values)
        {
            // Skip if value already exists
            if (entry.Value.HasValue && existingValues.Contains(entry.Value.Value))
            {
                log?.Invoke($"  SKIP: Value {entry.Value} already exists in '{def.OptionSetName}'");
                continue;
            }

            // Warn if label already exists
            if (existingLabels.Contains(entry.Label))
            {
                log?.Invoke($"  WARNING: Label '{entry.Label}' already exists in '{def.OptionSetName}' — adding anyway");
            }

            var request = new InsertOptionValueRequest
            {
                OptionSetName = def.OptionSetName,
                Label = new Label(entry.Label, 1030), // Danish
                Value = entry.Value,
            };

            if (!string.IsNullOrEmpty(entry.Description))
                request.Description = new Label(entry.Description, 1030);

            if (!string.IsNullOrEmpty(solutionUniqueName))
                request.SolutionUniqueName = solutionUniqueName;

            var response = (InsertOptionValueResponse)service.Execute(request);
            var assignedValue = response.NewOptionValue;

            log?.Invoke($"  Added '{entry.Label}' = {assignedValue} to '{def.OptionSetName}'");
        }

        // Publish the option set changes
        service.Execute(new PublishAllXmlRequest());
        log?.Invoke($"  Published option set '{def.OptionSetName}'");
    }
}
