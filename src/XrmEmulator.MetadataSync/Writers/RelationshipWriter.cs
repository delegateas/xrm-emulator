using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class RelationshipWriter
{
    public static void Update(IOrganizationService service, RelationshipUpdateDefinition def)
    {
        // Retrieve the existing relationship
        var retrieveReq = new RetrieveRelationshipRequest
        {
            Name = def.SchemaName
        };
        var retrieveResp = (RetrieveRelationshipResponse)service.Execute(retrieveReq);
        var relationship = retrieveResp.RelationshipMetadata;

        if (relationship is OneToManyRelationshipMetadata oneToMany)
        {
            var cascade = oneToMany.CascadeConfiguration;

            if (def.DeleteBehavior != null)
                cascade.Delete = ParseCascadeType(def.DeleteBehavior);
            if (def.AssignBehavior != null)
                cascade.Assign = ParseCascadeType(def.AssignBehavior);
            if (def.ShareBehavior != null)
                cascade.Share = ParseCascadeType(def.ShareBehavior);
            if (def.UnshareBehavior != null)
                cascade.Unshare = ParseCascadeType(def.UnshareBehavior);
            if (def.ReparentBehavior != null)
                cascade.Reparent = ParseCascadeType(def.ReparentBehavior);
            if (def.MergeBehavior != null)
                cascade.Merge = ParseCascadeType(def.MergeBehavior);

            var updateReq = new UpdateRelationshipRequest
            {
                Relationship = oneToMany
            };
            service.Execute(updateReq);
        }
        else
        {
            throw new InvalidOperationException(
                $"Relationship '{def.SchemaName}' is not a One-to-Many relationship. Cascade configuration only applies to 1:N relationships.");
        }
    }

    private static CascadeType ParseCascadeType(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "cascade" => CascadeType.Cascade,
            "removelink" => CascadeType.RemoveLink,
            "restrict" => CascadeType.Restrict,
            "nocascade" => CascadeType.NoCascade,
            "active" => CascadeType.Active,
            "userowned" => CascadeType.UserOwned,
            _ => throw new InvalidOperationException(
                $"Unknown cascade type: '{value}'. Valid values: Cascade, RemoveLink, Restrict, NoCascade, Active, UserOwned")
        };
    }
}
