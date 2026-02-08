namespace XrmEmulator.Services;

/// <summary>
/// Service for managing XrmMockup snapshot persistence
/// </summary>
public interface ISnapshotService
{
    /// <summary>
    /// Mark the snapshot as dirty (requiring save)
    /// </summary>
    void MarkDirty();

    /// <summary>
    /// Force an immediate snapshot save
    /// </summary>
    Task SaveNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a snapshot file exists
    /// </summary>
    bool SnapshotExists();

    /// <summary>
    /// Delete the snapshot file
    /// </summary>
    void DeleteSnapshot();
}
