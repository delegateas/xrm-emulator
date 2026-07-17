using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Readers;

/// <summary>
/// Lightweight live reader for the singleton organization record's orgdborgsettings column.
/// Distinct from OrganizationReader (which does a full-row ColumnSet(true) read used once
/// during full sync export) — this one is scoped to two columns for fast, frequent calls
/// from the orgsetting list/get/set/rollback commands.
/// </summary>
public static class OrgDbOrgSettingsReader
{
    public static (Guid OrganizationId, string? Xml) RetrieveLive(IOrganizationService service)
    {
        var query = new QueryExpression("organization")
        {
            ColumnSet = new ColumnSet("organizationid", "orgdborgsettings"),
            TopCount = 1
        };

        var org = service.RetrieveMultiple(query).Entities.FirstOrDefault()
            ?? throw new InvalidOperationException("No organization entity found.");

        return (org.Id, org.GetAttributeValue<string>("orgdborgsettings"));
    }
}
