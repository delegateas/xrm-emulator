namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Full snapshot of the organisation's BU hierarchy and teams, written to OrgStructure.json
/// by MetadataSync and read by the XRM emulator at startup to recreate the live structure.
/// </summary>
public record OrgStructureData(
    List<BusinessUnitData> BusinessUnits,
    List<TeamData> Teams);

public record BusinessUnitData(
    Guid Id,
    string Name,
    Guid? ParentId);

/// <param name="TeamType">0 = owner, 1 = access (template-based)</param>
public record TeamData(
    Guid Id,
    string Name,
    Guid BusinessUnitId,
    int TeamType,
    Guid[] SecurityRoleIds);
