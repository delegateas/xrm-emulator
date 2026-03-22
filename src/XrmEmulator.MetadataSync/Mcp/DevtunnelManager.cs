using System.Diagnostics;

namespace XrmEmulator.MetadataSync.Mcp;

public class DevtunnelManager : IDisposable
{
    private Process? _hostProcess;
    private string? _publicUrl;

    /// <summary>
    /// Create a new devtunnel and return the tunnel ID.
    /// </summary>
    public static string CreateTunnel()
    {
        var (exitCode, stdout, stderr) = RunCommand("devtunnel", "create --allow-anonymous");
        if (exitCode != 0)
            throw new InvalidOperationException($"devtunnel create failed: {stderr}");

        // Parse tunnel ID from output — typically "Tunnel ID: <id>" or just the ID on a line
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("Tunnel ID:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(':', 2);
                if (parts.Length == 2) return parts[1].Trim();
            }
            // Some versions output just the ID
            if (!trimmed.Contains(' ') && trimmed.Length > 5)
                return trimmed;
        }

        throw new InvalidOperationException($"Could not parse tunnel ID from output:\n{stdout}");
    }

    /// <summary>
    /// Add a port to an existing tunnel.
    /// </summary>
    public static void AddPort(string tunnelId, int port)
    {
        var (exitCode, _, stderr) = RunCommand("devtunnel", $"port create {tunnelId} -p {port}");
        if (exitCode != 0)
            throw new InvalidOperationException($"devtunnel port create failed: {stderr}");
    }

    /// <summary>
    /// Get the public URL for a tunnel.
    /// </summary>
    public static string GetPublicUrl(string tunnelId)
    {
        var (exitCode, stdout, stderr) = RunCommand("devtunnel", $"show {tunnelId}");
        if (exitCode != 0)
            throw new InvalidOperationException($"devtunnel show failed: {stderr}");

        // Parse URL from output
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("https://", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
                var url = trimmed[idx..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                return url.TrimEnd('/');
            }
        }

        throw new InvalidOperationException($"Could not parse public URL from devtunnel show output:\n{stdout}");
    }

    /// <summary>
    /// Check if the devtunnel CLI is available and logged in.
    /// </summary>
    public static bool IsLoggedIn()
    {
        try
        {
            var (exitCode, stdout, _) = RunCommand("devtunnel", "user show");
            return exitCode == 0 && !stdout.Contains("not logged in", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if a tunnel still exists (may have expired after 30 days).
    /// </summary>
    public static bool TunnelExists(string tunnelId)
    {
        var (exitCode, _, _) = RunCommand("devtunnel", $"show {tunnelId}");
        return exitCode == 0;
    }

    /// <summary>
    /// Start hosting the tunnel as a background process. Blocks until the tunnel is ready.
    /// </summary>
    public async Task StartHostingAsync(string tunnelId, int port, CancellationToken ct = default)
    {
        _hostProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "devtunnel",
                Arguments = $"host {tunnelId} --allow-anonymous",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _hostProcess.Start();

        // Wait for "ready" or URL in output (with timeout)
        var readyTcs = new TaskCompletionSource<bool>();
        _hostProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            if (e.Data.Contains("https://", StringComparison.OrdinalIgnoreCase)
                || e.Data.Contains("ready", StringComparison.OrdinalIgnoreCase)
                || e.Data.Contains("Connected", StringComparison.OrdinalIgnoreCase))
            {
                // Extract URL if present
                var line = e.Data;
                var httpsIdx = line.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
                if (httpsIdx >= 0)
                    _publicUrl = line[httpsIdx..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('/');

                readyTcs.TrySetResult(true);
            }
        };
        _hostProcess.BeginOutputReadLine();
        _hostProcess.BeginErrorReadLine();

        // Wait up to 30 seconds for the tunnel to be ready
        var timeout = Task.Delay(TimeSpan.FromSeconds(30), ct);
        var completed = await Task.WhenAny(readyTcs.Task, timeout);

        if (completed == timeout)
        {
            // Try to get the URL via show command as fallback
            _publicUrl = GetPublicUrl(tunnelId);
        }
    }

    public string? PublicUrl => _publicUrl;

    public void Stop()
    {
        if (_hostProcess != null && !_hostProcess.HasExited)
        {
            try { _hostProcess.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }
    }

    public void Dispose()
    {
        Stop();
        _hostProcess?.Dispose();
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCommand(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(30));

        return (process.ExitCode, stdout, stderr);
    }
}
