using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class BusinessRuleWriter
{
    /// <summary>
    /// Checks if a business rule with the same name already exists for the entity.
    /// Returns the existing ID if found, null otherwise.
    /// </summary>
    public static Guid? FindExistingByName(IOrganizationService service, string name, string primaryEntity)
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
                    new ConditionExpression("category", ConditionOperator.Equal, 2), // Business Rule
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
        entity["type"] = new OptionSetValue(1);       // 1 = Definition
        entity["category"] = new OptionSetValue(2);    // 2 = Business Rule
        entity["scope"] = new OptionSetValue(rule.Scope);

        if (rule.Description != null)
            entity["description"] = rule.Description;

        Guid id;
        try
        {
            id = service.Create(entity);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create business rule '{rule.Name}' for entity '{rule.PrimaryEntity}': {ex.Detail.Message}", ex);
        }

        // Add to solution separately
        if (!string.IsNullOrEmpty(solutionUniqueName))
        {
            var addRequest = new AddSolutionComponentRequest
            {
                ComponentId = id,
                ComponentType = 29, // Workflow
                SolutionUniqueName = solutionUniqueName
            };
            service.Execute(addRequest);
        }

        // Create processtrigger to scope to a specific form
        if (rule.ProcessTriggerFormId.HasValue)
        {
            var trigger = new Entity("processtrigger");
            trigger["processid"] = new EntityReference("workflow", id);
            trigger["formid"] = new EntityReference("systemform", rule.ProcessTriggerFormId.Value);
            trigger["scope"] = new OptionSetValue(rule.ProcessTriggerScope ?? 1); // 1 = Form
            service.Create(trigger);
        }

        return id;
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
