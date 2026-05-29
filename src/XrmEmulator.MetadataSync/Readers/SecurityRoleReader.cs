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
        var allPrivileges = RetrieveAllRolePrivileges(service);

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

        return roles;
    }

    /// <summary>
    /// Fetches all roleprivileges records in pages and returns them grouped by role ID.
    /// </summary>
    private static Dictionary<Guid, Dictionary<string, Dictionary<AccessRights, RolePrivilege>>>
        RetrieveAllRolePrivileges(IOrganizationService service)
    {
        var query = new QueryExpression("roleprivileges")
        {
            ColumnSet = new ColumnSet("roleid", "privilegedepthmask"),
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

                var privName = GetAliasedValue<string>(rp, "priv.name");
                if (string.IsNullOrEmpty(privName)) continue;

                var depthMask = rp.GetAttributeValue<int>("privilegedepthmask");
                var accessRightValue = GetAliasedValue<int>(rp, "priv.accessright");
                var accessRight = (AccessRights)accessRightValue;
                var depth = ConvertDepthMask(depthMask);

                var rolePrivilege = new RolePrivilege
                {
                    AccessRight = accessRight,
                    PrivilegeDepth = depth,
                    CanBeBasic = GetAliasedValue<bool>(rp, "priv.canbebasic"),
                    CanBeLocal = GetAliasedValue<bool>(rp, "priv.canbelocal"),
                    CanBeDeep = GetAliasedValue<bool>(rp, "priv.canbedeep"),
                    CanBeGlobal = GetAliasedValue<bool>(rp, "priv.canbeglobal")
                };

                if (!byRole.TryGetValue(roleId, out var privsByName))
                    byRole[roleId] = privsByName = new Dictionary<string, Dictionary<AccessRights, RolePrivilege>>(
                        StringComparer.OrdinalIgnoreCase);

                if (!privsByName.TryGetValue(privName, out var privsByRight))
                    privsByName[privName] = privsByRight = [];

                privsByRight[accessRight] = rolePrivilege;
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

    private static T? GetAliasedValue<T>(Entity entity, string attributeName)
    {
        if (entity.Contains(attributeName) && entity[attributeName] is AliasedValue aliased)
        {
            return (T)aliased.Value;
        }
        return default;
    }
}
