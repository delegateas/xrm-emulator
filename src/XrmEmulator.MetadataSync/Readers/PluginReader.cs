using System.Net.Http;
using DG.Tools.XrmMockup;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Readers;

public static class PluginReader
{
    private const int MaxRetries = 5;

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

        var allSteps = new List<Entity>();
        var pageNumber = 1;
        string? pagingCookie = null;
        while (true)
        {
            query.PageInfo = new PagingInfo
            {
                Count = 50,
                PageNumber = pageNumber,
                PagingCookie = pagingCookie
            };
            var page = RetryRetrieve(service, query);
            allSteps.AddRange(page.Entities);
            if (!page.MoreRecords) break;
            pagingCookie = page.PagingCookie;
            pageNumber++;
        }

        // Fetch all step images in one paged query rather than one call per step.
        // The N+1 pattern (one HTTP call per step) causes the server to drop the
        // connection mid-export (HttpIOException: response ended prematurely).
        var allImages = RetrieveAllImages(service);

        foreach (var step in allSteps)
        {
            var primaryEntity = GetAliasedValue<string>(step, "filter.primaryobjecttypecode") ?? "none";

            // Filter to only requested entities (or "none" for global plugins)
            if (primaryEntity != "none" && !entityNames.Contains(primaryEntity))
                continue;

            allImages.TryGetValue(step.Id, out var images);

            var plugin = new MetaPlugin
            {
                Name = step.GetAttributeValue<string>("name") ?? string.Empty,
                Mode = step.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0,
                Rank = step.GetAttributeValue<int>("rank"),
                Stage = step.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 0,
                FilteredAttributes = step.GetAttributeValue<string>("filteringattributes") ?? string.Empty,
                AsyncAutoDelete = step.GetAttributeValue<bool>("asyncautodelete"),
                MessageName = GetAliasedValue<string>(step, "message.name") ?? string.Empty,
                // MetadataRegistrationStrategy checks: AssemblyName == pluginType.FullName (full type name)
                // and PluginTypeAssemblyName == pluginType.Assembly.GetName().Name (short assembly name).
                AssemblyName = GetAliasedValue<string>(step, "plugintype.name") ?? string.Empty,
                PluginTypeAssemblyName = GetAliasedValue<string>(step, "plugintype.assemblyname") ?? string.Empty,
                PrimaryEntity = primaryEntity,
                ImpersonatingUserId = step.GetAttributeValue<EntityReference>("impersonatinguserid")?.Id,
                Images = images ?? []
            };

            plugins.Add(plugin);
        }

        return plugins;
    }

    /// <summary>
    /// Fetches all sdkmessageprocessingstepimage records in pages and returns them
    /// grouped by sdkmessageprocessingstepid.
    /// </summary>
    private static Dictionary<Guid, List<MetaImage>> RetrieveAllImages(IOrganizationService service)
    {
        var query = new QueryExpression("sdkmessageprocessingstepimage")
        {
            ColumnSet = new ColumnSet("sdkmessageprocessingstepid", "attributes", "entityalias", "name", "imagetype"),
        };

        var byStep = new Dictionary<Guid, List<MetaImage>>();
        var pageNumber = 1;
        string? pagingCookie = null;

        while (true)
        {
            query.PageInfo = new PagingInfo
            {
                Count = 250,
                PageNumber = pageNumber,
                PagingCookie = pagingCookie
            };

            var page = RetryRetrieve(service, query);

            foreach (var image in page.Entities)
            {
                var stepRef = image.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid");
                if (stepRef is null) continue;

                if (!byStep.TryGetValue(stepRef.Id, out var list))
                    byStep[stepRef.Id] = list = [];

                list.Add(new MetaImage
                {
                    Attributes = image.GetAttributeValue<string>("attributes") ?? string.Empty,
                    EntityAlias = image.GetAttributeValue<string>("entityalias") ?? string.Empty,
                    Name = image.GetAttributeValue<string>("name") ?? string.Empty,
                    ImageType = image.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0
                });
            }

            if (!page.MoreRecords) break;
            pagingCookie = page.PagingCookie;
            pageNumber++;
        }

        return byStep;
    }

    /// <summary>
    /// Wraps RetrieveMultiple with exponential-backoff retries for transient
    /// HttpIOException (ResponseEnded) errors that occur on large Dataverse orgs.
    /// </summary>
    private static EntityCollection RetryRetrieve(IOrganizationService service, QueryBase query)
    {
        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return service.RetrieveMultiple(query);
            }
            catch (HttpIOException) when (attempt < MaxRetries)
            {
                Thread.Sleep(delay);
                delay *= 2;
            }
        }
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
