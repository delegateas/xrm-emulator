using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace XrmEmulator.DataverseFakeApi.Controllers;

/// <summary>
/// Represents health controller.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Basic health check endpoint.
    /// </summary>
    /// <returns>An OK result with status and timestamp information.</returns>
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Detailed health check with system information.
    /// </summary>
    /// <returns>An OK result with detailed health and system information.</returns>
    [HttpGet("detailed")]
    public IActionResult GetDetailed()
    {
        var healthInfo = new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            system = new
            {
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                machine_name = Environment.MachineName,
                process_id = Environment.ProcessId,
                working_set_mb = GC.GetTotalMemory(false) / (1024 * 1024),
                uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime
            },
            version = new
            {
                api_version = "1.0.0",
                dotnet_version = Environment.Version.ToString(),
                framework = "ASP.NET Core"
            }
        };

        return Ok(healthInfo);
    }

    /// <summary>
    /// Simple ping endpoint.
    /// </summary>
    /// <returns>An OK result with a pong message and timestamp.</returns>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { message = "pong", timestamp = DateTime.UtcNow });
    }
}