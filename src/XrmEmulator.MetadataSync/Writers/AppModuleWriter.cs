using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Writers;

public static class AppModuleWriter
{
    public static Guid GetAppModuleId(IOrganizationService service, string appModuleUniqueName)
    {
        var query = new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("appmoduleid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("uniquename", ConditionOperator.Equal, appModuleUniqueName)
                }
            }
        };

        var results = service.RetrieveMultiple(query);
        if (results.Entities.Count == 0)
            throw new InvalidOperationException($"AppModule '{appModuleUniqueName}' not found in CRM.");

        return results.Entities[0].Id;
    }

    public static void AddEntity(IOrganizationService service, string appModuleUniqueName, string entityLogicalName)
    {
        var appModuleId = GetAppModuleId(service, appModuleUniqueName);

        // Retrieve the entity's MetadataId
        var retrieveRequest = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Entity
        };
        var retrieveResponse = (RetrieveEntityResponse)service.Execute(retrieveRequest);
        var metadataId = retrieveResponse.EntityMetadata.MetadataId
            ?? throw new InvalidOperationException($"Entity '{entityLogicalName}' has no MetadataId.");

        service.Execute(new AddAppComponentsRequest
        {
            AppId = appModuleId,
            Components = new EntityReferenceCollection
            {
                new EntityReference("entity", metadataId)
            }
        });
    }

    /// <summary>
    /// Associate a security role with an AppModule (role map). Resolves the role to its
    /// root-BU copy and issues AddAppComponentsRequest. Idempotent — if the role is already
    /// in the app's role map, logs a notice and returns.
    /// </summary>
    public static void AddRole(
        IOrganizationService service,
        string appModuleUniqueName,
        string roleName,
        Action<string>? log = null)
    {
        var appModuleId = GetAppModuleId(service, appModuleUniqueName);

        var rootBuId = GetRootBusinessUnitId(service);
        var roleId = FindRoleInBusinessUnit(service, roleName, rootBuId)
            ?? throw new InvalidOperationException(
                $"Security role '{roleName}' not found in root business unit. " +
                "AppModule role maps require the root-BU copy. Create the role in the root BU first.");

        log?.Invoke($"Associating role '{roleName}' ({roleId}) with app '{appModuleUniqueName}' ({appModuleId})");

        if (AppModuleHasRole(service, appModuleId, roleId))
        {
            log?.Invoke("  Role already in app map — no-op.");
            return;
        }

        // AppModule↔role uses the appmoduleroles_association N:N relationship,
        // not AddAppComponentsRequest (which silently no-ops for role components).
        service.Execute(new AssociateRequest
        {
            Target = new EntityReference("appmodule", appModuleId),
            RelatedEntities = new EntityReferenceCollection
            {
                new EntityReference("role", roleId)
            },
            Relationship = new Relationship("appmoduleroles_association")
        });

        log?.Invoke("  Role added to app map OK.");
    }

    private static Guid GetRootBusinessUnitId(IOrganizationService service)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("parentbusinessunitid", ConditionOperator.Null) }
            },
            TopCount = 1
        };
        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault()
            ?? throw new InvalidOperationException("Root business unit not found.");
        return result.Id;
    }

    private static Guid? FindRoleInBusinessUnit(IOrganizationService service, string roleName, Guid businessUnitId)
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

    private static bool AppModuleHasRole(IOrganizationService service, Guid appModuleId, Guid roleId)
    {
        var query = new QueryExpression("appmoduleroles")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("appmoduleid", ConditionOperator.Equal, appModuleId),
                    new ConditionExpression("roleid", ConditionOperator.Equal, roleId),
                }
            },
            TopCount = 1
        };
        return service.RetrieveMultiple(query).Entities.Count > 0;
    }

    public static void UpdateViewSelection(
        IOrganizationService service,
        string appModuleUniqueName,
        List<Guid> viewIdsToAdd,
        List<Guid> viewIdsToRemove)
    {
        var appModuleId = GetAppModuleId(service, appModuleUniqueName);

        // Remove views first
        if (viewIdsToRemove.Count > 0)
        {
            service.Execute(new RemoveAppComponentsRequest
            {
                AppId = appModuleId,
                Components = new EntityReferenceCollection(
                    viewIdsToRemove.Select(id => new EntityReference("savedquery", id)).ToList())
            });
        }

        // Add views
        if (viewIdsToAdd.Count > 0)
        {
            service.Execute(new AddAppComponentsRequest
            {
                AppId = appModuleId,
                Components = new EntityReferenceCollection(
                    viewIdsToAdd.Select(id => new EntityReference("savedquery", id)).ToList())
            });
        }
    }

    public static void AddBpf(
        IOrganizationService service,
        string appModuleUniqueName,
        string bpfName,
        string? primaryEntity = null)
    {
        var appModuleId = GetAppModuleId(service, appModuleUniqueName);
        var workflowId = ResolveBpfWorkflowId(service, bpfName, primaryEntity);

        service.Execute(new AddAppComponentsRequest
        {
            AppId = appModuleId,
            Components = new EntityReferenceCollection
            {
                new EntityReference("workflow", workflowId)
            }
        });
    }

    public static void RemoveBpf(
        IOrganizationService service,
        string appModuleUniqueName,
        string bpfName,
        string? primaryEntity = null)
    {
        var appModuleId = GetAppModuleId(service, appModuleUniqueName);
        var workflowId = ResolveBpfWorkflowId(service, bpfName, primaryEntity);

        service.Execute(new RemoveAppComponentsRequest
        {
            AppId = appModuleId,
            Components = new EntityReferenceCollection
            {
                new EntityReference("workflow", workflowId)
            }
        });
    }

    private static Guid ResolveBpfWorkflowId(IOrganizationService service, string bpfName, string? primaryEntity)
    {
        var query = new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid", "name", "primaryentity"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, bpfName),
                    new ConditionExpression("category", ConditionOperator.Equal, 4),
                }
            },
        };
        if (!string.IsNullOrEmpty(primaryEntity))
            query.Criteria.Conditions.Add(
                new ConditionExpression("primaryentity", ConditionOperator.Equal, primaryEntity));

        var results = service.RetrieveMultiple(query);
        if (results.Entities.Count == 0)
            throw new InvalidOperationException($"Business Process Flow named '{bpfName}' not found in CRM.");
        if (results.Entities.Count > 1)
        {
            var matches = string.Join(", ", results.Entities.Select(e =>
                $"{e.GetAttributeValue<string>("name")} (on {e.GetAttributeValue<string>("primaryentity")})"));
            throw new InvalidOperationException(
                $"Multiple BPFs named '{bpfName}' found: {matches}. Use --entity to disambiguate.");
        }
        return results.Entities[0].Id;
    }

    public static void UpdateFormSelection(
        IOrganizationService service,
        string appModuleUniqueName,
        List<Guid> formIdsToAdd,
        List<Guid> formIdsToRemove)
    {
        var appModuleId = GetAppModuleId(service, appModuleUniqueName);

        // Remove forms first
        if (formIdsToRemove.Count > 0)
        {
            service.Execute(new RemoveAppComponentsRequest
            {
                AppId = appModuleId,
                Components = new EntityReferenceCollection(
                    formIdsToRemove.Select(id => new EntityReference("systemform", id)).ToList())
            });
        }

        // Add forms
        if (formIdsToAdd.Count > 0)
        {
            service.Execute(new AddAppComponentsRequest
            {
                AppId = appModuleId,
                Components = new EntityReferenceCollection(
                    formIdsToAdd.Select(id => new EntityReference("systemform", id)).ToList())
            });
        }
    }
}
