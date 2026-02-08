using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SecurityRole = DG.Tools.XrmMockup.SecurityRole;
using RolePrivilege = DG.Tools.XrmMockup.RolePrivilege;

namespace XrmEmulator.MetadataSync.Readers;

public static class SecurityRoleReader
{
    public static List<SecurityRole> Read(IOrganizationService service)
    {
        var roles = new List<SecurityRole>();

        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("name", "roleid", "roletemplateid", "businessunitid")
        };

        var results = service.RetrieveMultiple(query);

        foreach (var roleEntity in results.Entities)
        {
            var roleId = roleEntity.GetAttributeValue<Guid>("roleid");
            var privileges = RetrieveRolePrivileges(service, roleId);

            var role = new SecurityRole
            {
                Name = roleEntity.GetAttributeValue<string>("name") ?? string.Empty,
                RoleId = roleId,
                RoleTemplateId = roleEntity.GetAttributeValue<EntityReference>("roletemplateid")?.Id ?? Guid.Empty,
                BusinessUnitId = roleEntity.GetAttributeValue<EntityReference>("businessunitid"),
                Privileges = privileges
            };

            roles.Add(role);
        }

        return roles;
    }

    private static Dictionary<string, Dictionary<AccessRights, RolePrivilege>> RetrieveRolePrivileges(
        IOrganizationService service, Guid roleId)
    {
        var privileges = new Dictionary<string, Dictionary<AccessRights, RolePrivilege>>(
            StringComparer.OrdinalIgnoreCase);

        var query = new QueryExpression("roleprivileges")
        {
            ColumnSet = new ColumnSet("privilegedepthmask"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("roleid", ConditionOperator.Equal, roleId)
                }
            },
            LinkEntities =
            {
                new LinkEntity("roleprivileges", "privilege", "privilegeid", "privilegeid", JoinOperator.Inner)
                {
                    EntityAlias = "priv",
                    Columns = new ColumnSet("name", "accessright", "canbebasic", "canbelocal", "canbedeep", "canbeglobal")
                }
            }
        };

        var results = service.RetrieveMultiple(query);

        foreach (var rp in results.Entities)
        {
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

            if (!privileges.ContainsKey(privName))
            {
                privileges[privName] = new Dictionary<AccessRights, RolePrivilege>();
            }

            privileges[privName][accessRight] = rolePrivilege;
        }

        return privileges;
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
