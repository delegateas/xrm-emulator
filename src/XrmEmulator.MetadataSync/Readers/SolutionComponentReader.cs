using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Readers;

public static class SolutionComponentReader
{
    // Component type 1 = Entity
    private const int EntityComponentType = 1;

    public static HashSet<string> GetEntityLogicalNames(IOrganizationService service, Guid solutionId)
    {
        var query = new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("objectid", "componenttype"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId),
                    new ConditionExpression("componenttype", ConditionOperator.Equal, EntityComponentType)
                }
            }
        };

        var results = service.RetrieveMultiple(query);
        var entityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in results.Entities)
        {
            var metadataId = component.GetAttributeValue<Guid>("objectid");
            if (metadataId == Guid.Empty) continue;

            var logicalName = ResolveEntityLogicalName(service, metadataId);
            if (!string.IsNullOrEmpty(logicalName))
            {
                entityNames.Add(logicalName);
            }
        }

        return entityNames;
    }

    private static string? ResolveEntityLogicalName(IOrganizationService service, Guid metadataId)
    {
        try
        {
            var request = new RetrieveEntityRequest
            {
                MetadataId = metadataId,
                EntityFilters = EntityFilters.Entity
            };

            var response = (RetrieveEntityResponse)service.Execute(request);
            return response.EntityMetadata.LogicalName;
        }
        catch
        {
            return null;
        }
    }
}
