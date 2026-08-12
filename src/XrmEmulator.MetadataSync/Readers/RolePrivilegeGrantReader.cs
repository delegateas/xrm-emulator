using System.Net.Http;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Readers;

/// <summary>
/// Reads role privileges as the grants they actually are — one row per <c>roleprivileges</c>
/// record — instead of the entity-keyed shape <see cref="SecurityRoleReader"/> produces.
/// </summary>
/// <remarks>
/// A single privilege can cover many entities: <c>prvCreateActivity</c> applies to every activity
/// type in the org (appointment, task, email, and whatever Power Pages, Customer Voice and
/// Omnichannel installed), and <c>prvReadCustomization</c>-style privileges cover dozens of
/// metadata entities. Fanning those out per entity — which the emulator needs, because XrmMockup
/// enforces access by entity name — turns one decision into thirty rows and makes an audit
/// unreadable. This reader keeps the privilege as the unit and lists the entities it reaches.
///
/// It also keeps task-based privileges (<c>prvSearchAvailability</c>, <c>prvExportToExcel</c>, …),
/// which the entity-keyed reader has to drop because they map to no entity at all.
/// </remarks>
public static class RolePrivilegeGrantReader
{
    private const int MaxRetries = 5;

    public sealed record Grant
    {
        public required string PrivilegeName { get; init; }
        public required AccessRights AccessRight { get; init; }
        public required PrivilegeDepth Depth { get; init; }
        public required List<string> Entities { get; init; }

        public bool IsTaskPrivilege => Entities.Count == 0;
    }

    public sealed record RoleGrants
    {
        public required string RoleName { get; init; }
        public required Guid RoleId { get; init; }
        public required List<Grant> Grants { get; init; }
    }

    public static List<RoleGrants> Read(IOrganizationService service)
    {
        var rootBusinessUnitId = RetrieveRootBusinessUnitId(service);
        var roleEntities = RetrieveAll(service, new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("name", "roleid", "businessunitid")
        });

        var entityNamesByPrivilege = RetrievePrivilegeEntityNames(service);
        var grantsByRole = RetrieveGrants(service, entityNamesByPrivilege);

        // Dataverse keeps one role record per business unit and only the root-BU copy carries
        // roleprivileges rows, so collapse by name the same way SecurityRoleReader does.
        return roleEntities
            .Select(r => new
            {
                Name = r.GetAttributeValue<string>("name") ?? string.Empty,
                Id = r.GetAttributeValue<Guid>("roleid"),
                BusinessUnitId = r.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty
            })
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var chosen = group.FirstOrDefault(r => r.BusinessUnitId == rootBusinessUnitId)
                    ?? group.FirstOrDefault(r => grantsByRole.ContainsKey(r.Id))
                    ?? group.First();

                grantsByRole.TryGetValue(chosen.Id, out var grants);

                return new RoleGrants
                {
                    RoleName = chosen.Name,
                    RoleId = chosen.Id,
                    Grants = grants ?? []
                };
            })
            .ToList();
    }

    private static Dictionary<Guid, List<Grant>> RetrieveGrants(
        IOrganizationService service,
        Dictionary<Guid, List<string>> entityNamesByPrivilege)
    {
        var query = new QueryExpression("roleprivileges")
        {
            ColumnSet = new ColumnSet("roleid", "privilegeid", "privilegedepthmask"),
            LinkEntities =
            {
                new LinkEntity("roleprivileges", "privilege", "privilegeid", "privilegeid", JoinOperator.Inner)
                {
                    EntityAlias = "priv",
                    Columns = new ColumnSet("name", "accessright")
                }
            }
        };

        var byRole = new Dictionary<Guid, List<Grant>>();

        foreach (var rp in RetrieveAll(service, query))
        {
            var roleId = rp.GetAttributeValue<Guid>("roleid");
            if (roleId == Guid.Empty) continue;

            var depthMask = rp.GetAttributeValue<int>("privilegedepthmask");
            if (depthMask == 0) continue;

            var privilegeName = GetAliasedValue<string>(rp, "priv.name");
            if (string.IsNullOrWhiteSpace(privilegeName)) continue;

            var privilegeId = GetReferenceId(rp, "privilegeid");
            entityNamesByPrivilege.TryGetValue(privilegeId, out var entities);

            if (!byRole.TryGetValue(roleId, out var grants))
                byRole[roleId] = grants = [];

            grants.Add(new Grant
            {
                PrivilegeName = privilegeName!,
                AccessRight = (AccessRights)GetAliasedValue<int>(rp, "priv.accessright"),
                Depth = ConvertDepthMask(depthMask),
                Entities = entities is null
                    ? []
                    : entities.Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
                        .ToList()
            });
        }

        return byRole;
    }

    private static Dictionary<Guid, List<string>> RetrievePrivilegeEntityNames(IOrganizationService service)
    {
        var byPrivilege = new Dictionary<Guid, List<string>>();

        var query = new QueryExpression("privilegeobjecttypecodes")
        {
            ColumnSet = new ColumnSet("privilegeid", "objecttypecode")
        };

        foreach (var potc in RetrieveAll(service, query))
        {
            var privilegeId = GetReferenceId(potc, "privilegeid");
            var entityName = potc.GetAttributeValue<string>("objecttypecode");

            if (privilegeId == Guid.Empty || string.IsNullOrEmpty(entityName) || entityName == "none")
                continue;

            if (!byPrivilege.TryGetValue(privilegeId, out var names))
                byPrivilege[privilegeId] = names = [];

            names.Add(entityName);
        }

        return byPrivilege;
    }

    private static Guid RetrieveRootBusinessUnitId(IOrganizationService service)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            TopCount = 1,
            Criteria = { Conditions = { new ConditionExpression("parentbusinessunitid", ConditionOperator.Null) } }
        };

        var result = RetryRetrieve(service, query);
        return result.Entities.Count > 0 ? result.Entities[0].Id : Guid.Empty;
    }

    private static List<Entity> RetrieveAll(IOrganizationService service, QueryExpression query)
    {
        var all = new List<Entity>();
        var pageNumber = 1;
        string? pagingCookie = null;

        while (true)
        {
            query.PageInfo = new PagingInfo
            {
                Count = 250,
                PageNumber = pageNumber,
                PagingCookie = pagingCookie
            };

            var page = RetryRetrieve(service, query);
            all.AddRange(page.Entities);
            if (!page.MoreRecords) break;

            pagingCookie = page.PagingCookie;
            pageNumber++;
        }

        return all;
    }

    private static EntityCollection RetryRetrieve(IOrganizationService service, QueryBase query)
    {
        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return service.RetrieveMultiple(query);
            }
            catch (HttpIOException) when (attempt < MaxRetries)
            {
                Thread.Sleep(delay);
                delay *= 2;
            }
        }
    }

    private static PrivilegeDepth ConvertDepthMask(int depthMask) => depthMask switch
    {
        1 => PrivilegeDepth.Basic,
        2 => PrivilegeDepth.Local,
        4 => PrivilegeDepth.Deep,
        8 => PrivilegeDepth.Global,
        _ => PrivilegeDepth.Basic
    };

    private static Guid GetReferenceId(Entity entity, string attributeName)
    {
        if (!entity.Contains(attributeName)) return Guid.Empty;

        return entity[attributeName] switch
        {
            EntityReference reference => reference.Id,
            Guid id => id,
            _ => Guid.Empty
        };
    }

    private static T? GetAliasedValue<T>(Entity entity, string attributeName)
    {
        if (entity.Contains(attributeName) && entity[attributeName] is AliasedValue aliased)
        {
            return (T)aliased.Value;
        }

        return default;
    }
}
