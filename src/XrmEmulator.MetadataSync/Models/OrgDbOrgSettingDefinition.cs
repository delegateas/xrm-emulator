namespace XrmEmulator.MetadataSync.Models;

/// <summary>
/// Pending file model for staging a change to the org-wide `orgdborgsettings` XML blob
/// on the singleton `organization` record.
/// Pending file extension: *.orgdborgsetting.json
/// Pending folder: _pending/OrgDbOrgSettings/
/// </summary>
public enum OrgDbOrgSettingMode
{
    SetValue,
    RestoreBlob
}

public record OrgDbOrgSettingDefinition
{
    public required OrgDbOrgSettingMode Mode { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string EnvironmentUrl { get; init; }

    /// <summary>
    /// Full orgdborgsettings blob as it existed live at staging time.
    /// Doubles as the freshness/concurrency baseline AND, once archived to
    /// _committed/ by the normal commit flow, the permanent backup for rollback.
    /// </summary>
    public required string BaselineXml { get; init; }

    public required DateTimeOffset StagedAt { get; init; }

    // Mode = SetValue
    public string? SettingName { get; init; }
    public string? NewValue { get; init; }
    public string? PreviousValueForDisplay { get; init; }

    // Mode = RestoreBlob (rollback)
    public string? RestoreXml { get; init; }
    public string? RollbackSourceFile { get; init; }
}
