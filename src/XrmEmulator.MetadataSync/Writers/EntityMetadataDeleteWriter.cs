using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Spectre.Console;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class EntityMetadataDeleteWriter
{
    /// <summary>
    /// Deletes an entire custom entity from Dataverse via <see cref="DeleteEntityRequest"/>.
    /// Before the final call, queries <c>RetrieveDependenciesForDeleteRequest</c> and
    /// removes every blocking component from the target solution, then retries.
    ///
    /// Still relies on the caller to have removed records, relationships, and AppModule
    /// references beforehand — Dataverse refuses the delete if those remain.
    /// </summary>
    public static void Delete(IOrganizationService service, EntityMetadataDeleteDefinition def, Action<string>? log = null)
    {
        var logical = def.EntityLogicalName.ToLowerInvariant();

        var entityMd = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = logical,
            EntityFilters = EntityFilters.Entity,
        });

        var entityId = entityMd.EntityMetadata.MetadataId ?? Guid.Empty;
        if (entityId == Guid.Empty)
        {
            throw new InvalidOperationException($"Could not resolve MetadataId for entity '{logical}'.");
        }

        if (!string.IsNullOrEmpty(def.SolutionUniqueName))
        {
            ClearDependencies(service, entityId, def.SolutionUniqueName!, log);
        }

        service.Execute(new DeleteEntityRequest { LogicalName = logical });
    }

    private static void ClearDependencies(
        IOrganizationService service,
        Guid entityId,
        string solutionName,
        Action<string>? log)
    {
        // Component types on dependency records: 1=Entity, 2=Attribute, 9=Relationship,
        // 26=SavedQuery, 60=SystemForm, 61=WebResource, 59=Report, 24=FormControl, etc.
        var req = new RetrieveDependenciesForDeleteRequest
        {
            ComponentType = 1, // Entity
            ObjectId = entityId,
        };
        var resp = (RetrieveDependenciesForDeleteResponse)service.Execute(req);

        var deps = resp.EntityCollection.Entities;
        AnsiConsole.MarkupLine($"  [grey]Dependencies returned by Dataverse: {deps.Count}[/]");
        if (deps.Count == 0) return;

        foreach (var d in deps)
        {
            var depObjectId = d.GetAttributeValue<Guid?>("dependentcomponentobjectid");
            var depTypeVal = d.GetAttributeValue<OptionSetValue>("dependentcomponenttype");
            if (depObjectId is not Guid id || depTypeVal is null)
            {
                AnsiConsole.MarkupLine("    [yellow]Skipping dependency row with missing id/type[/]");
                continue;
            }

            var componentType = depTypeVal.Value;
            var label = DescribeComponent(componentType, id);

            try
            {
                service.Execute(new RemoveSolutionComponentRequest
                {
                    ComponentId = id,
                    ComponentType = componentType,
                    SolutionUniqueName = solutionName,
                });
                AnsiConsole.MarkupLine($"    [green]Removed from solution:[/] {Markup.Escape(label)}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"    [red]Could not remove[/] {Markup.Escape(label)}: {Markup.Escape(ex.Message)}");
            }
        }
    }

    private static string DescribeComponent(int componentType, Guid id)
    {
        var typeName = componentType switch
        {
            1 => "Entity",
            2 => "Attribute",
            9 => "Relationship",
            10 => "AttributePicklistValue",
            11 => "AttributeLookupValue",
            12 => "ViewAttribute",
            22 => "DisplayString",
            24 => "Form (legacy)",
            26 => "SavedQuery",
            36 => "EntityMap",
            44 => "RibbonCustomization",
            59 => "Report",
            60 => "SystemForm",
            61 => "WebResource",
            62 => "SiteMap",
            65 => "HierarchyRule",
            66 => "CustomControl",
            68 => "CustomControlDefaultConfig",
            _ => $"ComponentType({componentType})",
        };
        return $"{typeName} {id}";
    }
}
