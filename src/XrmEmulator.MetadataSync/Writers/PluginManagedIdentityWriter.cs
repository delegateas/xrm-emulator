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
    public record ApplyResult(Guid PluginAssemblyId, Guid ManagedIdentityId, bool ManagedIdentityCreated, bool LinkUpdated);

    public static ApplyResult Apply(IOrganizationService service, PluginManagedIdentityDefinition def)
    {
        // 1. Locate the pluginassembly by name.
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
            throw new InvalidOperationException(
                $"Plug-in assembly '{def.AssemblyName}' not found in CRM. Push the assembly first via `plugin update` + commit.");
        }
        var assembly = asmResults.Entities[0];

        // 2. Reuse an existing managedidentity row for this clientId, otherwise create one.
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

        Guid managedIdentityId;
        bool managedIdentityCreated = false;
        if (miResults.Entities.Count > 0)
        {
            managedIdentityId = miResults.Entities[0].Id;
        }
        else
        {
            var mi = new Entity("managedidentity")
            {
                ["managedidentityid"] = Guid.NewGuid(),
                ["applicationid"] = def.ApplicationId,
                ["tenantid"] = def.TenantId,
                ["credentialsource"] = 2, // Managed client (federated identity credential)
                ["subjectscope"] = 1,     // Environment scope
                ["version"] = 1
            };
            managedIdentityId = service.Create(mi);
            managedIdentityCreated = true;
        }

        // 3. Link assembly → managedidentity, if not already linked to the same id.
        bool linkUpdated = false;
        var existingLink = assembly.GetAttributeValue<EntityReference>("managedidentityid");
        if (existingLink == null || existingLink.Id != managedIdentityId)
        {
            var update = new Entity("pluginassembly", assembly.Id)
            {
                ["managedidentityid"] = new EntityReference("managedidentity", managedIdentityId)
            };
            service.Update(update);
            linkUpdated = true;
        }

        return new ApplyResult(assembly.Id, managedIdentityId, managedIdentityCreated, linkUpdated);
    }
}
