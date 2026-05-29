using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Readers;

public static class OrgStructureReader
{
    public static OrgStructureData Read(IOrganizationService service)
    {
        var businessUnits = ReadBusinessUnits(service);
        var teamRoleMap = ReadAllTeamRoles(service);
        var teams = ReadTeams(service, teamRoleMap);
        return new OrgStructureData(businessUnits, teams);
    }

    private static List<BusinessUnitData> ReadBusinessUnits(IOrganizationService service)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid", "name", "parentbusinessunitid")
        };

        return service.RetrieveMultiple(query).Entities
            .Select(e => new BusinessUnitData(
                e.Id,
                e.GetAttributeValue<string>("name") ?? e.Id.ToString(),
                e.GetAttributeValue<EntityReference>("parentbusinessunitid")?.Id))
            .ToList();
    }

    // Fetch all team-role assignments in one round trip using a role→teamroles link entity.
    private static Dictionary<Guid, List<Guid>> ReadAllTeamRoles(IOrganizationService service)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid")
        };
        var link = query.AddLink("teamroles", "roleid", "roleid");
        link.Columns = new ColumnSet("teamid");
        link.EntityAlias = "tr";

        var map = new Dictionary<Guid, List<Guid>>();
        foreach (var entity in service.RetrieveMultiple(query).Entities)
        {
            if (entity.GetAttributeValue<AliasedValue>("tr.teamid")?.Value is not Guid teamId)
                continue;

            if (!map.TryGetValue(teamId, out var roles))
                map[teamId] = roles = [];
            roles.Add(entity.Id);
        }
        return map;
    }

    private static List<TeamData> ReadTeams(
        IOrganizationService service,
        Dictionary<Guid, List<Guid>> teamRoleMap)
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("teamid", "name", "businessunitid", "teamtype")
        };
        // Skip XrmMockup's auto-created default teams and Azure AD group teams (types 2 and 3).
        query.Criteria.AddCondition("isdefault", ConditionOperator.Equal, false);
        query.Criteria.AddCondition("teamtype", ConditionOperator.In, new object[] { 0, 1 });

        return service.RetrieveMultiple(query).Entities
            .Where(e => e.GetAttributeValue<EntityReference>("businessunitid") is not null)
            .Select(e => new TeamData(
                e.Id,
                e.GetAttributeValue<string>("name") ?? e.Id.ToString(),
                e.GetAttributeValue<EntityReference>("businessunitid")!.Id,
                e.GetAttributeValue<OptionSetValue>("teamtype")?.Value ?? 0,
                teamRoleMap.TryGetValue(e.Id, out var roles) ? [.. roles] : []))
            .ToList();
    }
}
