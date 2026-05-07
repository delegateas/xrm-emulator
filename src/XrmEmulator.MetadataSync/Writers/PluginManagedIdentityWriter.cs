using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

/// <summary>
/// Applies a PluginManagedIdentityDefinition to CRM:
///   - Reuses or creates a managedidentity record for the (applicationid, tenantid) pair.
///   - Links the pluginassembly to that record via the managedidentityid lookup.
///
/// Both the assembly and the cert it's signed with must already be live in CRM —
/// Dataverse rejects the link otherwise with `0x80040216 — Plugin assembly must be
/// signed with valid certificate to associate to Managed Identity`.
/// </summary>
public static class PluginManagedIdentityWriter
{
    public record ApplyResult(
        Guid PluginAssemblyId,        // Guid.Empty if assembly wasn't in CRM yet (pre-import)
        Guid ManagedIdentityId,
        bool ManagedIdentityCreated,
        bool LinkUpdated,
        bool LinkSkippedAssemblyMissing,
        bool LinkSkippedAssemblyUnsigned);

    public static ApplyResult Apply(IOrganizationService service, PluginManagedIdentityDefinition def)
    {
        // 1. Reuse or create the managedidentity row.
        //    Lookup priority: by pinned ManagedIdentityId (cross-env GUID), then by ApplicationId.
        Entity? existingMi = null;
        if (def.ManagedIdentityId is { } pinnedId)
        {
            try
            {
                existingMi = service.Retrieve("managedidentity", pinnedId,
                    new ColumnSet("managedidentityid", "applicationid"));
            }
            catch
            {
                // Row doesn't exist with that id yet — fall through to create.
            }
        }
        if (existingMi == null)
        {
            var miQuery = new QueryExpression("managedidentity")
            {
                ColumnSet = new ColumnSet("managedidentityid", "applicationid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("applicationid", ConditionOperator.Equal, def.ApplicationId)
                    }
                },
                TopCount = 1
            };
            var miResults = service.RetrieveMultiple(miQuery);
            if (miResults.Entities.Count > 0)
                existingMi = miResults.Entities[0];
        }

        Guid managedIdentityId;
        bool managedIdentityCreated = false;
        if (existingMi != null)
        {
            managedIdentityId = existingMi.Id;
        }
        else
        {
            var mi = new Entity("managedidentity")
            {
                ["managedidentityid"] = def.ManagedIdentityId ?? Guid.NewGuid(),
                ["applicationid"] = def.ApplicationId,
                ["tenantid"] = def.TenantId,
                ["credentialsource"] = 2, // Managed client (federated identity credential)
                ["subjectscope"] = 1,     // Environment scope
                ["version"] = 1
            };
            managedIdentityId = service.Create(mi);
            managedIdentityCreated = true;
        }

        // 2. Locate the pluginassembly by name. If absent, the env is being pre-staged
        //    ahead of a solution import — the row is enough; the link is part of the
        //    solution payload itself.
        var asmQuery = new QueryExpression("pluginassembly")
        {
            ColumnSet = new ColumnSet("pluginassemblyid", "name", "managedidentityid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, def.AssemblyName)
                }
            },
            TopCount = 1
        };
        var asmResults = service.RetrieveMultiple(asmQuery);
        if (asmResults.Entities.Count == 0)
        {
            return new ApplyResult(Guid.Empty, managedIdentityId, managedIdentityCreated, false, true, false);
        }
        var assembly = asmResults.Entities[0];

        // 3. Link assembly → managedidentity, if not already linked to the same id.
        //    Dataverse rejects this with 0x80040216 ("Plugin assembly must be signed with
        //    valid certificate to associate to Managed Identity") when the assembly in this
        //    env hasn't been Authenticode-signed yet. In a target-env onboarding flow that's
        //    expected — the signed bits arrive with the solution import, which carries the
        //    managedidentityid lookup itself. We treat that specific error as "skip link",
        //    not failure, so the row creation still gets credited.
        bool linkUpdated = false;
        bool linkSkippedUnsigned = false;
        var existingLink = assembly.GetAttributeValue<EntityReference>("managedidentityid");
        if (existingLink == null || existingLink.Id != managedIdentityId)
        {
            try
            {
                var update = new Entity("pluginassembly", assembly.Id)
                {
                    ["managedidentityid"] = new EntityReference("managedidentity", managedIdentityId)
                };
                service.Update(update);
                linkUpdated = true;
            }
            catch (Exception ex) when (IsAssemblyUnsignedFault(ex))
            {
                linkSkippedUnsigned = true;
            }
        }

        return new ApplyResult(assembly.Id, managedIdentityId, managedIdentityCreated, linkUpdated, false, linkSkippedUnsigned);
    }

    // Dataverse signals "you can't link an unsigned assembly to a managed identity" via
    // error code 0x80040216 with a specific message. The exception type depends on which
    // transport ServiceClient used:
    //   - SOAP path → FaultException<OrganizationServiceFault>, message on .Detail.Message
    //   - Web API path → Microsoft.Rest.HttpOperationException, message body on
    //     .Response.Content (reached via reflection — Microsoft.Rest.ClientRuntime isn't
    //     a direct dependency here).
    // Walk the inner-exception chain and check both shapes.
    private static bool IsAssemblyUnsignedFault(Exception ex)
    {
        const string marker = "Plugin assembly must be signed with valid certificate";
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if ((e.Message ?? string.Empty).Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
            if (e is FaultException<OrganizationServiceFault> fault &&
                (fault.Detail?.Message ?? string.Empty).Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
            // HttpOperationException.Response.Content holds the OData error body.
            var responseProp = e.GetType().GetProperty("Response");
            if (responseProp?.GetValue(e) is { } response)
            {
                var contentProp = response.GetType().GetProperty("Content");
                if (contentProp?.GetValue(response) is string content
                    && content.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
