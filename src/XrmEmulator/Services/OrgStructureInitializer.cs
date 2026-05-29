using System.Text.Json;
using System.Text.Json.Serialization;
using DG.Tools.XrmMockup;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.Services;

/// <summary>
/// Hosted service that reads OrgStructure.json files written by MetadataSync and recreates
/// the live environment's BU hierarchy and team structure in XrmMockup at startup.
/// Runs after snapshot restore, skipping any entities that already exist.
/// </summary>
public sealed class OrgStructureInitializer(
    XrmMockup365 xrmMockup,
    IOrganizationServiceAsync orgService,
    IConfiguration configuration,
    ILogger<OrgStructureInitializer> logger) : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var solutionExportsPath = configuration.GetValue<string>("SolutionExports:Path");
        if (string.IsNullOrEmpty(solutionExportsPath)) return Task.CompletedTask;

        var merged = LoadAndMerge(solutionExportsPath);
        if (merged is null) return Task.CompletedTask;

        Initialize(merged);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private OrgStructureData? LoadAndMerge(string solutionExportsPath)
    {
        if (!Directory.Exists(solutionExportsPath)) return null;

        var allFiles = Directory.GetDirectories(solutionExportsPath)
            .Select(d => Path.Combine(d, "OrgStructure.json"))
            .Where(File.Exists)
            .ToList();

        if (allFiles.Count == 0) return null;

        var buById = new Dictionary<Guid, BuData>();
        var teamById = new Dictionary<Guid, TeamData>();

        foreach (var file in allFiles)
        {
            OrgStructureData? data;
            try
            {
                data = JsonSerializer.Deserialize<OrgStructureData>(File.ReadAllText(file), JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not read {File}: {Message}", file, ex.Message);
                continue;
            }

            if (data is null) continue;

            foreach (var bu in data.BusinessUnits)
                buById.TryAdd(bu.Id, bu);

            foreach (var team in data.Teams)
                teamById.TryAdd(team.Id, team);
        }

        return buById.Count == 0 && teamById.Count == 0
            ? null
            : new OrgStructureData([.. buById.Values], [.. teamById.Values]);
    }

    private void Initialize(OrgStructureData data)
    {
        var rootId = xrmMockup.RootBusinessUnit.Id;

        // Create sub-BUs in topological order (parents before children)
        CreateBusinessUnits(data.BusinessUnits, rootId);

        // Create non-default teams
        CreateTeams(data.Teams);
    }

    private void CreateBusinessUnits(IList<BuData> allBus, Guid rootId)
    {
        var remaining = allBus.Where(b => b.Id != rootId).ToList();
        var created = new HashSet<Guid> { rootId };

        while (remaining.Count > 0)
        {
            var progress = false;
            for (var i = remaining.Count - 1; i >= 0; i--)
            {
                var bu = remaining[i];
                if (bu.ParentId is null || !created.Contains(bu.ParentId.Value)) continue;

                if (!EntityExists("businessunit", bu.Id))
                {
                    try
                    {
                        var entity = new Entity("businessunit", bu.Id)
                        {
                            ["name"] = bu.Name,
                            ["parentbusinessunitid"] = new EntityReference("businessunit", bu.ParentId.Value)
                        };
                        orgService.Create(entity);
                        logger.LogDebug("Created business unit {Name} ({Id})", bu.Name, bu.Id);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Could not create BU {Name}: {Message}", bu.Name, ex.Message);
                    }
                }

                created.Add(bu.Id);
                remaining.RemoveAt(i);
                progress = true;
            }

            if (!progress)
            {
                foreach (var orphan in remaining)
                    logger.LogWarning("BU {Name} ({Id}) skipped — parent {ParentId} not found", orphan.Name, orphan.Id, orphan.ParentId);
                break;
            }
        }
    }

    private void CreateTeams(IList<TeamData> teams)
    {
        foreach (var team in teams)
        {
            if (EntityExists("team", team.Id)) continue;

            try
            {
                var entity = new Entity("team", team.Id)
                {
                    ["name"] = team.Name,
                    ["businessunitid"] = new EntityReference("businessunit", team.BusinessUnitId),
                    ["teamtype"] = new OptionSetValue(team.TeamType)
                };

                orgService.Create(entity);

                if (team.SecurityRoleIds.Length > 0)
                    xrmMockup.AddSecurityRolesToPrinciple(
                        new EntityReference("team", team.Id),
                        team.SecurityRoleIds);

                logger.LogDebug("Created team {Name} ({Id})", team.Name, team.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not create team {Name}: {Message}", team.Name, ex.Message);
            }
        }
    }

    private bool EntityExists(string logicalName, Guid id)
    {
        try
        {
            orgService.Retrieve(logicalName, id, new ColumnSet(false));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── local JSON model (mirrors MetadataSync's OrgStructureData) ──────────

    private sealed record OrgStructureData(
        List<BuData> BusinessUnits,
        List<TeamData> Teams);

    private sealed record BuData(Guid Id, string Name, Guid? ParentId);

    private sealed record TeamData(
        Guid Id,
        string Name,
        Guid BusinessUnitId,
        int TeamType,
        Guid[] SecurityRoleIds);
}
