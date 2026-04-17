using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class EnableChangeTrackingWriter
{
    public static void Execute(IOrganizationService service, EnableChangeTrackingDefinition def)
    {
        var retrieveReq = new RetrieveEntityRequest
        {
            LogicalName = def.EntityLogicalName,
            EntityFilters = EntityFilters.Entity
        };
        var retrieveResp = (RetrieveEntityResponse)service.Execute(retrieveReq);
        var entity = retrieveResp.EntityMetadata;

        entity.ChangeTrackingEnabled = true;

        var updateReq = new UpdateEntityRequest
        {
            Entity = entity
        };
        updateReq.Parameters["SolutionUniqueName"] = def.SolutionUniqueName;

        service.Execute(updateReq);
    }
}
