using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Readers;

/// <summary>A solution as it exists in one environment.</summary>
public record SolutionInfo(Guid SolutionId, string UniqueName, string FriendlyName, string Version, bool IsManaged);

/// <summary>
/// One environment variable's state in an environment: the definition's default plus the
/// environment-specific current value (absent when the environment relies on the default).
/// </summary>
public record EnvVarState(
    Guid DefinitionId,
    string SchemaName,
    string? DisplayName,
    int? Type,
    string? DefaultValue,
    string? CurrentValue)
{
    public bool UsesDefault => CurrentValue == null;

    /// <summary>Secret-typed variables hold a Key Vault pointer, not the secret itself.</summary>
    public bool IsSecret => Type == 100000005;
}

/// <summary>A connection reference with no connection bound — dependent flows stay dormant.</summary>
public record UnboundConnectionReference(string LogicalName, string? DisplayName, string? ConnectorId);

/// <summary>An assembly in the solution whose managed-identity lookup must resolve in the target.</summary>
public record ManagedIdentityBinding(string AssemblyName, Guid ManagedIdentityId);

/// <summary>A plug-in step that is registered but not running.</summary>
public record DisabledPluginStep(Guid StepId, string Name, string? PrimaryEntity, string? MessageName);

/// <summary>
/// Live queries used by the <c>promote</c> command to pre-flight a solution import and to report
/// on environment-specific carriers afterwards. Everything is read from the environments
/// themselves — no dependency on a local metadata-sync folder.
/// </summary>
public static class PromoteReports
{
    /// <summary>Looks up a solution by unique name. Returns null when the target does not have it yet.</summary>
    public static SolutionInfo? GetSolution(IOrganizationService service, string uniqueName)
    {
        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid", "uniquename", "friendlyname", "version", "ismanaged"),
            TopCount = 1,
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("uniquename", ConditionOperator.Equal, uniqueName)
                }
            }
        };

        var result = service.RetrieveMultiple(query);
        if (result.Entities.Count == 0)
            return null;

        var e = result.Entities[0];
        return new SolutionInfo(
            e.Id,
            e.GetAttributeValue<string>("uniquename") ?? uniqueName,
            e.GetAttributeValue<string>("friendlyname") ?? uniqueName,
            e.GetAttributeValue<string>("version") ?? "?",
            e.GetAttributeValue<bool>("ismanaged"));
    }

    /// <summary>
    /// Every object id in the solution, regardless of component type. Callers intersect this with
    /// an entity query instead of relying on componenttype codes, which differ between the
    /// documented values and what this codebase's display map assumes.
    /// </summary>
    public static HashSet<Guid> GetSolutionComponentIds(IOrganizationService service, Guid solutionId)
    {
        var query = new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("objectid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId),
                    new ConditionExpression("objectid", ConditionOperator.NotNull)
                }
            }
        };

        var ids = new HashSet<Guid>();
        foreach (var e in RetrieveAll(service, query))
        {
            var oid = e.GetAttributeValue<Guid?>("objectid");
            if (oid.HasValue && oid.Value != Guid.Empty)
                ids.Add(oid.Value);
        }
        return ids;
    }

    /// <summary>
    /// Assemblies in the solution that carry a managed-identity lookup. A solution export writes
    /// the lookup as a hard GUID, so the matching <c>managedidentity</c> row must already exist in
    /// the target — the row itself is not a solution component.
    /// </summary>
    public static List<ManagedIdentityBinding> GetManagedIdentityBindings(
        IOrganizationService service, HashSet<Guid> solutionComponentIds)
    {
        var query = new QueryExpression("pluginassembly")
        {
            ColumnSet = new ColumnSet("name", "managedidentityid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("managedidentityid", ConditionOperator.NotNull)
                }
            }
        };

        var bindings = new List<ManagedIdentityBinding>();
        foreach (var e in RetrieveAll(service, query))
        {
            if (!solutionComponentIds.Contains(e.Id))
                continue;

            var mi = e.GetAttributeValue<EntityReference>("managedidentityid");
            if (mi != null)
                bindings.Add(new ManagedIdentityBinding(e.GetAttributeValue<string>("name") ?? e.Id.ToString(), mi.Id));
        }
        return bindings;
    }

    /// <summary>
    /// Plug-in steps in the solution that are disabled (statecode 1). An import only enables steps
    /// when <c>PublishWorkflows</c> is set, so this is the check that catches a solution whose logic
    /// arrived but is not running.
    /// </summary>
    public static List<DisabledPluginStep> GetDisabledPluginSteps(
        IOrganizationService service, HashSet<Guid> solutionComponentIds)
    {
        var query = new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet("sdkmessageprocessingstepid", "name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.Equal, 1) // Disabled
                }
            },
            LinkEntities =
            {
                new LinkEntity("sdkmessageprocessingstep", "sdkmessagefilter",
                    "sdkmessagefilterid", "sdkmessagefilterid", JoinOperator.LeftOuter)
                {
                    EntityAlias = "filter",
                    Columns = new ColumnSet("primaryobjecttypecode")
                },
                new LinkEntity("sdkmessageprocessingstep", "sdkmessage",
                    "sdkmessageid", "sdkmessageid", JoinOperator.LeftOuter)
                {
                    EntityAlias = "message",
                    Columns = new ColumnSet("name")
                }
            }
        };

        var disabled = new List<DisabledPluginStep>();
        foreach (var e in RetrieveAll(service, query))
        {
            if (!solutionComponentIds.Contains(e.Id))
                continue;

            disabled.Add(new DisabledPluginStep(
                e.Id,
                e.GetAttributeValue<string>("name") ?? e.Id.ToString(),
                GetAliased<string>(e, "filter.primaryobjecttypecode"),
                GetAliased<string>(e, "message.name")));
        }
        return disabled
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static T? GetAliased<T>(Entity entity, string attributeName) =>
        entity.Contains(attributeName) && entity[attributeName] is AliasedValue aliased
            ? (T)aliased.Value
            : default;

    /// <summary>True when the target already holds the managed-identity row with this id.</summary>
    public static bool ManagedIdentityExists(IOrganizationService service, Guid managedIdentityId)
    {
        try
        {
            service.Retrieve("managedidentity", managedIdentityId, new ColumnSet("managedidentityid"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Environment variables belonging to the solution, with each one's default and the current
    /// environment-specific value. Used both to snapshot the target before an import and to report
    /// afterwards which variables now run on the source environment's default.
    /// </summary>
    public static List<EnvVarState> GetEnvironmentVariables(
        IOrganizationService service, HashSet<Guid> solutionComponentIds)
    {
        var definitions = new QueryExpression("environmentvariabledefinition")
        {
            ColumnSet = new ColumnSet("environmentvariabledefinitionid", "schemaname", "displayname", "type", "defaultvalue")
        };

        var inSolution = RetrieveAll(service, definitions)
            .Where(e => solutionComponentIds.Contains(e.Id))
            .ToList();

        if (inSolution.Count == 0)
            return [];

        // Current values are separate rows; fetch them all once and match by definition.
        var values = new QueryExpression("environmentvariablevalue")
        {
            ColumnSet = new ColumnSet("environmentvariabledefinitionid", "value")
        };

        var valueByDefinition = new Dictionary<Guid, string?>();
        foreach (var v in RetrieveAll(service, values))
        {
            var defRef = v.GetAttributeValue<EntityReference>("environmentvariabledefinitionid");
            if (defRef != null)
                valueByDefinition[defRef.Id] = v.GetAttributeValue<string>("value");
        }

        return inSolution
            .Select(e => new EnvVarState(
                e.Id,
                e.GetAttributeValue<string>("schemaname") ?? e.Id.ToString(),
                e.GetAttributeValue<string>("displayname"),
                e.GetAttributeValue<OptionSetValue>("type")?.Value,
                e.GetAttributeValue<string>("defaultvalue"),
                valueByDefinition.TryGetValue(e.Id, out var current) ? current : null))
            .OrderBy(v => v.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Connection references in the solution with no connection bound. An import always brings
    /// these in unbound, and every cloud flow that depends on one stays dormant until it is
    /// connected by hand.
    /// </summary>
    public static List<UnboundConnectionReference> GetUnboundConnectionReferences(
        IOrganizationService service, HashSet<Guid> solutionComponentIds)
    {
        var query = new QueryExpression("connectionreference")
        {
            ColumnSet = new ColumnSet(
                "connectionreferenceid", "connectionreferencelogicalname",
                "connectionreferencedisplayname", "connectionid", "connectorid")
        };

        var unbound = new List<UnboundConnectionReference>();
        foreach (var e in RetrieveAll(service, query))
        {
            if (!solutionComponentIds.Contains(e.Id))
                continue;
            if (!string.IsNullOrEmpty(e.GetAttributeValue<string>("connectionid")))
                continue;

            unbound.Add(new UnboundConnectionReference(
                e.GetAttributeValue<string>("connectionreferencelogicalname") ?? e.Id.ToString(),
                e.GetAttributeValue<string>("connectionreferencedisplayname"),
                e.GetAttributeValue<string>("connectorid")));
        }
        return unbound;
    }

    /// <summary>
    /// Retrieves every page of a query. The existing single-call RetrieveMultiple usages in this
    /// codebase silently cap at 5000 rows, which a large solution's component list exceeds.
    /// </summary>
    private static IEnumerable<Entity> RetrieveAll(IOrganizationService service, QueryExpression query)
    {
        var pageNumber = 1;
        string? pagingCookie = null;

        while (true)
        {
            query.PageInfo = new PagingInfo
            {
                Count = 5000,
                PageNumber = pageNumber,
                PagingCookie = pagingCookie
            };

            var page = service.RetrieveMultiple(query);
            foreach (var e in page.Entities)
                yield return e;

            if (!page.MoreRecords)
                break;

            pagingCookie = page.PagingCookie;
            pageNumber++;
        }
    }
}
