using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Writers;

public static class IconWriter
{
    /// <summary>
    /// Uploads (or updates) a web resource.
    /// Returns true if created, false if updated.
    /// </summary>
    public static bool UploadWebResource(
        IOrganizationService service,
        string name,
        string displayName,
        string base64Content,
        string? solutionUniqueName,
        int webResourceType = 11)
    {
        // Query for existing web resource by name
        var query = new QueryExpression("webresource")
        {
            ColumnSet = new ColumnSet("webresourceid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, name)
                }
            }
        };

        var existing = service.RetrieveMultiple(query).Entities.FirstOrDefault();

        if (existing != null)
        {
            // Update existing
            var update = new Entity("webresource", existing.Id);
            update["content"] = base64Content;
            update["displayname"] = displayName;
            service.Update(update);
            return false;
        }

        // Create new
        var webResource = new Entity("webresource");
        webResource["name"] = name;
        webResource["displayname"] = displayName;
        webResource["content"] = base64Content;
        webResource["webresourcetype"] = new OptionSetValue(webResourceType);

        var createRequest = new CreateRequest { Target = webResource };
        if (!string.IsNullOrEmpty(solutionUniqueName))
            createRequest.Parameters["SolutionUniqueName"] = solutionUniqueName;

        service.Execute(createRequest);
        return true;
    }

    /// <summary>
    /// Sets the IconVectorName on an entity's metadata.
    /// Returns true if the update ran, false if it was skipped (e.g. virtual entity).
    /// Virtual entities reject UpdateEntityRequest with
    /// "You can't set the DataProvider of this entity to null", so we probe
    /// the metadata first and no-op when the target is virtual — the SVG
    /// web resource still gets uploaded.
    /// </summary>
    public static bool SetEntityIcon(
        IOrganizationService service,
        string entityLogicalName,
        string webResourceName,
        Action<string>? log = null)
    {
        var retrieve = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Entity,
        });

        if (retrieve.EntityMetadata?.DataProviderId != null
            || retrieve.EntityMetadata?.IsLogicalEntity == true)
        {
            log?.Invoke($"  Skipped IconVectorName: '{entityLogicalName}' is a virtual entity.");
            return false;
        }

        service.Execute(new UpdateEntityRequest
        {
            Entity = new EntityMetadata
            {
                LogicalName = entityLogicalName,
                IconVectorName = webResourceName
            }
        });
        return true;
    }
}
