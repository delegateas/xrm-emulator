using DG.Tools.XrmMockup;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Readers;

public static class PluginReader
{
    public static List<MetaPlugin> Read(IOrganizationService service, HashSet<string> entityNames)
    {
        var plugins = new List<MetaPlugin>();

        var query = new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet(
                "name", "mode", "rank", "stage",
                "filteringattributes", "asyncautodelete",
                "impersonatinguserid", "sdkmessageprocessingstepid"),
            LinkEntities =
            {
                new LinkEntity("sdkmessageprocessingstep", "sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid", JoinOperator.LeftOuter)
                {
                    EntityAlias = "filter",
                    Columns = new ColumnSet("primaryobjecttypecode")
                },
                new LinkEntity("sdkmessageprocessingstep", "plugintype", "eventhandler", "plugintypeid", JoinOperator.LeftOuter)
                {
                    EntityAlias = "plugintype",
                    Columns = new ColumnSet("assemblyname", "name")
                },
                new LinkEntity("sdkmessageprocessingstep", "sdkmessage", "sdkmessageid", "sdkmessageid", JoinOperator.LeftOuter)
                {
                    EntityAlias = "message",
                    Columns = new ColumnSet("name")
                }
            },
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Enabled
                }
            }
        };

        var results = service.RetrieveMultiple(query);

        foreach (var step in results.Entities)
        {
            var primaryEntity = GetAliasedValue<string>(step, "filter.primaryobjecttypecode") ?? "none";

            // Filter to only requested entities (or "none" for global plugins)
            if (primaryEntity != "none" && !entityNames.Contains(primaryEntity))
                continue;

            var images = RetrieveImages(service, step.Id);

            var plugin = new MetaPlugin
            {
                Name = step.GetAttributeValue<string>("name") ?? string.Empty,
                Mode = step.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0,
                Rank = step.GetAttributeValue<int>("rank"),
                Stage = step.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 0,
                FilteredAttributes = step.GetAttributeValue<string>("filteringattributes") ?? string.Empty,
                AsyncAutoDelete = step.GetAttributeValue<bool>("asyncautodelete"),
                MessageName = GetAliasedValue<string>(step, "message.name") ?? string.Empty,
                AssemblyName = GetAliasedValue<string>(step, "plugintype.assemblyname") ?? string.Empty,
                PluginTypeAssemblyName = GetAliasedValue<string>(step, "plugintype.name") ?? string.Empty,
                PrimaryEntity = primaryEntity,
                ImpersonatingUserId = step.GetAttributeValue<EntityReference>("impersonatinguserid")?.Id,
                Images = images
            };

            plugins.Add(plugin);
        }

        return plugins;
    }

    private static List<MetaImage> RetrieveImages(IOrganizationService service, Guid stepId)
    {
        var query = new QueryExpression("sdkmessageprocessingstepimage")
        {
            ColumnSet = new ColumnSet("attributes", "entityalias", "name", "imagetype"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId)
                }
            }
        };

        var results = service.RetrieveMultiple(query);
        var images = new List<MetaImage>();

        foreach (var image in results.Entities)
        {
            images.Add(new MetaImage
            {
                Attributes = image.GetAttributeValue<string>("attributes") ?? string.Empty,
                EntityAlias = image.GetAttributeValue<string>("entityalias") ?? string.Empty,
                Name = image.GetAttributeValue<string>("name") ?? string.Empty,
                ImageType = image.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0
            });
        }

        return images;
    }

    private static T? GetAliasedValue<T>(Entity entity, string attributeName)
    {
        if (entity.Contains(attributeName) && entity[attributeName] is AliasedValue aliased)
        {
            return (T)aliased.Value;
        }
        return default;
    }
}
