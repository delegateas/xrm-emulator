using System.Reflection;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class PluginWriter
{
    /// <summary>
    /// Scans the DLL for IPlugin types and compares against the plugin.json definition.
    /// Returns warnings for types in the DLL that are not in the JSON (missing registration)
    /// and types in the JSON that are not in the DLL (stale registration).
    /// </summary>
    public static List<string> ValidateTypesAgainstDll(
        PluginRegistrationDefinition def, string baseDir)
    {
        var warnings = new List<string>();

        var dllPath = Path.IsPathRooted(def.AssemblyPath)
            ? def.AssemblyPath
            : Path.Combine(baseDir, def.AssemblyPath);

        if (!File.Exists(dllPath))
        {
            warnings.Add($"DLL not found: {dllPath}");
            return warnings;
        }

        var definedTypeNames = new HashSet<string>(
            def.Types.Select(t => t.TypeName), StringComparer.OrdinalIgnoreCase);

        // Reflect the DLL to find all IPlugin implementations
        HashSet<string> dllTypeNames;
        try
        {
            // Load into reflection-only context to avoid locking
            var assembly = Assembly.LoadFrom(dllPath);
            dllTypeNames = new HashSet<string>(
                assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract &&
                                t.GetInterface("Microsoft.Xrm.Sdk.IPlugin") != null)
                    .Select(t => t.FullName!),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types may fail to load — use what we can
            dllTypeNames = new HashSet<string>(
                ex.Types.Where(t => t != null &&
                                    t.IsClass && !t.IsAbstract &&
                                    t.GetInterface("Microsoft.Xrm.Sdk.IPlugin") != null)
                    .Select(t => t!.FullName!),
                StringComparer.OrdinalIgnoreCase);
        }

        // Types in DLL but not in JSON — probably forgot to register
        foreach (var dllType in dllTypeNames)
        {
            if (!definedTypeNames.Contains(dllType))
                warnings.Add($"IPlugin type '{dllType}' exists in DLL but is not in plugin.json — missing registration?");
        }

        // Types in JSON but not in DLL — stale definition
        foreach (var jsonType in definedTypeNames)
        {
            if (!dllTypeNames.Contains(jsonType))
                warnings.Add($"Type '{jsonType}' is in plugin.json but not found in DLL — stale definition?");
        }

        return warnings;
    }

    /// <summary>
    /// Register or update a plugin assembly. Returns the assembly ID.
    /// </summary>
    public static Guid RegisterAssembly(
        IOrganizationService service,
        PluginRegistrationDefinition def,
        string baseDir,
        string solutionUniqueName)
    {
        var dllPath = Path.IsPathRooted(def.AssemblyPath)
            ? def.AssemblyPath
            : Path.Combine(baseDir, def.AssemblyPath);

        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Plugin assembly DLL not found: {dllPath}");

        var content = Convert.ToBase64String(File.ReadAllBytes(dllPath));

        // Check if assembly already exists
        var existingId = FindAssemblyByName(service, def.AssemblyName);

        if (existingId.HasValue)
        {
            // Update existing assembly (re-upload DLL)
            var update = new Entity("pluginassembly", existingId.Value);
            update["content"] = content;
            service.Update(update);
            return existingId.Value;
        }

        // Create new assembly
        var entity = new Entity("pluginassembly");
        entity["name"] = def.AssemblyName;
        entity["content"] = content;
        entity["isolationmode"] = new OptionSetValue(def.IsolationMode);
        entity["sourcetype"] = new OptionSetValue(def.SourceType);

        Guid id;
        try
        {
            id = service.Create(entity);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create plugin assembly '{def.AssemblyName}': {ex.Detail.Message}", ex);
        }

        if (!string.IsNullOrEmpty(solutionUniqueName))
        {
            var addRequest = new AddSolutionComponentRequest
            {
                ComponentId = id,
                ComponentType = 91, // PluginAssembly
                SolutionUniqueName = solutionUniqueName
            };
            service.Execute(addRequest);
        }

        return id;
    }

    /// <summary>
    /// Register or update a plugin type. Returns the type ID.
    /// </summary>
    public static Guid RegisterType(
        IOrganizationService service,
        PluginTypeRegistration typeDef,
        Guid assemblyId)
    {
        // Check if type already exists
        var existingId = FindTypeByName(service, typeDef.TypeName, assemblyId);

        if (existingId.HasValue)
        {
            var update = new Entity("plugintype", existingId.Value);
            update["friendlyname"] = typeDef.FriendlyName;
            update["name"] = typeDef.TypeName;
            service.Update(update);
            return existingId.Value;
        }

        var entity = new Entity("plugintype");
        entity["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId);
        entity["typename"] = typeDef.TypeName;
        entity["friendlyname"] = typeDef.FriendlyName;
        entity["name"] = typeDef.TypeName;

        try
        {
            return service.Create(entity);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create plugin type '{typeDef.TypeName}': {ex.Detail.Message}", ex);
        }
    }

    /// <summary>
    /// Register or update a plugin step. Returns the step ID.
    /// </summary>
    public static Guid RegisterStep(
        IOrganizationService service,
        PluginStepRegistration stepDef,
        Guid pluginTypeId,
        string solutionUniqueName)
    {
        var messageId = ResolveSdkMessageId(service, stepDef.MessageName);
        var filterId = ResolveSdkMessageFilterId(service, messageId, stepDef.PrimaryEntity);

        var stepName = $"{stepDef.MessageName} of {stepDef.PrimaryEntity}";

        // Check if step already exists for this type + message + entity
        var existingId = FindStepByTypeAndMessage(service, pluginTypeId, messageId, filterId);

        if (existingId.HasValue)
        {
            var update = new Entity("sdkmessageprocessingstep", existingId.Value);
            update["mode"] = new OptionSetValue(stepDef.Mode);
            update["rank"] = stepDef.Rank;
            update["stage"] = new OptionSetValue(stepDef.Stage);
            update["filteringattributes"] = stepDef.FilteringAttributes;
            update["asyncautodelete"] = stepDef.AsyncAutoDelete;
            service.Update(update);
            return existingId.Value;
        }

        var entity = new Entity("sdkmessageprocessingstep");
        entity["name"] = stepName;
        entity["sdkmessageid"] = new EntityReference("sdkmessage", messageId);
        entity["eventhandler"] = new EntityReference("plugintype", pluginTypeId);
        entity["stage"] = new OptionSetValue(stepDef.Stage);
        entity["mode"] = new OptionSetValue(stepDef.Mode);
        entity["rank"] = stepDef.Rank;
        entity["supporteddeployment"] = new OptionSetValue(0); // ServerOnly
        entity["asyncautodelete"] = stepDef.AsyncAutoDelete;

        if (filterId != Guid.Empty)
            entity["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterId);

        if (!string.IsNullOrEmpty(stepDef.FilteringAttributes))
            entity["filteringattributes"] = stepDef.FilteringAttributes;

        Guid id;
        try
        {
            id = service.Create(entity);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create plugin step '{stepName}': {ex.Detail.Message}", ex);
        }

        if (!string.IsNullOrEmpty(solutionUniqueName))
        {
            var addRequest = new AddSolutionComponentRequest
            {
                ComponentId = id,
                ComponentType = 92, // SdkMessageProcessingStep
                SolutionUniqueName = solutionUniqueName
            };
            service.Execute(addRequest);
        }

        return id;
    }

    /// <summary>
    /// Register a step image. Returns the image ID.
    /// </summary>
    public static Guid RegisterImage(
        IOrganizationService service,
        PluginImageRegistration imageDef,
        Guid stepId)
    {
        var entity = new Entity("sdkmessageprocessingstepimage");
        entity["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId);
        entity["imagetype"] = new OptionSetValue(imageDef.ImageType);
        entity["name"] = imageDef.Name;
        entity["entityalias"] = imageDef.EntityAlias;
        entity["messagepropertyname"] = "Target";

        if (!string.IsNullOrEmpty(imageDef.Attributes))
            entity["attributes"] = imageDef.Attributes;

        try
        {
            return service.Create(entity);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create plugin image '{imageDef.Name}': {ex.Detail.Message}", ex);
        }
    }

    /// <summary>
    /// Remove plugin steps and types that exist in CRM but are no longer in the definition.
    /// Must be called BEFORE uploading the new assembly DLL, so CRM doesn't reject the
    /// update due to missing types referenced by orphaned steps.
    /// </summary>
    public static void RemoveOrphanedStepsAndTypes(
        IOrganizationService service,
        PluginRegistrationDefinition def,
        Guid assemblyId,
        Action<string>? log = null)
    {
        var definedTypeNames = new HashSet<string>(
            def.Types.Select(t => t.TypeName), StringComparer.OrdinalIgnoreCase);

        // Find all types registered in CRM for this assembly
        var existingTypes = FindAllTypesByAssembly(service, assemblyId);

        foreach (var (typeId, typeName) in existingTypes)
        {
            if (definedTypeNames.Contains(typeName))
            {
                // Type still exists in JSON — check for orphaned steps
                var typeDef = def.Types.First(t =>
                    string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

                var existingSteps = FindAllStepsByType(service, typeId);
                foreach (var (stepId, stepMessageId, stepFilterId) in existingSteps)
                {
                    var stillDefined = typeDef.Steps.Any(s =>
                    {
                        var msgId = ResolveSdkMessageId(service, s.MessageName);
                        var filtId = ResolveSdkMessageFilterId(service, msgId, s.PrimaryEntity);
                        return msgId == stepMessageId &&
                               (filtId == stepFilterId || (filtId == Guid.Empty && stepFilterId == Guid.Empty));
                    });

                    if (!stillDefined)
                    {
                        // Delete images first, then the step
                        DeleteStepImages(service, stepId);
                        service.Delete("sdkmessageprocessingstep", stepId);
                        log?.Invoke($"  Removed orphaned step {stepId} from type {typeName}");
                    }
                }
            }
            else
            {
                // Type no longer in JSON — delete all its steps, then the type
                var existingSteps = FindAllStepsByType(service, typeId);
                foreach (var (stepId, _, _) in existingSteps)
                {
                    DeleteStepImages(service, stepId);
                    service.Delete("sdkmessageprocessingstep", stepId);
                    log?.Invoke($"  Removed orphaned step {stepId} from removed type {typeName}");
                }

                service.Delete("plugintype", typeId);
                log?.Invoke($"  Removed orphaned type: {typeName}");
            }
        }
    }

    private static List<(Guid Id, string TypeName)> FindAllTypesByAssembly(
        IOrganizationService service, Guid assemblyId)
    {
        var query = new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet("typename"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("pluginassemblyid", ConditionOperator.Equal, assemblyId)
                }
            }
        };
        return service.RetrieveMultiple(query).Entities
            .Select(e => (e.Id, e.GetAttributeValue<string>("typename")))
            .ToList();
    }

    private static List<(Guid Id, Guid MessageId, Guid FilterId)> FindAllStepsByType(
        IOrganizationService service, Guid pluginTypeId)
    {
        var query = new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet("sdkmessageid", "sdkmessagefilterid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("eventhandler", ConditionOperator.Equal, pluginTypeId)
                }
            }
        };
        return service.RetrieveMultiple(query).Entities
            .Select(e => (
                e.Id,
                e.GetAttributeValue<EntityReference>("sdkmessageid")?.Id ?? Guid.Empty,
                e.GetAttributeValue<EntityReference>("sdkmessagefilterid")?.Id ?? Guid.Empty))
            .ToList();
    }

    private static void DeleteStepImages(IOrganizationService service, Guid stepId)
    {
        var query = new QueryExpression("sdkmessageprocessingstepimage")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId)
                }
            }
        };
        foreach (var image in service.RetrieveMultiple(query).Entities)
        {
            service.Delete("sdkmessageprocessingstepimage", image.Id);
        }
    }

    public static Guid? FindExistingAssemblyId(IOrganizationService service, string assemblyName)
    {
        return FindAssemblyByName(service, assemblyName);
    }

    private static Guid? FindAssemblyByName(IOrganizationService service, string assemblyName)
    {
        var query = new QueryExpression("pluginassembly")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, assemblyName)
                }
            }
        };
        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.Id;
    }

    private static Guid? FindTypeByName(IOrganizationService service, string typeName, Guid assemblyId)
    {
        var query = new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("typename", ConditionOperator.Equal, typeName),
                    new ConditionExpression("pluginassemblyid", ConditionOperator.Equal, assemblyId)
                }
            }
        };
        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.Id;
    }

    private static Guid? FindStepByTypeAndMessage(
        IOrganizationService service, Guid pluginTypeId, Guid messageId, Guid filterId)
    {
        var query = new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("eventhandler", ConditionOperator.Equal, pluginTypeId),
                    new ConditionExpression("sdkmessageid", ConditionOperator.Equal, messageId)
                }
            }
        };

        if (filterId != Guid.Empty)
            query.Criteria.Conditions.Add(
                new ConditionExpression("sdkmessagefilterid", ConditionOperator.Equal, filterId));

        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.Id;
    }

    private static Guid ResolveSdkMessageId(IOrganizationService service, string messageName)
    {
        var query = new QueryExpression("sdkmessage")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, messageName)
                }
            }
        };
        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault()
            ?? throw new InvalidOperationException($"SDK message '{messageName}' not found in CRM.");
        return result.Id;
    }

    private static Guid ResolveSdkMessageFilterId(
        IOrganizationService service, Guid messageId, string entityLogicalName)
    {
        var query = new QueryExpression("sdkmessagefilter")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sdkmessageid", ConditionOperator.Equal, messageId),
                    new ConditionExpression("primaryobjecttypecode", ConditionOperator.Equal, entityLogicalName)
                }
            }
        };
        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.Id ?? Guid.Empty;
    }
}
