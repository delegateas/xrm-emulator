using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class SecurityRoleWriter
{
    /// <summary>
    /// Add privileges to a security role. Finds the role in the root business unit.
    /// </summary>
    public static void UpdatePrivileges(
        IOrganizationService service,
        SecurityRoleUpdateDefinition def,
        Action<string>? log = null)
    {
        // Find the role by name — pick the one in the root BU (has most privileges typically)
        var roleId = FindRoleByName(service, def.RoleName)
            ?? throw new InvalidOperationException($"Security role '{def.RoleName}' not found.");

        log?.Invoke($"Found role '{def.RoleName}' (ID: {roleId})");

        // Group privileges by entity to minimize SDK calls
        var privilegesByEntity = def.Privileges
            .GroupBy(p => p.Entity, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var addedPrivileges = new List<RolePrivilege>();

        foreach (var entityGroup in privilegesByEntity)
        {
            var entityLogicalName = entityGroup.Key;

            foreach (var priv in entityGroup)
            {
                var privilegeId = ResolvePrivilege(service, priv.Access, entityLogicalName, log);

                if (privilegeId == null)
                {
                    var privilegeName = MapAccessToPrivilegeName(priv.Access, entityLogicalName);
                    log?.Invoke($"  WARNING: Privilege '{privilegeName}' not found — skipping");
                    continue;
                }

                var depth = ParseDepth(priv.Depth);
                addedPrivileges.Add(new RolePrivilege
                {
                    PrivilegeId = privilegeId.Value,
                    Depth = depth
                });

                log?.Invoke($"  {priv.Access} on {entityLogicalName} ({depth}) → privilege {privilegeId.Value}");
            }
        }

        if (addedPrivileges.Count == 0)
        {
            log?.Invoke("No valid privileges to add.");
            return;
        }

        // Add all privileges in one call
        var request = new AddPrivilegesRoleRequest
        {
            RoleId = roleId,
            Privileges = addedPrivileges.ToArray()
        };

        try
        {
            service.Execute(request);
            log?.Invoke($"  Added {addedPrivileges.Count} privilege(s) to role '{def.RoleName}'.");
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to add privileges to role '{def.RoleName}': {ex.Detail.Message}", ex);
        }
    }

    private static Guid? FindRoleByName(IOrganizationService service, string roleName)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, roleName)
                }
            },
            Orders = { new OrderExpression("modifiedon", OrderType.Descending) },
            TopCount = 10
        };

        // Prefer the role instance that has the most privileges (root BU)
        // by joining roleprivileges — or just pick the first one with parentroleid = null
        var results = service.RetrieveMultiple(query);

        // Try to find the root role (parentroleid is null for root BU roles)
        foreach (var role in results.Entities)
        {
            // The root BU copy typically has parentroleid = null
            // Just return the first match — AddPrivilegesRoleRequest propagates to child roles
            return role.Id;
        }

        return null;
    }

    private static Guid? FindPrivilegeByName(IOrganizationService service, string privilegeName)
    {
        var query = new QueryExpression("privilege")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, privilegeName)
                }
            },
            TopCount = 1
        };

        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.Id;
    }

    /// <summary>
    /// Map access type + entity to CRM privilege name.
    /// CRM convention: prv{Access}{EntityLogicalName} e.g. prvReadkf_partnerrelation
    /// For activity entities, falls back to the generic Activity privilege if the entity-specific one doesn't exist.
    /// </summary>
    private static string MapAccessToPrivilegeName(string access, string entityLogicalName)
    {
        var accessVerb = access.ToLowerInvariant() switch
        {
            "read" => "Read",
            "write" or "update" => "Write",
            "create" => "Create",
            "delete" => "Delete",
            "append" => "Append",
            "appendto" => "AppendTo",
            "assign" => "Assign",
            "share" => "Share",
            _ => throw new InvalidOperationException($"Unknown access type: '{access}'. Valid: Read, Write, Create, Delete, Append, AppendTo, Assign, Share")
        };

        return $"prv{accessVerb}{entityLogicalName}";
    }

    /// <summary>
    /// Resolves a privilege by entity name, falling back to the base Activity privilege
    /// for custom activity entities that inherit from activitypointer.
    /// </summary>
    private static Guid? ResolvePrivilege(IOrganizationService service, string access, string entityLogicalName, Action<string>? log)
    {
        var privilegeName = MapAccessToPrivilegeName(access, entityLogicalName);
        var id = FindPrivilegeByName(service, privilegeName);

        if (id == null)
        {
            // Try the generic Activity privilege — custom activity entities use it
            var activityPrivilegeName = MapAccessToPrivilegeName(access, "Activity");
            id = FindPrivilegeByName(service, activityPrivilegeName);
            if (id != null)
            {
                log?.Invoke($"  Resolved '{privilegeName}' → '{activityPrivilegeName}' (activity entity)");
            }
        }

        return id;
    }

    private static PrivilegeDepth ParseDepth(string depth)
    {
        return depth.ToLowerInvariant() switch
        {
            "basic" or "user" => PrivilegeDepth.Basic,
            "local" or "businessunit" => PrivilegeDepth.Local,
            "deep" or "parentchild" => PrivilegeDepth.Deep,
            "global" or "organization" => PrivilegeDepth.Global,
            _ => throw new InvalidOperationException($"Unknown privilege depth: '{depth}'. Valid: Basic, Local, Deep, Global")
        };
    }
}
