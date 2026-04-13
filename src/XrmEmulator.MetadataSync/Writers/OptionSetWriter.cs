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
    /// Creates the option set if it doesn't exist yet.
    /// </summary>
    public static void AddValues(
        IOrganizationService service,
        OptionSetValueDefinition def,
        string? solutionUniqueName,
        Action<string>? log = null)
    {
        log?.Invoke($"Adding {def.Values.Count} value(s) to global option set '{def.OptionSetName}'");

        // Try to retrieve existing option set
        OptionSetMetadata? optionSet = null;
        try
        {
            var retrieveRequest = new RetrieveOptionSetRequest { Name = def.OptionSetName };
            var retrieveResponse = (RetrieveOptionSetResponse)service.Execute(retrieveRequest);
            optionSet = retrieveResponse.OptionSetMetadata as OptionSetMetadata;
        }
        catch
        {
            // Option set doesn't exist — create it
        }

        if (optionSet == null)
        {
            log?.Invoke($"  Option set '{def.OptionSetName}' not found — creating...");

            var options = def.Values.Select(v => new OptionMetadata(
                new Label(v.Label, 1030), v.Value)).ToArray();

            var newOptionSet = new OptionSetMetadata
            {
                Name = def.OptionSetName,
                DisplayName = new Label(def.OptionSetName, 1030),
                IsGlobal = true,
                OptionSetType = OptionSetType.Picklist,
            };
            foreach (var opt in options)
                newOptionSet.Options.Add(opt);

            var createRequest = new CreateOptionSetRequest { OptionSet = newOptionSet };
            if (!string.IsNullOrEmpty(solutionUniqueName))
                createRequest.SolutionUniqueName = solutionUniqueName;

            service.Execute(createRequest);
            log?.Invoke($"  Created option set '{def.OptionSetName}' with {options.Length} value(s).");
        }
        else
        {
            // Add values to existing option set
            var existingValues = optionSet.Options
                .Select(o => o.Value ?? 0)
                .ToHashSet();

            foreach (var entry in def.Values)
            {
                if (entry.Value.HasValue && existingValues.Contains(entry.Value.Value))
                {
                    log?.Invoke($"  SKIP: Value {entry.Value} already exists in '{def.OptionSetName}'");
                    continue;
                }

                var request = new InsertOptionValueRequest
                {
                    OptionSetName = def.OptionSetName,
                    Label = new Label(entry.Label, 1030),
                    Value = entry.Value,
                };

                if (!string.IsNullOrEmpty(entry.Description))
                    request.Description = new Label(entry.Description, 1030);

                if (!string.IsNullOrEmpty(solutionUniqueName))
                    request.SolutionUniqueName = solutionUniqueName;

                var response = (InsertOptionValueResponse)service.Execute(request);
                log?.Invoke($"  Added '{entry.Label}' = {response.NewOptionValue} to '{def.OptionSetName}'");
            }
        }

        service.Execute(new PublishAllXmlRequest());
        log?.Invoke($"  Published option set '{def.OptionSetName}'");
    }
}
