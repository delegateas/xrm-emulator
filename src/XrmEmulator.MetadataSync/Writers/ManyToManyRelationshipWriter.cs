using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class ManyToManyRelationshipWriter
{
    /// <summary>
    /// Creates a new N:N (many-to-many) relationship between two entities.
    /// The intersect entity is created automatically with the given name
    /// (defaults to the relationship schema name).
    /// </summary>
    public static Guid Create(IOrganizationService service, ManyToManyRelationshipDefinition def, string? solutionUniqueName)
    {
        var intersectName = string.IsNullOrWhiteSpace(def.IntersectEntityName)
            ? def.SchemaName.ToLowerInvariant()
            : def.IntersectEntityName.ToLowerInvariant();

        var relationship = new ManyToManyRelationshipMetadata
        {
            SchemaName = def.SchemaName,
            IntersectEntityName = intersectName,
            Entity1LogicalName = def.Entity1LogicalName.ToLowerInvariant(),
            Entity2LogicalName = def.Entity2LogicalName.ToLowerInvariant(),
            Entity1AssociatedMenuConfiguration = BuildMenuConfiguration(def.Entity1MenuLabel),
            Entity2AssociatedMenuConfiguration = BuildMenuConfiguration(def.Entity2MenuLabel),
        };

        var req = new CreateManyToManyRequest
        {
            ManyToManyRelationship = relationship,
            IntersectEntitySchemaName = intersectName,
            SolutionUniqueName = solutionUniqueName,
        };

        var resp = (CreateManyToManyResponse)service.Execute(req);
        return resp.ManyToManyRelationshipId;
    }

    private static AssociatedMenuConfiguration BuildMenuConfiguration(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            // Dataverse default: show the related entity's display collection name.
            return new AssociatedMenuConfiguration
            {
                Behavior = AssociatedMenuBehavior.UseCollectionName,
                Group = AssociatedMenuGroup.Details,
            };
        }

        return new AssociatedMenuConfiguration
        {
            Behavior = AssociatedMenuBehavior.UseLabel,
            Group = AssociatedMenuGroup.Details,
            Label = new Label(label, 1030), // Danish — matches organization default language
        };
    }
}
