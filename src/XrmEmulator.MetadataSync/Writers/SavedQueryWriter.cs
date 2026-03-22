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
            entity["layoutxml"] = query.LayoutXml;

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
