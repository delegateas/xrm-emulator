using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class BusinessRuleWriter
{
    /// <summary>
    /// Checks if a business rule with the same name already exists for the entity.
    /// Returns the existing ID if found, null otherwise.
    /// </summary>
    public static Guid? FindExistingByName(IOrganizationService service, string name, string primaryEntity, int category = 2)
    {
        var query = new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, name),
                    new ConditionExpression("primaryentity", ConditionOperator.Equal, primaryEntity),
                    new ConditionExpression("category", ConditionOperator.Equal, category),
                }
            },
            TopCount = 1
        };
        var results = service.RetrieveMultiple(query);
        return results.Entities.Count > 0 ? results.Entities[0].Id : null;
    }

    public static Guid Create(IOrganizationService service, BusinessRuleDefinition rule, string? solutionUniqueName)
    {
        var entity = new Entity("workflow");
        entity["name"] = rule.Name;
        entity["primaryentity"] = rule.PrimaryEntity;
        entity["xaml"] = rule.Xaml;
        entity["type"] = new OptionSetValue(1);                     // 1 = Definition
        entity["category"] = new OptionSetValue(rule.Category);     // 2 = Business Rule, 4 = BPF
        entity["scope"] = new OptionSetValue(rule.Scope);

        if (rule.Description != null)
            entity["description"] = rule.Description;

        Guid id;
        try
        {
            // Create in solution context so the workflow gets the correct publisher prefix.
            // This is critical for BPFs — the backing entity name derives from the publisher prefix.
            var createRequest = new CreateRequest { Target = entity };
            if (!string.IsNullOrEmpty(solutionUniqueName))
                createRequest.Parameters["SolutionUniqueName"] = solutionUniqueName;

            var createResponse = (CreateResponse)service.Execute(createRequest);
            id = createResponse.id;
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create workflow '{rule.Name}' (category {rule.Category}) for entity '{rule.PrimaryEntity}': {ex.Detail.Message}", ex);
        }

        // Create processtrigger to scope to a specific form (business rules only)
        if (rule.ProcessTriggerFormId.HasValue && rule.Category == 2)
        {
            var trigger = new Entity("processtrigger");
            trigger["processid"] = new EntityReference("workflow", id);
            trigger["formid"] = new EntityReference("systemform", rule.ProcessTriggerFormId.Value);
            trigger["scope"] = new OptionSetValue(rule.ProcessTriggerScope ?? 1); // 1 = Form
            service.Create(trigger);
        }

        // Activate if requested (BPFs must be activated for Dynamics to create backing entity/stages)
        if (rule.ActivateOnCreate)
        {
            var activate = new SetStateRequest
            {
                EntityMoniker = new EntityReference("workflow", id),
                State = new OptionSetValue(1),   // Activated
                Status = new OptionSetValue(2),  // Activated
            };
            service.Execute(activate);

            // For BPFs: activation creates a backing entity that must also be added to the solution.
            // The backing entity name is stored in the workflow's "uniquename" field after activation.
            if (rule.Category == 4 && !string.IsNullOrEmpty(solutionUniqueName))
            {
                var workflow = service.Retrieve("workflow", id, new ColumnSet("uniquename"));
                var bpfEntityName = workflow.GetAttributeValue<string>("uniquename");
                if (!string.IsNullOrEmpty(bpfEntityName))
                {
                    // Resolve the entity's MetadataId
                    var entityQuery = new QueryExpression("entity")
                    {
                        ColumnSet = new ColumnSet("entityid"),
                        Criteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression("logicalname", ConditionOperator.Equal, bpfEntityName)
                            }
                        }
                    };
                    var entityResult = service.RetrieveMultiple(entityQuery).Entities.FirstOrDefault();
                    if (entityResult != null)
                    {
                        var addEntityRequest = new AddSolutionComponentRequest
                        {
                            ComponentId = entityResult.Id,
                            ComponentType = 1, // Entity
                            SolutionUniqueName = solutionUniqueName
                        };
                        service.Execute(addEntityRequest);
                    }
                }
            }
        }

        return id;
    }

    /// <summary>
    /// Activates an existing workflow (business rule or BPF). For BPFs (category 4), also
    /// adds the generated backing entity to the specified solution so it can be exported.
    /// Idempotent — already-activated workflows are a no-op.
    /// </summary>
    public static void ActivateExistingWorkflow(
        IOrganizationService service,
        Guid workflowId,
        string? solutionUniqueName,
        Action<string>? log = null)
    {
        var existing = service.Retrieve("workflow", workflowId, new ColumnSet("statecode", "category", "uniquename", "name"));
        var stateCode = existing.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
        var category = existing.GetAttributeValue<OptionSetValue>("category")?.Value ?? 0;
        var name = existing.GetAttributeValue<string>("name") ?? "<unknown>";

        if (stateCode != 1)
        {
            service.Execute(new SetStateRequest
            {
                EntityMoniker = new EntityReference("workflow", workflowId),
                State = new OptionSetValue(1),   // Activated
                Status = new OptionSetValue(2),  // Activated
            });
            log?.Invoke($"Activated workflow '{name}' ({workflowId})");
        }
        else
        {
            log?.Invoke($"Workflow '{name}' already activated — skip activation");
        }

        // For BPFs: ensure the backing entity is in the target solution.
        if (category == 4 && !string.IsNullOrEmpty(solutionUniqueName))
        {
            var refreshed = service.Retrieve("workflow", workflowId, new ColumnSet("uniquename"));
            var bpfEntityName = refreshed.GetAttributeValue<string>("uniquename");
            if (string.IsNullOrEmpty(bpfEntityName))
            {
                log?.Invoke("  Warning: BPF uniquename is blank after activation — backing entity not added to solution.");
                return;
            }

            var entityRow = service.RetrieveMultiple(new QueryExpression("entity")
            {
                ColumnSet = new ColumnSet("entityid"),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("logicalname", ConditionOperator.Equal, bpfEntityName) }
                },
                TopCount = 1
            }).Entities.FirstOrDefault();

            if (entityRow == null)
            {
                log?.Invoke($"  Warning: entity '{bpfEntityName}' not found in metadata.");
                return;
            }

            try
            {
                service.Execute(new AddSolutionComponentRequest
                {
                    ComponentId = entityRow.Id,
                    ComponentType = 1, // Entity
                    SolutionUniqueName = solutionUniqueName
                });
                log?.Invoke($"  Added backing entity '{bpfEntityName}' to solution '{solutionUniqueName}'");
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                // "Component already exists" is fine — idempotent.
                if (ex.Detail.ErrorCode == -2147220969 || ex.Detail.Message.Contains("already exists"))
                    log?.Invoke($"  Backing entity '{bpfEntityName}' already in solution — skip");
                else
                    throw;
            }
        }
    }

    /// <summary>
    /// Remove a workflow from a solution (does NOT delete the workflow itself).
    /// Useful to unblock solution export when a draft workflow is included in the solution.
    /// </summary>
    public static void RemoveWorkflowFromSolution(
        IOrganizationService service,
        Guid workflowId,
        string solutionUniqueName,
        Action<string>? log = null)
    {
        try
        {
            service.Execute(new Microsoft.Crm.Sdk.Messages.RemoveSolutionComponentRequest
            {
                ComponentId = workflowId,
                ComponentType = 29, // Workflow
                SolutionUniqueName = solutionUniqueName
            });
            log?.Invoke($"Removed workflow {workflowId} from solution '{solutionUniqueName}'");
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            // "Component doesn't exist in solution" is fine — idempotent.
            if (ex.Detail.Message.Contains("does not exist") || ex.Detail.Message.Contains("not found"))
                log?.Invoke($"Workflow {workflowId} was not in solution '{solutionUniqueName}' — skip");
            else
                throw;
        }
    }

    /// <summary>Find workflow by name across any primaryentity/category.</summary>
    public static Guid? FindWorkflowByName(IOrganizationService service, string name)
    {
        var results = service.RetrieveMultiple(new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, name),
                    new ConditionExpression("type", ConditionOperator.Equal, 1), // Definition
                }
            },
            TopCount = 2
        });
        if (results.Entities.Count == 0) return null;
        if (results.Entities.Count > 1)
            throw new InvalidOperationException(
                $"Multiple workflows named '{name}' found. Disambiguate by workflowid.");
        return results.Entities[0].Id;
    }

    public static void Update(IOrganizationService service, BusinessRuleDefinition rule)
    {
        // Check if the workflow is currently activated (statecode=1).
        // Published workflows must be deactivated before updating.
        var existing = service.Retrieve("workflow", rule.WorkflowId, new ColumnSet("statecode"));
        var stateCode = existing.GetAttributeValue<OptionSetValue>("statecode")?.Value;
        var wasActivated = stateCode == 1;

        if (wasActivated)
        {
            var deactivate = new SetStateRequest
            {
                EntityMoniker = new EntityReference("workflow", rule.WorkflowId),
                State = new OptionSetValue(0),   // Draft
                Status = new OptionSetValue(1),  // Draft
            };
            service.Execute(deactivate);
        }

        var entity = new Entity("workflow", rule.WorkflowId);
        entity["name"] = rule.Name;
        entity["xaml"] = rule.Xaml;

        if (rule.Description != null)
            entity["description"] = rule.Description;

        service.Update(entity);

        if (wasActivated)
        {
            var activate = new SetStateRequest
            {
                EntityMoniker = new EntityReference("workflow", rule.WorkflowId),
                State = new OptionSetValue(1),   // Activated
                Status = new OptionSetValue(2),  // Activated
            };
            service.Execute(activate);
        }
    }
}
