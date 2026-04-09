using System.Diagnostics;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class PcfControlWriter
{
    /// <summary>
    /// Deploy a PCF control by building the project with npm + MSBuild (pac-compatible),
    /// then importing the resulting solution zip via ImportSolutionRequest.
    ///
    /// Build steps (no pac auth required):
    ///   1. npm run build          → produces bundle.js + ControlManifest.xml in out/controls/
    ///   2. dotnet build *.cdsproj → packs into a Dataverse solution zip
    ///
    /// The cdsproj is created by "pac pcf push" on first run and lives in obj/PowerAppsToolsTemp_{prefix}/.
    /// </summary>
    public static Guid Upsert(IOrganizationService service, PcfControlDefinition def, string baseDir,
        Action<string>? log = null)
    {
        var projectDir = Path.IsPathRooted(def.ProjectPath)
            ? def.ProjectPath
            : Path.GetFullPath(Path.Combine(baseDir, def.ProjectPath));

        if (!Directory.Exists(projectDir))
            throw new DirectoryNotFoundException($"PCF project directory not found: {projectDir}");

        var prefix = def.PublisherPrefix;

        // Step 1: npm run build
        log?.Invoke("  Building PCF control (npm run build)...");
        RunProcess("npm", "run build", projectDir, log);

        // Step 2: Build the cdsproj to produce the solution zip
        var cdsProj = Path.Combine(projectDir, $"obj/PowerAppsToolsTemp_{prefix}/PowerAppsToolsTemp_{prefix}.cdsproj");
        if (!File.Exists(cdsProj))
            throw new FileNotFoundException(
                $"cdsproj not found at: {cdsProj}. Run 'pac pcf push --publisher-prefix {prefix}' once to scaffold it.");

        log?.Invoke("  Packing solution zip (dotnet build cdsproj)...");
        RunProcess("dotnet", $"build \"{cdsProj}\"", projectDir, log);

        // Step 3: Find the output zip
        var zipPath = Path.Combine(projectDir, $"obj/PowerAppsToolsTemp_{prefix}/bin/Debug/PowerAppsToolsTemp_{prefix}.zip");
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Solution zip not found at: {zipPath}");

        var solutionZip = File.ReadAllBytes(zipPath);
        log?.Invoke($"  Solution zip: {solutionZip.Length / 1024} KB");

        // Step 4: Import
        log?.Invoke("  Importing solution...");
        var importRequest = new ImportSolutionRequest
        {
            CustomizationFile = solutionZip,
            OverwriteUnmanagedCustomizations = true,
            PublishWorkflows = false,
        };
        service.Execute(importRequest);
        log?.Invoke("  Solution imported successfully.");

        // Step 5: Publish
        log?.Invoke("  Publishing customizations...");
        service.Execute(new PublishAllXmlRequest());
        log?.Invoke("  Published.");

        return Guid.Empty;
    }

    private static void RunProcess(string fileName, string arguments, string workingDirectory, Action<string>? log)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {fileName} {arguments}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var output = string.IsNullOrEmpty(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"'{fileName} {arguments}' failed (exit code {process.ExitCode}):\n{output}");
        }
    }
}
