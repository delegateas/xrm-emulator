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
        // Find the role by name — pick the one in the root BU.
        // Auto-create if missing so staging privileges + the role itself is one commit.
        var existingRoleId = FindRoleByName(service, def.RoleName);
        Guid roleId;
        if (existingRoleId.HasValue)
        {
            roleId = existingRoleId.Value;
            log?.Invoke($"Found role '{def.RoleName}' (ID: {roleId})");
        }
        else
        {
            roleId = CreateRoleInRootBusinessUnit(service, def.RoleName, log);
            log?.Invoke($"Created new role '{def.RoleName}' (ID: {roleId})");
        }

        // Group privileges by entity to minimize SDK calls
        var privilegesByEntity = def.Privileges
            .GroupBy(p => p.Entity, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var addedPrivileges = new List<RolePrivilege>();
        var privilegeNames = new Dictionary<Guid, string>();

        foreach (var entityGroup in privilegesByEntity)
        {
            var entityLogicalName = entityGroup.Key;

            foreach (var priv in entityGroup)
            {
                Guid? privilegeId;
                string privilegeName;

                if (!string.IsNullOrWhiteSpace(priv.Privilege))
                {
                    // Task-based / miscellaneous privilege referenced by raw name
                    // (e.g. prvSearchAvailability) — no entity/access mapping applies.
                    privilegeName = priv.Privilege;
                    privilegeId = FindPrivilegeByName(service, privilegeName);

                    if (privilegeId == null)
                    {
                        throw new InvalidOperationException(
                            $"Privilege '{privilegeName}' not found in Dataverse. " +
                            "Check the privilege name — no changes have been applied to the role.");
                    }
                }
                else
                {
                    privilegeId = ResolvePrivilege(service, priv.Access, entityLogicalName, log);
                    privilegeName = MapAccessToPrivilegeName(priv.Access, entityLogicalName);

                    if (privilegeId == null)
                    {
                        throw new InvalidOperationException(
                            $"Privilege '{privilegeName}' not found in Dataverse. " +
                            "Check the entity name and access type — no changes have been applied to the role.");
                    }
                }

                var depth = ParseDepth(priv.Depth);
                addedPrivileges.Add(new RolePrivilege
                {
                    PrivilegeId = privilegeId.Value,
                    Depth = depth
                });
                privilegeNames[privilegeId.Value] = privilegeName;

                var what = string.IsNullOrWhiteSpace(priv.Privilege)
                    ? $"{priv.Access} on {entityLogicalName}"
                    : privilegeName;
                log?.Invoke($"  {what} ({depth}) → privilege {privilegeId.Value}");
            }
        }

        if (addedPrivileges.Count == 0)
        {
            log?.Invoke("No valid privileges to add.");
            return;
        }

        // Deduplicate by privilege GUID — two JSON entries can resolve to the same
        // underlying privilege (e.g. two system entities that share the Activity fallback),
        // and AddPrivilegesRoleRequest fails with "privilege is duplicated" if sent twice.
        var deduped = addedPrivileges
            .GroupBy(p => p.PrivilegeId)
            .Select(g => g.First())
            .ToArray();

        if (deduped.Length < addedPrivileges.Count)
            log?.Invoke($"  Deduplicated {addedPrivileges.Count - deduped.Length} duplicate privilege GUID(s) before sending.");

        // Fetch the role's existing privileges so ReplacePrivilegesRoleRequest does not wipe them.
        // ReplacePrivilegesRoleRequest requires the complete desired set — pending entries are
        // merged in (new depth wins on conflict). This also fixes the silent-drop issue that
        // AddPrivilegesRoleRequest has with certain system-entity privileges (e.g. prvAppendToUser).
        var existingPrivileges = FetchExistingRolePrivileges(service, roleId);
        log?.Invoke($"  Fetched {existingPrivileges.Length} existing privilege(s) from role.");

        // Merge: start from existing, overwrite any that appear in the pending delta.
        var pendingById = deduped.ToDictionary(p => p.PrivilegeId);
        var merged = existingPrivileges
            .Where(p => !pendingById.ContainsKey(p.PrivilegeId))   // keep existing not in delta
            .Concat(deduped)                                         // add all pending (new depth wins)
            .ToArray();

        var request = new ReplacePrivilegesRoleRequest
        {
            RoleId = roleId,
            Privileges = merged
        };

        try
        {
            service.Execute(request);
            log?.Invoke($"  Replaced role privileges: {existingPrivileges.Length} existing + {deduped.Length} pending → {merged.Length} total.");
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to replace privileges on role '{def.RoleName}': {ex.Detail.Message}", ex);
        }

        // Verify that every pending privilege actually persisted.
        VerifyPrivilegesPersisted(service, roleId, deduped, privilegeNames, def.RoleName, log);
    }

    /// <summary>
    /// Assign a role to a systemuser (associates via systemuserroles_association).
    /// Idempotent — if the association already exists, logs a notice and returns.
    /// </summary>
    public static void AssignToUser(
        IOrganizationService service,
        SecurityRoleAssignmentDefinition def,
        Action<string>? log = null)
    {
        var user = ResolveSystemUserWithBu(service, def.User)
            ?? throw new InvalidOperationException($"systemuser '{def.User}' not found.");
        var userId = user.Id;
        var userBuId = user.BusinessUnitId;

        // Roles live per business unit — find the copy in the user's BU.
        var roleId = FindRoleByNameInBusinessUnit(service, def.RoleName, userBuId)
            ?? throw new InvalidOperationException(
                $"Security role '{def.RoleName}' not found in business unit {userBuId} (user '{def.User}' lives there). " +
                "Ensure the role was created in the root BU so Dataverse auto-propagates copies to child BUs, then retry.");

        log?.Invoke($"Assigning role '{def.RoleName}' ({roleId}) to user '{def.User}' ({userId})");

        if (UserHasRole(service, userId, roleId))
        {
            log?.Invoke("  User already has this role — no-op.");
            return;
        }

        var request = new AssociateRequest
        {
            Target = new EntityReference("systemuser", userId),
            RelatedEntities = new EntityReferenceCollection
            {
                new EntityReference("role", roleId)
            },
            Relationship = new Relationship("systemuserroles_association")
        };

        try
        {
            service.Execute(request);
            log?.Invoke("  Role assigned OK.");
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to assign role '{def.RoleName}' to user '{def.User}': {ex.Detail.Message}", ex);
        }
    }

    private record SystemUserRef(Guid Id, Guid BusinessUnitId);

    private static SystemUserRef? ResolveSystemUserWithBu(IOrganizationService service, string identifier)
    {
        QueryExpression query;
        if (Guid.TryParse(identifier, out var id))
        {
            query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "businessunitid"),
                Criteria = new FilterExpression(LogicalOperator.Or)
                {
                    Conditions =
                    {
                        new ConditionExpression("systemuserid", ConditionOperator.Equal, id),
                        new ConditionExpression("applicationid", ConditionOperator.Equal, id),
                    }
                },
                TopCount = 2
            };
        }
        else
        {
            query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "businessunitid"),
                Criteria = new FilterExpression(LogicalOperator.Or)
                {
                    Conditions =
                    {
                        new ConditionExpression("domainname", ConditionOperator.Equal, identifier),
                        new ConditionExpression("fullname", ConditionOperator.Equal, identifier),
                        new ConditionExpression("internalemailaddress", ConditionOperator.Equal, identifier),
                    }
                },
                TopCount = 2
            };
        }

        var results = service.RetrieveMultiple(query);
        if (results.Entities.Count == 0) return null;
        if (results.Entities.Count > 1)
            throw new InvalidOperationException(
                $"Identifier '{identifier}' matched multiple users. Use the systemuserid GUID or a more specific identifier.");

        var row = results.Entities[0];
        var buRef = row.GetAttributeValue<EntityReference>("businessunitid")
            ?? throw new InvalidOperationException($"User '{identifier}' has no business unit.");
        return new SystemUserRef(row.Id, buRef.Id);
    }

    private static Guid? FindRoleByNameInBusinessUnit(IOrganizationService service, string roleName, Guid businessUnitId)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, roleName),
                    new ConditionExpression("businessunitid", ConditionOperator.Equal, businessUnitId),
                }
            },
            TopCount = 1
        };
        return service.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id;
    }

    private static bool UserHasRole(IOrganizationService service, Guid userId, Guid roleId)
    {
        var query = new QueryExpression("systemuserroles")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("systemuserid", ConditionOperator.Equal, userId),
                    new ConditionExpression("roleid", ConditionOperator.Equal, roleId),
                }
            },
            TopCount = 1
        };
        return service.RetrieveMultiple(query).Entities.Count > 0;
    }

    private static Guid CreateRoleInRootBusinessUnit(IOrganizationService service, string roleName, Action<string>? log)
    {
        var rootBuQuery = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("parentbusinessunitid", ConditionOperator.Null) }
            },
            TopCount = 1
        };
        var rootBu = service.RetrieveMultiple(rootBuQuery).Entities.FirstOrDefault()
            ?? throw new InvalidOperationException("Root business unit not found.");

        var role = new Entity("role")
        {
            ["name"] = roleName,
            ["businessunitid"] = new EntityReference("businessunit", rootBu.Id)
        };

        try
        {
            return service.Create(role);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create role '{roleName}': {ex.Detail.Message}", ex);
        }
    }

    private static Guid? FindRoleByName(IOrganizationService service, string roleName)
    {
        // Always target the root BU copy (parentroleid = null).
        // AddPrivilegesRoleRequest on the root automatically propagates to all child-BU copies.
        // Targeting a child copy instead would silently succeed but leave the root (and other
        // child copies) without the privilege — users in the root BU would never see it.
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, roleName),
                    new ConditionExpression("parentroleid", ConditionOperator.Null)
                }
            },
            TopCount = 1
        };

        var results = service.RetrieveMultiple(query);

        foreach (var role in results.Entities)
        {
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
            "merge" => "Merge",
            _ => throw new InvalidOperationException($"Unknown access type: '{access}'. Valid: Read, Write, Create, Delete, Append, AppendTo, Assign, Share, Merge")
        };

        return $"prv{accessVerb}{entityLogicalName}";
    }

    /// <summary>
    /// Resolves a privilege by entity name.
    /// Falls back to (1) the generic Activity privilege for custom activity entities, then
    /// (2) entity metadata, which handles system entities with non-standard privilege naming
    /// (e.g. 'systemuser' entity → 'prvAppendToUser', not 'prvAppendToSystemUser').
    /// </summary>
    private static Guid? ResolvePrivilege(IOrganizationService service, string access, string entityLogicalName, Action<string>? log)
    {
        var privilegeName = MapAccessToPrivilegeName(access, entityLogicalName);
        var id = FindPrivilegeByName(service, privilegeName);
        if (id != null) return id;

        // Try the generic Activity privilege — custom activity entities use it
        var activityPrivilegeName = MapAccessToPrivilegeName(access, "Activity");
        id = FindPrivilegeByName(service, activityPrivilegeName);
        if (id != null)
        {
            log?.Invoke($"  Resolved '{privilegeName}' → '{activityPrivilegeName}' (activity entity)");
            return id;
        }

        // Fall back to entity metadata — system entities often use a different privilege suffix
        // (e.g. 'systemuser' entity has 'prvAppendToUser', 'prvReadUser', etc.)
        id = ResolvePrivilegeFromEntityMetadata(service, access, entityLogicalName, log);
        return id;
    }

    private static Guid? ResolvePrivilegeFromEntityMetadata(
        IOrganizationService service, string access, string entityLogicalName, Action<string>? log)
    {
        PrivilegeType? privilegeType = access.ToLowerInvariant() switch
        {
            "read"           => PrivilegeType.Read,
            "write"          => PrivilegeType.Write,
            "update"         => PrivilegeType.Write,
            "create"         => PrivilegeType.Create,
            "delete"         => PrivilegeType.Delete,
            "append"         => PrivilegeType.Append,
            "appendto"       => PrivilegeType.AppendTo,
            "assign"         => PrivilegeType.Assign,
            "share"          => PrivilegeType.Share,
            _                => null
        };
        if (privilegeType == null) return null;

        try
        {
            var response = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
            {
                LogicalName = entityLogicalName,
                EntityFilters = EntityFilters.Privileges
            });

            var match = response.EntityMetadata.Privileges
                .FirstOrDefault(p => p.PrivilegeType == privilegeType.Value);

            if (match != null)
                log?.Invoke($"  Resolved via entity metadata: '{entityLogicalName}' {access} → '{match.Name}' ({match.PrivilegeId})");

            return match?.PrivilegeId;
        }
        catch (Exception ex)
        {
            log?.Invoke($"  Entity metadata lookup failed for '{entityLogicalName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Remove specific privileges from a security role. Only the listed privileges
    /// are removed; all other privileges on the role are left untouched.
    /// </summary>
    public static void RemovePrivileges(
        IOrganizationService service,
        SecurityRolePrivilegeRemoveDefinition def,
        Action<string>? log = null)
    {
        var roleId = FindRoleByName(service, def.RoleName)
            ?? throw new InvalidOperationException($"Role '{def.RoleName}' not found.");

        log?.Invoke($"Found role '{def.RoleName}' ({roleId})");

        var privilegeIds = new List<Guid>();
        foreach (var priv in def.Privileges)
        {
            var privilegeId = ResolvePrivilege(service, priv.Access, priv.Entity, log);
            if (privilegeId == null)
            {
                log?.Invoke($"  WARNING: Privilege '{priv.Access}' on '{priv.Entity}' not found — skipping");
                continue;
            }
            privilegeIds.Add(privilegeId.Value);
            log?.Invoke($"  Remove {priv.Access} on {priv.Entity} → privilege {privilegeId.Value}");
        }

        if (privilegeIds.Count == 0)
        {
            log?.Invoke("No valid privileges to remove.");
            return;
        }

        // RemovePrivilegeRoleRequest takes one privilege at a time
        foreach (var privId in privilegeIds)
        {
            try
            {
                service.Execute(new RemovePrivilegeRoleRequest { RoleId = roleId, PrivilegeId = privId });
                log?.Invoke($"  Removed privilege {privId} OK.");
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                throw new InvalidOperationException(
                    $"Failed to remove privilege {privId} from role '{def.RoleName}': {ex.Detail.Message}", ex);
            }
        }
        log?.Invoke($"  Removed {privilegeIds.Count} privilege(s) from role '{def.RoleName}'.");
    }

    /// <summary>
    /// Delete a security role by name. Only deletes the root BU copy (parentroleid = null);
    /// Dataverse automatically removes all child-BU inherited copies.
    /// Logs a warning and returns if the role is not found.
    /// </summary>
    public static void DeleteRole(
        IOrganizationService service,
        SecurityRoleDeleteDefinition def,
        Action<string>? log = null)
    {
        // Only delete the root BU copy — Dataverse rejects Delete on inherited child copies
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid", "name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, def.RoleName),
                    new ConditionExpression("parentroleid", ConditionOperator.Null),
                }
            },
            TopCount = 1
        };

        var results = service.RetrieveMultiple(query);
        if (results.Entities.Count == 0)
        {
            log?.Invoke($"Role '{def.RoleName}' not found (root BU copy) — nothing to delete.");
            return;
        }

        var roleId = results.Entities[0].Id;
        log?.Invoke($"Deleting root role '{def.RoleName}' ({roleId})");
        try
        {
            service.Delete("role", roleId);
            log?.Invoke($"  Deleted OK.");
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to delete role '{def.RoleName}' ({roleId}): {ex.Detail.Message}", ex);
        }
    }

    private static RolePrivilege[] FetchExistingRolePrivileges(IOrganizationService service, Guid roleId)
    {
        // roleprivileges.privilegedepthmask uses bit-flags (1=Basic,2=Local,4=Deep,8=Global)
        // which differ from PrivilegeDepth enum values (Basic=0,Local=1,Deep=2,Global=8).
        var q = new QueryExpression("roleprivileges")
        {
            ColumnSet = new ColumnSet("privilegeid", "privilegedepthmask"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("roleid", ConditionOperator.Equal, roleId) }
            }
        };
        return service.RetrieveMultiple(q).Entities
            .Select(e => new RolePrivilege
            {
                PrivilegeId = e.GetAttributeValue<Guid>("privilegeid"),
                Depth = MaskToDepth(e.GetAttributeValue<int>("privilegedepthmask"))
            })
            .Where(p => p.PrivilegeId != Guid.Empty)
            .ToArray();
    }

    private static PrivilegeDepth MaskToDepth(int mask) => mask switch
    {
        1 => PrivilegeDepth.Basic,
        2 => PrivilegeDepth.Local,
        4 => PrivilegeDepth.Deep,
        _ => PrivilegeDepth.Global   // 8
    };

    private static void VerifyPrivilegesPersisted(
        IOrganizationService service,
        Guid roleId,
        RolePrivilege[] expected,
        Dictionary<Guid, string> privilegeNames,
        string roleName,
        Action<string>? log)
    {
        // Query roleprivileges directly — RetrieveRolePrivilegesRequest is not available in this SDK version.
        var q = new QueryExpression("roleprivileges")
        {
            ColumnSet = new ColumnSet("privilegeid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("roleid", ConditionOperator.Equal, roleId) }
            }
        };
        var rows = service.RetrieveMultiple(q);
        var grantedIds = rows.Entities
            .Select(e => e.GetAttributeValue<Guid>("privilegeid"))
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var missing = expected.Select(p => p.PrivilegeId).Where(id => !grantedIds.Contains(id)).ToList();

        if (missing.Count > 0)
        {
            var missingList = string.Join(", ", missing.Select(id =>
                privilegeNames.TryGetValue(id, out var name) ? name : id.ToString()));
            throw new InvalidOperationException(
                $"Post-commit verification failed for role '{roleName}': " +
                $"privilege(s) not present after applying — {missingList}. " +
                "ReplacePrivilegesRoleRequest did not persist them — check Dataverse permissions or try again. " +
                "The pending file has NOT been archived — retry the commit.");
        }

        log?.Invoke($"  Verified: all {expected.Length} privilege(s) confirmed in Dataverse.");
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
