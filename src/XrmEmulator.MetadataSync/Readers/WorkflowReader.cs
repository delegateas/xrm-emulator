using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Readers;

public static class WorkflowReader
{
    public static List<Entity> Read(IOrganizationService service, HashSet<string>? entityNames = null)
    {
        var query = new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet(true),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    // category = 0 (Workflow)
                    new ConditionExpression("category", ConditionOperator.Equal, 0),
                    // statecode = 1 (Activated)
                    new ConditionExpression("statecode", ConditionOperator.Equal, 1),
                    // Only parent workflows (not child/sub-workflows)
                    new ConditionExpression("parentworkflowid", ConditionOperator.Null)
                }
            }
        };

        var results = service.RetrieveMultiple(query);
        var workflows = results.Entities.ToList();

        // Filter by entity names if provided
        if (entityNames is { Count: > 0 })
        {
            workflows = workflows
                .Where(w =>
                {
                    var primaryEntity = w.GetAttributeValue<string>("primaryentity");
                    return string.IsNullOrEmpty(primaryEntity)
                        || primaryEntity == "none"
                        || entityNames.Contains(primaryEntity);
                })
                .ToList();
        }

        return workflows;
    }
}
