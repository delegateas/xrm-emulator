using System.Diagnostics;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace XrmEmulator.MetadataSync.Readers;

public static class SolutionExporter
{
    public static void Export(IOrganizationService service, string solutionUniqueName, string outputDirectory)
    {
        var exportDir = Path.Combine(outputDirectory, "SolutionExport");
        Directory.CreateDirectory(exportDir);

        var zipPath = Path.Combine(exportDir, $"{solutionUniqueName}.zip");
        var unpackDir = Path.Combine(exportDir, solutionUniqueName);

        // Clean previous unpack (stale files from renames/deletes would otherwise persist)
        // Preserve _pending/ and _committed/ which live alongside the solution folder
        if (Directory.Exists(unpackDir))
            Directory.Delete(unpackDir, recursive: true);

        // Export solution as unmanaged zip
        var request = new ExportSolutionRequest
        {
            SolutionName = solutionUniqueName,
            Managed = false
        };
        var response = (ExportSolutionResponse)service.Execute(request);
        File.WriteAllBytes(zipPath, response.ExportSolutionFile);

        // Unpack using pac CLI via dnx
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"dnx --yes Microsoft.PowerApps.CLI.Tool solution unpack -- -z \"{zipPath}\" -f \"{unpackDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start pac solution unpack process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"pac solution unpack failed with exit code {process.ExitCode}: {output}");
        }
    }
}
