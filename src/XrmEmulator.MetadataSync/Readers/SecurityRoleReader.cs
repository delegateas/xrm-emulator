using System.Net.Http;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SecurityRole = DG.Tools.XrmMockup.SecurityRole;
using RolePrivilege = DG.Tools.XrmMockup.RolePrivilege;

namespace XrmEmulator.MetadataSync.Readers;

public static class SecurityRoleReader
{
    private const int MaxRetries = 5;

    public static List<SecurityRole> Read(IOrganizationService service)
    {
        var roles = new List<SecurityRole>();

        var rootBusinessUnitId = RetrieveRootBusinessUnitId(service);

        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("name", "roleid", "roletemplateid", "businessunitid")
        };

        var allRoleEntities = new List<Entity>();
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
            allRoleEntities.AddRange(page.Entities);
            if (!page.MoreRecords) break;
            pagingCookie = page.PagingCookie;
            pageNumber++;
        }

        // Fetch all role privileges in one batch rather than one call per role.
        // The N+1 pattern (one HTTP call per role) causes the server to drop the
        // connection mid-export (HttpIOException: response ended prematurely).
        var entityNamesByPrivilege = RetrievePrivilegeEntityNames(service);
        var allPrivileges = RetrieveAllRolePrivileges(service, entityNamesByPrivilege);

        foreach (var roleEntity in allRoleEntities)
        {
            var roleId = roleEntity.GetAttributeValue<Guid>("roleid");
            allPrivileges.TryGetValue(roleId, out var privileges);

            var role = new SecurityRole
            {
                Name = roleEntity.GetAttributeValue<string>("name") ?? string.Empty,
                RoleId = roleId,
                RoleTemplateId = roleEntity.GetAttributeValue<EntityReference>("roletemplateid")?.Id ?? Guid.Empty,
                BusinessUnitId = roleEntity.GetAttributeValue<EntityReference>("businessunitid"),
                Privileges = privileges ?? []
            };

            roles.Add(role);
        }

        return CollapseBusinessUnitCopies(roles, rootBusinessUnitId);
    }

    /// <summary>
    /// Dataverse stores one role record per business unit; only the copy in the root
    /// business unit carries the roleprivileges rows. Consumers write one file per role
    /// name, so keeping every copy means an empty child-BU copy overwrites the root copy
    /// and the exported role ends up with no privileges at all.
    /// Collapse to a single role per name: root-BU copy first, then any copy that actually
    /// has privileges, then whatever came first.
    /// </summary>
    private static List<SecurityRole> CollapseBusinessUnitCopies(
        List<SecurityRole> roles,
        Guid rootBusinessUnitId)
    {
        return roles
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.FirstOrDefault(r => r.BusinessUnitId?.Id == rootBusinessUnitId)
                ?? group.FirstOrDefault(r => r.Privileges.Count > 0)
                ?? group.First())
            .ToList();
    }

    /// <summary>
    /// The root business unit is the only one without a parent.
    /// </summary>
    private static Guid RetrieveRootBusinessUnitId(IOrganizationService service)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            TopCount = 1,
            Criteria =
            {
                Conditions = { new ConditionExpression("parentbusinessunitid", ConditionOperator.Null) }
            }
        };

        var result = RetryRetrieve(service, query);
        return result.Entities.Count > 0 ? result.Entities[0].Id : Guid.Empty;
    }

    /// <summary>
    /// Maps each privilege to the entity logical names it applies to, via
    /// privilegeobjecttypecodes. A privilege can cover several entities (activity
    /// privileges in particular), and task-based privileges such as prvSearchAvailability
    /// map to none at all.
    /// </summary>
    private static Dictionary<Guid, List<string>> RetrievePrivilegeEntityNames(IOrganizationService service)
    {
        var query = new QueryExpression("privilegeobjecttypecodes")
        {
            ColumnSet = new ColumnSet("privilegeid", "objecttypecode")
        };

        var byPrivilege = new Dictionary<Guid, List<string>>();
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

            foreach (var potc in page.Entities)
            {
                var privilegeId = GetReferenceId(potc, "privilegeid");
                var entityName = potc.GetAttributeValue<string>("objecttypecode");

                if (privilegeId == Guid.Empty || string.IsNullOrEmpty(entityName) || entityName == "none")
                    continue;

                if (!byPrivilege.TryGetValue(privilegeId, out var names))
                    byPrivilege[privilegeId] = names = [];

                names.Add(entityName);
            }

            if (!page.MoreRecords) break;
            pagingCookie = page.PagingCookie;
            pageNumber++;
        }

        return byPrivilege;
    }

    /// <summary>
    /// Fetches all roleprivileges records in pages and returns them grouped by role ID.
    ///
    /// The inner dictionary is keyed by <b>entity logical name</b>, not privilege name:
    /// that is the key XrmMockup looks up when it enforces access (Security.HasCallerPermission).
    /// Keying by privilege name silently disables all enforcement, because XrmMockup treats
    /// an entity that no role mentions as unsecured and grants access to everyone.
    /// </summary>
    private static Dictionary<Guid, Dictionary<string, Dictionary<AccessRights, RolePrivilege>>>
        RetrieveAllRolePrivileges(
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
                    Columns = new ColumnSet("name", "accessright", "canbebasic", "canbelocal", "canbedeep", "canbeglobal")
                }
            }
        };

        var byRole = new Dictionary<Guid, Dictionary<string, Dictionary<AccessRights, RolePrivilege>>>();
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

            foreach (var rp in page.Entities)
            {
                var roleId = rp.GetAttributeValue<Guid>("roleid");
                if (roleId == Guid.Empty) continue;

                var depthMask = rp.GetAttributeValue<int>("privilegedepthmask");
                if (depthMask == 0) continue;

                var accessRight = (AccessRights)GetAliasedValue<int>(rp, "priv.accessright");
                if (accessRight == AccessRights.None) continue;

                var privilegeId = GetReferenceId(rp, "privilegeid");
                if (!entityNamesByPrivilege.TryGetValue(privilegeId, out var entityNames))
                    continue; // task-based privilege with no entity behind it

                var rolePrivilege = new RolePrivilege
                {
                    AccessRight = accessRight,
                    PrivilegeDepth = ConvertDepthMask(depthMask),
                    CanBeBasic = GetAliasedValue<bool>(rp, "priv.canbebasic"),
                    CanBeLocal = GetAliasedValue<bool>(rp, "priv.canbelocal"),
                    CanBeDeep = GetAliasedValue<bool>(rp, "priv.canbedeep"),
                    CanBeGlobal = GetAliasedValue<bool>(rp, "priv.canbeglobal")
                };

                if (!byRole.TryGetValue(roleId, out var privsByEntity))
                    byRole[roleId] = privsByEntity = new Dictionary<string, Dictionary<AccessRights, RolePrivilege>>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (var entityName in entityNames)
                {
                    if (!privsByEntity.TryGetValue(entityName, out var privsByRight))
                        privsByEntity[entityName] = privsByRight = [];

                    privsByRight[accessRight] = rolePrivilege;
                }
            }

            if (!page.MoreRecords) break;
            pagingCookie = page.PagingCookie;
            pageNumber++;
        }

        return byRole;
    }

    /// <summary>
    /// Wraps RetrieveMultiple with exponential-backoff retries for transient
    /// HttpIOException (ResponseEnded) errors that occur on large Dataverse orgs.
    /// </summary>
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

    private static PrivilegeDepth ConvertDepthMask(int depthMask)
    {
        return depthMask switch
        {
            1 => PrivilegeDepth.Basic,
            2 => PrivilegeDepth.Local,
            4 => PrivilegeDepth.Deep,
            8 => PrivilegeDepth.Global,
            _ => PrivilegeDepth.Basic
        };
    }

    /// <summary>
    /// Reads a reference-valued attribute. Intersect entities such as roleprivileges
    /// return the raw Guid where ordinary entities return an EntityReference.
    /// </summary>
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
