using DG.Tools.XrmMockup;
using Microsoft.AspNetCore.Mvc;
using XrmEmulator.Services;

namespace XrmEmulator.Controllers;

/// <summary>
/// Controller for managing XRM Mockup snapshots (export/import XRM state).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SnapshotController : ControllerBase
{
    private readonly XrmMockup365 _xrmMockup;
    private readonly ISnapshotService _snapshotService;
    private readonly ILogger<SnapshotController> _logger;

    // Thread-safety: Only one restore operation at a time (XrmMockup is singleton)
    private static readonly SemaphoreSlim _restoreLock = new(1, 1);

    public SnapshotController(
        XrmMockup365 xrmMockup,
        ISnapshotService snapshotService,
        ILogger<SnapshotController> logger)
    {
        _xrmMockup = xrmMockup;
        _snapshotService = snapshotService;
        _logger = logger;
    }

    /// <summary>
    /// Download current XRM snapshot as ZIP file.
    /// </summary>
    /// <returns>ZIP file stream containing XRM state</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadSnapshot(CancellationToken cancellationToken)
    {
        try
        {
            // Save current state to temporary file
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"xrm-snapshot-{Guid.NewGuid()}.zip");

            _logger.LogInformation("Creating snapshot at {TempFilePath}", tempFilePath);

            await Task.Run(() => _xrmMockup.TakeZipSnapshot(tempFilePath), cancellationToken);

            if (!System.IO.File.Exists(tempFilePath))
            {
                _logger.LogWarning("Snapshot file not found after creation: {TempFilePath}", tempFilePath);
                return NotFound(new { error = "Snapshot file was not created" });
            }

            var fileInfo = new FileInfo(tempFilePath);
            _logger.LogInformation("Snapshot created successfully. Size: {SizeKB} KB", fileInfo.Length / 1024);

            // Stream file and delete after completion
            var stream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose);
            return File(stream, "application/zip", "xrm-snapshot.zip");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create snapshot");
            return StatusCode(500, new { error = "Failed to create snapshot", message = ex.Message });
        }
    }

    /// <summary>
    /// Restore XRM state from uploaded ZIP snapshot.
    /// </summary>
    /// <param name="file">ZIP snapshot file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success or error message</returns>
    [HttpPost("restore")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [RequestSizeLimit(500_000_000)] // 500 MB limit
    public async Task<IActionResult> RestoreSnapshot(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded or file is empty" });
        }

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "File must be a ZIP archive" });
        }

        // Ensure only one restore operation at a time
        if (!await _restoreLock.WaitAsync(0, cancellationToken))
        {
            return StatusCode(409, new { error = "Another restore operation is already in progress" });
        }

        try
        {
            _logger.LogInformation("Starting snapshot restore from uploaded file: {FileName} ({SizeKB} KB)",
                file.FileName, file.Length / 1024);

            // Save uploaded file to temp location
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"xrm-restore-{Guid.NewGuid()}.zip");

            await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
            {
                await file.CopyToAsync(fileStream, cancellationToken);
            }

            _logger.LogInformation("Uploaded file saved to {TempFilePath}, restoring XrmMockup state...", tempFilePath);

            // Restore from temp file
            await Task.Run(() =>
            {
                _xrmMockup.RestoreZipSnapshot(tempFilePath);
                _logger.LogInformation("XrmMockup state restored successfully from {TempFilePath}", tempFilePath);
            }, cancellationToken);

            // Delete temp file
            System.IO.File.Delete(tempFilePath);

            // Delete persistent snapshot file (if any) to avoid confusion
            _snapshotService.DeleteSnapshot();

            // Mark as dirty so the new state gets saved periodically
            _snapshotService.MarkDirty();

            _logger.LogInformation("Snapshot restore completed successfully");

            return Ok(new
            {
                success = true,
                message = "Snapshot restored successfully",
                timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore snapshot");
            return StatusCode(500, new { error = "Failed to restore snapshot", message = ex.Message });
        }
        finally
        {
            _restoreLock.Release();
        }
    }

    /// <summary>
    /// Restore XRM state from file path (local development only).
    /// </summary>
    /// <param name="request">Request with file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success or error message</returns>
    [HttpPost("restore-from-file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RestoreFromFile([FromBody] RestoreFromFileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return BadRequest(new { error = "FilePath is required" });
        }

        if (!System.IO.File.Exists(request.FilePath))
        {
            return NotFound(new { error = $"File not found: {request.FilePath}" });
        }

        // Ensure only one restore operation at a time
        if (!await _restoreLock.WaitAsync(0, cancellationToken))
        {
            return StatusCode(409, new { error = "Another restore operation is already in progress" });
        }

        try
        {
            _logger.LogInformation("Starting snapshot restore from file path: {FilePath}", request.FilePath);

            await Task.Run(() =>
            {
                _xrmMockup.RestoreZipSnapshot(request.FilePath);
                _logger.LogInformation("XrmMockup state restored successfully from {FilePath}", request.FilePath);
            }, cancellationToken);

            // Delete persistent snapshot file (if any) to avoid confusion
            _snapshotService.DeleteSnapshot();

            // Mark as dirty so the new state gets saved periodically
            _snapshotService.MarkDirty();

            _logger.LogInformation("Snapshot restore from file completed successfully");

            return Ok(new
            {
                success = true,
                message = "Snapshot restored successfully from file",
                filePath = request.FilePath,
                timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore snapshot from file: {FilePath}", request.FilePath);
            return StatusCode(500, new { error = "Failed to restore snapshot", message = ex.Message });
        }
        finally
        {
            _restoreLock.Release();
        }
    }

    /// <summary>
    /// Get snapshot status (exists, size, timestamp).
    /// </summary>
    /// <returns>Snapshot status information</returns>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var exists = _snapshotService.SnapshotExists();

        if (!exists)
        {
            return Ok(new
            {
                exists = false,
                message = "No persistent snapshot file found"
            });
        }

        // Get file info if exists
        try
        {
            var filePath = "xrm-emulator-snapshot.zip"; // Default path from SnapshotOptions
            var fileInfo = new FileInfo(filePath);

            return Ok(new
            {
                exists = true,
                filePath,
                sizeBytes = fileInfo.Length,
                sizeKB = fileInfo.Length / 1024,
                lastModified = fileInfo.LastWriteTimeUtc,
                created = fileInfo.CreationTimeUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get snapshot file info");
            return Ok(new
            {
                exists = true,
                error = "Failed to read file info",
                message = ex.Message
            });
        }
    }
}

/// <summary>
/// Request model for restore-from-file endpoint.
/// </summary>
public record RestoreFromFileRequest
{
    /// <summary>
    /// File path to the snapshot ZIP file.
    /// </summary>
    public required string FilePath { get; init; }
}
