using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class CustomApiWriter
{
    /// <summary>
    /// Create or update a Custom API and its request parameters + response properties.
    /// All created records are added to the solution via the SolutionUniqueName parameter
    /// on CreateRequest, which is the reliable way to include custom components in a solution.
    /// Returns the Custom API record ID.
    /// </summary>
    public static Guid Upsert(
        IOrganizationService service,
        CustomApiDefinition def,
        Action<string>? log = null)
    {
        ValidateFieldLengths(def);

        // Resolve the plugin type ID from the assembly
        var pluginTypeId = FindPluginTypeByName(service, def.PluginTypeName)
            ?? throw new InvalidOperationException(
                $"Plugin type '{def.PluginTypeName}' not found in CRM. " +
                "Make sure the plugin assembly is committed first (plugin update + commit).");

        // Check if Custom API already exists
        var existingId = FindCustomApiByUniqueName(service, def.UniqueName);

        Guid customApiId;
        if (existingId.HasValue)
        {
            // Update existing
            var update = new Entity("customapi", existingId.Value);
            update["name"] = def.Name;
            update["displayname"] = string.IsNullOrEmpty(def.DisplayName) ? def.Name : def.DisplayName;
            update["description"] = def.Description;
            update["isfunction"] = def.IsFunction;
            update["bindingtype"] = new OptionSetValue(def.BindingType);
            update["boundentitylogicalname"] = def.BoundEntityLogicalName;
            update["allowedcustomprocessingsteptype"] = new OptionSetValue(def.AllowedCustomProcessingStepType);
            update["isprivate"] = def.IsPrivate;
            update["executeprivilegename"] = def.ExecutePrivilegeName;
            update["plugintypeid"] = new EntityReference("plugintype", pluginTypeId);
            service.Update(update);
            customApiId = existingId.Value;
            log?.Invoke($"  Custom API updated: {def.UniqueName} (ID: {customApiId})");
        }
        else
        {
            // Create new — use CreateRequest with SolutionUniqueName to add to solution
            var entity = new Entity("customapi");
            entity["uniquename"] = def.UniqueName;
            entity["name"] = def.Name;
            entity["displayname"] = string.IsNullOrEmpty(def.DisplayName) ? def.Name : def.DisplayName;
            entity["description"] = def.Description;
            entity["isfunction"] = def.IsFunction;
            entity["bindingtype"] = new OptionSetValue(def.BindingType);
            entity["boundentitylogicalname"] = def.BoundEntityLogicalName;
            entity["allowedcustomprocessingsteptype"] = new OptionSetValue(def.AllowedCustomProcessingStepType);
            entity["isprivate"] = def.IsPrivate;
            entity["executeprivilegename"] = def.ExecutePrivilegeName;
            entity["plugintypeid"] = new EntityReference("plugintype", pluginTypeId);

            customApiId = CreateInSolution(service, entity, def.SolutionUniqueName,
                $"Custom API '{def.UniqueName}'");

            log?.Invoke($"  Custom API created: {def.UniqueName} (ID: {customApiId})");
        }

        // Sync request parameters
        UpsertRequestParameters(service, def, customApiId, log);

        // Sync response properties
        UpsertResponseProperties(service, def, customApiId, log);

        return customApiId;
    }

    /// <summary>
    /// Validate field lengths against Dataverse limits to fail early with a clear error
    /// instead of a cryptic 400 response from the server.
    /// </summary>
    private static void ValidateFieldLengths(CustomApiDefinition def)
    {
        // Dataverse limits: name=256, displayname=256, description=100
        var errors = new List<string>();

        void CheckDescription(string label, string? description)
        {
            if (description != null && description.Length > 100)
                errors.Add($"{label}: description is {description.Length} chars (max 100)");
        }

        CheckDescription($"Custom API '{def.UniqueName}'", def.Description);
        foreach (var p in def.RequestParameters)
            CheckDescription($"Request parameter '{p.UniqueName}'", p.Description);
        foreach (var p in def.ResponseProperties)
            CheckDescription($"Response property '{p.UniqueName}'", p.Description);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Custom API '{def.UniqueName}' has field length violations:\n  " +
                string.Join("\n  ", errors));
    }

    /// <summary>
    /// Create a record and add it to the solution in a single operation.
    /// Uses CreateRequest with SolutionUniqueName parameter, which is the
    /// reliable way to ensure components appear in the target solution.
    /// </summary>
    private static Guid CreateInSolution(
        IOrganizationService service,
        Entity entity,
        string? solutionUniqueName,
        string displayLabel)
    {
        var request = new CreateRequest { Target = entity };

        if (!string.IsNullOrEmpty(solutionUniqueName))
        {
            request["SolutionUniqueName"] = solutionUniqueName;
        }

        try
        {
            var response = (CreateResponse)service.Execute(request);
            return response.id;
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create {displayLabel}: {ex.Detail.Message}", ex);
        }
    }

    private static void UpsertRequestParameters(
        IOrganizationService service,
        CustomApiDefinition def,
        Guid customApiId,
        Action<string>? log)
    {
        var existing = FindRequestParameters(service, customApiId);

        foreach (var param in def.RequestParameters)
        {
            var existingParam = existing.FirstOrDefault(e =>
                string.Equals(e.UniqueName, param.UniqueName, StringComparison.OrdinalIgnoreCase));

            if (existingParam.Id != Guid.Empty)
            {
                // Update
                var update = new Entity("customapirequestparameter", existingParam.Id);
                update["name"] = param.UniqueName;
                update["displayname"] = param.DisplayName ?? param.UniqueName;
                update["description"] = param.Description ?? param.UniqueName;
                update["type"] = new OptionSetValue(param.Type);
                update["isoptional"] = param.IsOptional;
                update["logicalentityname"] = param.LogicalEntityName;
                service.Update(update);
                log?.Invoke($"    Request parameter updated: {param.UniqueName}");
            }
            else
            {
                // Create — add to solution
                var entity = new Entity("customapirequestparameter");
                entity["customapiid"] = new EntityReference("customapi", customApiId);
                entity["uniquename"] = param.UniqueName;
                entity["name"] = param.UniqueName;
                entity["displayname"] = param.DisplayName ?? param.UniqueName;
                entity["description"] = param.Description ?? param.UniqueName;
                entity["type"] = new OptionSetValue(param.Type);
                entity["isoptional"] = param.IsOptional;
                entity["logicalentityname"] = param.LogicalEntityName;

                CreateInSolution(service, entity, def.SolutionUniqueName,
                    $"request parameter '{param.UniqueName}'");
                log?.Invoke($"    Request parameter created: {param.UniqueName}");
            }
        }
    }

    private static void UpsertResponseProperties(
        IOrganizationService service,
        CustomApiDefinition def,
        Guid customApiId,
        Action<string>? log)
    {
        var existing = FindResponseProperties(service, customApiId);

        foreach (var prop in def.ResponseProperties)
        {
            var existingProp = existing.FirstOrDefault(e =>
                string.Equals(e.UniqueName, prop.UniqueName, StringComparison.OrdinalIgnoreCase));

            if (existingProp.Id != Guid.Empty)
            {
                // Update
                var update = new Entity("customapiresponseproperty", existingProp.Id);
                update["name"] = prop.UniqueName;
                update["displayname"] = prop.DisplayName ?? prop.UniqueName;
                update["description"] = prop.Description ?? prop.UniqueName;
                update["type"] = new OptionSetValue(prop.Type);
                update["logicalentityname"] = prop.LogicalEntityName;
                service.Update(update);
                log?.Invoke($"    Response property updated: {prop.UniqueName}");
            }
            else
            {
                // Create — add to solution
                var entity = new Entity("customapiresponseproperty");
                entity["customapiid"] = new EntityReference("customapi", customApiId);
                entity["uniquename"] = prop.UniqueName;
                entity["name"] = prop.UniqueName;
                entity["displayname"] = prop.DisplayName ?? prop.UniqueName;
                entity["description"] = prop.Description ?? prop.UniqueName;
                entity["type"] = new OptionSetValue(prop.Type);
                entity["logicalentityname"] = prop.LogicalEntityName;

                CreateInSolution(service, entity, def.SolutionUniqueName,
                    $"response property '{prop.UniqueName}'");
                log?.Invoke($"    Response property created: {prop.UniqueName}");
            }
        }
    }

    #region Queries

    private static Guid? FindCustomApiByUniqueName(IOrganizationService service, string uniqueName)
    {
        var query = new QueryExpression("customapi")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("uniquename", ConditionOperator.Equal, uniqueName)
                }
            }
        };
        return service.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id;
    }

    private static Guid? FindPluginTypeByName(IOrganizationService service, string typeName)
    {
        var query = new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("typename", ConditionOperator.Equal, typeName)
                }
            }
        };
        return service.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id;
    }

    private static List<(Guid Id, string UniqueName)> FindRequestParameters(
        IOrganizationService service, Guid customApiId)
    {
        var query = new QueryExpression("customapirequestparameter")
        {
            ColumnSet = new ColumnSet("uniquename"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("customapiid", ConditionOperator.Equal, customApiId)
                }
            }
        };
        return service.RetrieveMultiple(query).Entities
            .Select(e => (e.Id, e.GetAttributeValue<string>("uniquename")))
            .ToList();
    }

    private static List<(Guid Id, string UniqueName)> FindResponseProperties(
        IOrganizationService service, Guid customApiId)
    {
        var query = new QueryExpression("customapiresponseproperty")
        {
            ColumnSet = new ColumnSet("uniquename"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("customapiid", ConditionOperator.Equal, customApiId)
                }
            }
        };
        return service.RetrieveMultiple(query).Entities
            .Select(e => (e.Id, e.GetAttributeValue<string>("uniquename")))
            .ToList();
    }

    #endregion
}
