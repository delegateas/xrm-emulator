using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class SiteMapWriter
{
    public static void Update(IOrganizationService service, SiteMapDefinition siteMap)
    {
        // Query for the sitemap record by unique name
        var query = new QueryExpression("sitemap")
        {
            ColumnSet = new ColumnSet("sitemapid", "sitemapnameunique"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sitemapnameunique", ConditionOperator.Equal, siteMap.UniqueName)
                }
            }
        };

        var results = service.RetrieveMultiple(query);
        if (results.Entities.Count == 0)
            throw new InvalidOperationException($"SiteMap not found with uniquename: {siteMap.UniqueName}");

        var siteMapEntity = results.Entities[0];
        var updateEntity = new Entity("sitemap", siteMapEntity.Id);
        updateEntity["sitemapxml"] = siteMap.SiteMapXml;
        service.Update(updateEntity);
    }

    public static void PublishAll(IOrganizationService service)
    {
        service.Execute(new PublishAllXmlRequest());
    }
}
