using DG.Tools.XrmMockup;
using Microsoft.Extensions.Options;

namespace XrmEmulator.DataverseFakeApi.Services;

/// <summary>
/// Background service that periodically saves XrmMockup snapshots to disk
/// </summary>
public class SnapshotService : BackgroundService, ISnapshotService
{
    private readonly XrmMockup365 _xrmMockup;
    private readonly SnapshotOptions _options;
    private readonly ILogger<SnapshotService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private int _isDirty = 0; // 0 = clean, 1 = dirty
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public SnapshotService(
        XrmMockup365 xrmMockup,
        IOptions<SnapshotOptions> options,
        ILogger<SnapshotService> logger,
        IHostApplicationLifetime applicationLifetime)
    {
        _xrmMockup = xrmMockup;
        _options = options.Value;
        _logger = logger;
        _applicationLifetime = applicationLifetime;
    }

    /// <summary>
    /// Mark the snapshot as dirty (requiring save)
    /// </summary>
    public void MarkDirty()
    {
        Interlocked.Exchange(ref _isDirty, 1);
    }

    /// <summary>
    /// Check if snapshot is marked as dirty
    /// </summary>
    private bool IsDirty()
    {
        return Interlocked.CompareExchange(ref _isDirty, 0, 0) == 1;
    }

    /// <summary>
    /// Clear the dirty flag
    /// </summary>
    private void ClearDirty()
    {
        Interlocked.Exchange(ref _isDirty, 0);
    }

    /// <summary>
    /// Force an immediate snapshot save
    /// </summary>
    public async Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Snapshot persistence is disabled");
            return;
        }

        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            await SaveSnapshotAsync(cancellationToken);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Check if a snapshot file exists
    /// </summary>
    public bool SnapshotExists()
    {
        return File.Exists(_options.FilePath);
    }

    /// <summary>
    /// Delete the snapshot file
    /// </summary>
    public void DeleteSnapshot()
    {
        if (File.Exists(_options.FilePath))
        {
            File.Delete(_options.FilePath);
            _logger.LogInformation("Deleted snapshot file at {FilePath}", _options.FilePath);
        }
    }

    /// <summary>
    /// Restore snapshot on startup
    /// </summary>
    public void RestoreSnapshot()
    {
        if (!_options.Enabled || !_options.RestoreOnStartup)
        {
            _logger.LogDebug("Snapshot restore is disabled");
            return;
        }

        if (!File.Exists(_options.FilePath))
        {
            _logger.LogInformation("No snapshot file found at {FilePath}, starting with empty database", _options.FilePath);
            return;
        }

        try
        {
            _logger.LogInformation("Restoring snapshot from {FilePath}", _options.FilePath);
            var startTime = DateTime.UtcNow;

            _xrmMockup.RestoreZipSnapshot(_options.FilePath);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Successfully restored snapshot from {FilePath} in {DurationMs}ms",
                _options.FilePath, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore snapshot from {FilePath}. Starting with empty database.", _options.FilePath);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Snapshot persistence is disabled");
            return;
        }

        _logger.LogInformation(
            "Snapshot service started. Save interval: {IntervalSeconds}s, File: {FilePath}",
            _options.SaveIntervalSeconds,
            _options.FilePath);

        // Register shutdown handler
        if (_options.SaveOnShutdown)
        {
            _applicationLifetime.ApplicationStopping.Register(OnShutdown);
        }

        // Periodic save loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.SaveIntervalSeconds), stoppingToken);

                if (IsDirty())
                {
                    await _saveLock.WaitAsync(stoppingToken);
                    try
                    {
                        await SaveSnapshotAsync(stoppingToken);
                        ClearDirty();
                    }
                    finally
                    {
                        _saveLock.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in snapshot service periodic save");
            }
        }

        _logger.LogInformation("Snapshot service stopped");
    }

    private void OnShutdown()
    {
        if (IsDirty())
        {
            _logger.LogInformation("Application shutting down, saving final snapshot");
            try
            {
                // Synchronous save on shutdown
                _saveLock.Wait();
                try
                {
                    SaveSnapshotSync();
                    _logger.LogInformation("Final snapshot saved successfully on shutdown");
                }
                finally
                {
                    _saveLock.Release();
                }
            }
            catch (Exception ex)
            {
                // Log but don't rethrow - we don't want to prevent shutdown
                _logger.LogError(ex, "Failed to save snapshot on shutdown. Data may be lost.");
            }
        }
        else
        {
            _logger.LogDebug("No changes to snapshot on shutdown, skipping save");
        }
    }

    private async Task SaveSnapshotAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() => SaveSnapshotSync(), cancellationToken);
    }

    private void SaveSnapshotSync()
    {
        const int maxRetries = 3;
        const int retryDelayMs = 100;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_options.FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _logger.LogDebug("Saving snapshot to {FilePath} (attempt {Attempt}/{MaxRetries})",
                    _options.FilePath, attempt, maxRetries);
                var startTime = DateTime.UtcNow;

                _xrmMockup.TakeZipSnapshot(_options.FilePath);

                var duration = DateTime.UtcNow - startTime;
                var fileSize = new FileInfo(_options.FilePath).Length;

                _logger.LogInformation(
                    "Snapshot saved to {FilePath} in {DurationMs}ms (size: {SizeKB} KB)",
                    _options.FilePath,
                    duration.TotalMilliseconds,
                    fileSize / 1024);

                return; // Success
            }
            catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
            {
                // XrmMockup concurrency issue - database was modified during snapshot serialization
                if (attempt < maxRetries)
                {
                    _logger.LogWarning(
                        "Snapshot save failed due to concurrent modification (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms...",
                        attempt, maxRetries, retryDelayMs);
                    Thread.Sleep(retryDelayMs * attempt); // Exponential backoff
                }
                else
                {
                    _logger.LogWarning(ex,
                        "Failed to save snapshot after {MaxRetries} attempts due to concurrent modifications. Will retry on next interval.",
                        maxRetries);
                    // Don't throw - just skip this save and try again on next interval
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save snapshot to {FilePath}", _options.FilePath);
                throw;
            }
        }
    }
}
