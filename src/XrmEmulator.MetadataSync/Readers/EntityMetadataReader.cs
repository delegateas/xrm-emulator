using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace XrmEmulator.MetadataSync.Readers;

public static class EntityMetadataReader
{
    public static Dictionary<string, EntityMetadata> Read(
        IOrganizationService service, HashSet<string> entityNames)
    {
        var result = new Dictionary<string, EntityMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var entityName in entityNames)
        {
            try
            {
                var request = new RetrieveEntityRequest
                {
                    LogicalName = entityName,
                    EntityFilters = EntityFilters.Entity
                        | EntityFilters.Attributes
                        | EntityFilters.Relationships
                        | EntityFilters.Privileges
                };

                var response = (RetrieveEntityResponse)service.Execute(request);
                result[entityName] = response.EntityMetadata;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to retrieve metadata for entity '{entityName}': {ex.Message}");
            }
        }

        return result;
    }

    public static Dictionary<string, Dictionary<int, int>> BuildDefaultStateStatus(
        Dictionary<string, EntityMetadata> entityMetadata)
    {
        var defaultStateStatus = new Dictionary<string, Dictionary<int, int>>();

        foreach (var (logicalName, metadata) in entityMetadata)
        {
            var stateStatusMap = new Dictionary<int, int>();

            var stateAttribute = metadata.Attributes?
                .OfType<StateAttributeMetadata>()
                .FirstOrDefault();

            if (stateAttribute?.OptionSet?.Options != null)
            {
                foreach (var option in stateAttribute.OptionSet.Options)
                {
                    if (option.Value.HasValue)
                    {
                        var statusAttribute = metadata.Attributes?
                            .OfType<StatusAttributeMetadata>()
                            .FirstOrDefault();

                        var defaultStatus = statusAttribute?.OptionSet?.Options
                            .OfType<StatusOptionMetadata>()
                            .Where(s => s.State == option.Value.Value)
                            .Select(s => s.Value)
                            .FirstOrDefault();

                        if (defaultStatus.HasValue)
                        {
                            stateStatusMap[option.Value.Value] = defaultStatus.Value;
                        }
                    }
                }
            }

            if (stateStatusMap.Count > 0)
            {
                defaultStateStatus[logicalName] = stateStatusMap;
            }
        }

        return defaultStateStatus;
    }
}
