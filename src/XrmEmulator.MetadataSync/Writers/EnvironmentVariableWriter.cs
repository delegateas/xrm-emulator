using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class EnvironmentVariableWriter
{
    // Dataverse option set values for environmentvariabledefinition.type
    private static readonly Dictionary<string, int> TypeValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["String"]     = 100000000,
        ["Number"]     = 100000001,
        ["Boolean"]    = 100000002,
        ["JSON"]       = 100000003,
        ["DataSource"] = 100000004,
        ["Secret"]     = 100000005,
    };

    // Component type 380 = EnvironmentVariableDefinition
    private const int EnvironmentVariableDefinitionComponentType = 380;

    /// <summary>
    /// Upsert all environment variable definitions (and their current values) in the file.
    /// For each variable: find by schemaname → update if found, create if not.
    /// After creating a new definition, add it to the specified solution.
    /// Then upsert the current value record if CurrentValue is provided.
    /// </summary>
    public static void Upsert(
        IOrganizationService service,
        EnvironmentVariableFileDefinition fileDef,
        Action<string>? log = null)
    {
        foreach (var variable in fileDef.Variables)
            UpsertSingle(service, fileDef.SolutionUniqueName, variable, log);
    }

    /// <summary>Upsert a single environment variable definition and its current value.</summary>
    public static void UpsertSingle(
        IOrganizationService service,
        string solutionUniqueName,
        EnvironmentVariableEntry variable,
        Action<string>? log = null)
    {
        log?.Invoke($"  Processing: {variable.SchemaName}");

        var defId = UpsertDefinition(service, variable, solutionUniqueName, log);

        if (variable.CurrentValue != null)
        {
            UpsertValue(service, defId, variable.CurrentValue, log);
        }
        else
        {
            log?.Invoke($"    No currentValue specified — skipping value record.");
        }
    }

    private static Guid UpsertDefinition(
        IOrganizationService service,
        EnvironmentVariableEntry variable,
        string solutionUniqueName,
        Action<string>? log)
    {
        if (!TypeValues.TryGetValue(variable.Type, out var typeCode))
        {
            throw new InvalidOperationException(
                $"Unknown environment variable type '{variable.Type}'. " +
                $"Valid types: {string.Join(", ", TypeValues.Keys)}");
        }

        var existing = FindDefinitionBySchemaName(service, variable.SchemaName);

        if (existing.HasValue)
        {
            log?.Invoke($"    Found existing definition {existing.Value} — updating.");
            var update = new Entity("environmentvariabledefinition", existing.Value)
            {
                ["displayname"] = variable.DisplayName,
                ["type"]        = new OptionSetValue(typeCode),
            };
            if (variable.DefaultValue != null)
                update["defaultvalue"] = variable.DefaultValue;

            try
            {
                service.Update(update);
                log?.Invoke($"    Definition updated OK.");
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                throw new InvalidOperationException(
                    $"Failed to update environment variable definition '{variable.SchemaName}': {ex.Detail.Message}", ex);
            }

            return existing.Value;
        }
        else
        {
            log?.Invoke($"    Definition not found — creating.");
            var create = new Entity("environmentvariabledefinition")
            {
                ["schemaname"]   = variable.SchemaName,
                ["displayname"]  = variable.DisplayName,
                ["type"]         = new OptionSetValue(typeCode),
                ["defaultvalue"] = variable.DefaultValue ?? string.Empty,
            };

            Guid newId;
            try
            {
                newId = service.Create(create);
                log?.Invoke($"    Definition created. ID: {newId}");
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                throw new InvalidOperationException(
                    $"Failed to create environment variable definition '{variable.SchemaName}': {ex.Detail.Message}", ex);
            }

            // Add to solution
            AddDefinitionToSolution(service, newId, solutionUniqueName, variable.SchemaName, log);

            return newId;
        }
    }

    private static void UpsertValue(
        IOrganizationService service,
        Guid definitionId,
        string value,
        Action<string>? log)
    {
        var existingValueId = FindValueByDefinitionId(service, definitionId);

        if (existingValueId.HasValue)
        {
            log?.Invoke($"    Found existing value record {existingValueId.Value} — updating.");
            var update = new Entity("environmentvariablevalue", existingValueId.Value)
            {
                ["value"] = value
            };
            try
            {
                service.Update(update);
                log?.Invoke($"    Value updated OK.");
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                throw new InvalidOperationException(
                    $"Failed to update environment variable value for definition {definitionId}: {ex.Detail.Message}", ex);
            }
        }
        else
        {
            log?.Invoke($"    No existing value record — creating.");
            var create = new Entity("environmentvariablevalue")
            {
                ["environmentvariabledefinitionid"] = new EntityReference("environmentvariabledefinition", definitionId),
                ["value"] = value
            };
            try
            {
                var newValueId = service.Create(create);
                log?.Invoke($"    Value created. ID: {newValueId}");
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                throw new InvalidOperationException(
                    $"Failed to create environment variable value for definition {definitionId}: {ex.Detail.Message}", ex);
            }
        }
    }

    private static void AddDefinitionToSolution(
        IOrganizationService service,
        Guid definitionId,
        string solutionUniqueName,
        string schemaName,
        Action<string>? log)
    {
        var request = new AddSolutionComponentRequest
        {
            ComponentId         = definitionId,
            ComponentType       = EnvironmentVariableDefinitionComponentType,
            SolutionUniqueName  = solutionUniqueName,
            AddRequiredComponents = false,
        };

        try
        {
            service.Execute(request);
            log?.Invoke($"    Added to solution '{solutionUniqueName}'.");
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to add environment variable '{schemaName}' to solution '{solutionUniqueName}': {ex.Detail.Message}", ex);
        }
    }

    private static Guid? FindDefinitionBySchemaName(IOrganizationService service, string schemaName)
    {
        var query = new QueryExpression("environmentvariabledefinition")
        {
            ColumnSet = new ColumnSet("environmentvariabledefinitionid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName)
                }
            },
            TopCount = 1
        };
        return service.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id;
    }

    private static Guid? FindValueByDefinitionId(IOrganizationService service, Guid definitionId)
    {
        var query = new QueryExpression("environmentvariablevalue")
        {
            ColumnSet = new ColumnSet("environmentvariablevalueid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("environmentvariabledefinitionid", ConditionOperator.Equal, definitionId)
                }
            },
            TopCount = 1
        };
        return service.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id;
    }
}
