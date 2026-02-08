namespace XrmEmulator.DataverseFakeApi.Services;

/// <summary>
/// Configuration options for XrmMockup snapshot persistence
/// </summary>
public class SnapshotOptions
{
    /// <summary>
    /// Enable or disable snapshot persistence
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path where snapshot file will be stored
    /// </summary>
    public string FilePath { get; set; } = "./xrm-emulator-snapshot.zip";

    /// <summary>
    /// Interval in seconds between snapshot saves (when dirty)
    /// </summary>
    public int SaveIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Whether to save snapshot on graceful shutdown
    /// </summary>
    public bool SaveOnShutdown { get; set; } = true;

    /// <summary>
    /// Whether to restore snapshot on startup
    /// </summary>
    public bool RestoreOnStartup { get; set; } = true;
}
