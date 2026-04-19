using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class RelationshipDeleteWriter
{
    /// <summary>
    /// Deletes a relationship (1:N or N:N) by schema name via <see cref="DeleteRelationshipRequest"/>.
    /// For N:N, the intersect entity is dropped automatically.
    /// </summary>
    public static void Delete(IOrganizationService service, RelationshipDeleteDefinition def)
    {
        var req = new DeleteRelationshipRequest
        {
            Name = def.SchemaName,
        };
        service.Execute(req);
    }
}
