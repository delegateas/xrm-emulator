using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class SavedQueryWriter
{
    public static void Update(IOrganizationService service, SavedQueryDefinition query)
    {
        var entity = new Entity("savedquery", query.SavedQueryId);
        entity["name"] = query.Name;

        if (query.FetchXml != null)
            entity["fetchxml"] = query.FetchXml;

        if (query.LayoutXml != null)
        {
            // CRM requires the 'object' attribute on <grid> for updates but strips it on export.
            // Auto-inject it by retrieving the entity type code from the existing view.
            var layoutXml = query.LayoutXml;
            if (layoutXml.Contains("<grid") && !layoutXml.Contains("object="))
            {
                var existing = service.Retrieve("savedquery", query.SavedQueryId,
                    new Microsoft.Xrm.Sdk.Query.ColumnSet("returnedtypecode"));
                var entityTypeCode = existing.GetAttributeValue<string>("returnedtypecode");
                if (!string.IsNullOrEmpty(entityTypeCode))
                {
                    // Resolve entity type code from logical name
                    var metaReq = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
                    {
                        LogicalName = entityTypeCode,
                        EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Entity
                    };
                    var metaResp = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)service.Execute(metaReq);
                    var otc = metaResp.EntityMetadata.ObjectTypeCode;
                    if (otc.HasValue)
                    {
                        layoutXml = layoutXml.Replace("<grid ", $"<grid object=\"{otc.Value}\" ");
                    }
                }
            }
            entity["layoutxml"] = layoutXml;
        }

        service.Update(entity);
    }

    public static Guid Create(IOrganizationService service, SavedQueryDefinition query,
        string entityLogicalName, string? solutionUniqueName)
    {
        var entity = new Entity("savedquery");
        entity["name"] = query.Name;
        entity["returnedtypecode"] = entityLogicalName;
        entity["querytype"] = 0;

        if (query.FetchXml != null)
            entity["fetchxml"] = query.FetchXml;

        if (query.LayoutXml != null)
            entity["layoutxml"] = query.LayoutXml;

        Guid id;
        try
        {
            id = service.Create(entity);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create savedquery '{query.Name}' for entity '{entityLogicalName}': {ex.Detail.Message}", ex);
        }

        // Add to solution separately
        if (!string.IsNullOrEmpty(solutionUniqueName))
        {
            var addRequest = new AddSolutionComponentRequest
            {
                ComponentId = id,
                ComponentType = 26, // SavedQuery
                SolutionUniqueName = solutionUniqueName
            };
            service.Execute(addRequest);
        }

        return id;
    }

    public static void Delete(IOrganizationService service, Guid savedQueryId)
    {
        service.Delete("savedquery", savedQueryId);
    }

    public static void PublishAll(IOrganizationService service)
    {
        service.Execute(new PublishAllXmlRequest());
    }
}
