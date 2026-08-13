using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class OptionSetWriter
{
    private static int GetOrgBaseLcid(IOrganizationService service)
    {
        var query = new QueryExpression("organization")
        {
            ColumnSet = new ColumnSet("languagecode"),
            TopCount = 1
        };
        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.GetAttributeValue<int?>("languagecode") ?? 1030;
    }

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

            var createLcid = GetOrgBaseLcid(service);

            var options = def.Values.Select(v =>
            {
                var option = new OptionMetadata(new Label(v.Label, createLcid), v.Value);
                // Carried on create too — the insert path below writes descriptions, so dropping
                // them here would make the same definition file produce different metadata
                // depending on whether the option set happened to exist already.
                if (!string.IsNullOrEmpty(v.Description))
                    option.Description = new Label(v.Description, createLcid);
                return option;
            }).ToArray();

            var newOptionSet = new OptionSetMetadata
            {
                Name = def.OptionSetName,
                // Falls back to the schema name only when no label was supplied — that shows users
                // "kf_something" in every picklist, so the definition file should always carry one.
                DisplayName = new Label(def.DisplayName ?? def.OptionSetName, createLcid),
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
            // Index existing options by numeric value so we can detect inserts vs label updates.
            var existingByValue = optionSet.Options
                .Where(o => o.Value.HasValue)
                .GroupBy(o => o.Value!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            // Resolve org base language once for label writes.
            var lcid = GetOrgBaseLcid(service);

            foreach (var entry in def.Values)
            {
                if (entry.Value.HasValue && existingByValue.TryGetValue(entry.Value.Value, out var existing))
                {
                    var existingLabelText = existing.Label?.UserLocalizedLabel?.Label
                        ?? existing.Label?.LocalizedLabels?.FirstOrDefault()?.Label;

                    if (string.Equals(existingLabelText, entry.Label, StringComparison.Ordinal))
                    {
                        log?.Invoke($"  SKIP: Value {entry.Value} with label '{entry.Label}' already exists in '{def.OptionSetName}'");
                        continue;
                    }

                    var updateRequest = new UpdateOptionValueRequest
                    {
                        OptionSetName = def.OptionSetName,
                        Value = entry.Value.Value,
                        Label = new Label(entry.Label, lcid),
                    };

                    if (!string.IsNullOrEmpty(entry.Description))
                        updateRequest.Description = new Label(entry.Description, lcid);

                    if (!string.IsNullOrEmpty(solutionUniqueName))
                        updateRequest.SolutionUniqueName = solutionUniqueName;

                    service.Execute(updateRequest);
                    log?.Invoke($"  Updated value {entry.Value} label '{existingLabelText}' → '{entry.Label}' in '{def.OptionSetName}'");
                    continue;
                }

                var request = new InsertOptionValueRequest
                {
                    OptionSetName = def.OptionSetName,
                    Label = new Label(entry.Label, lcid),
                    Value = entry.Value,
                };

                if (!string.IsNullOrEmpty(entry.Description))
                    request.Description = new Label(entry.Description, lcid);

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
