using System.Diagnostics;
using System.Reflection;
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

        OrganizationResponse rawResponse;
        try
        {
            rawResponse = service.Execute(request);
        }
        catch (Exception ex)
        {
            // Try to extract detailed error from HTTP response body
            var detail = ExtractHttpResponseDetail(ex);
            if (detail != null)
                throw new InvalidOperationException(
                    $"Solution export failed for '{solutionUniqueName}': {detail}", ex);
            throw;
        }

        var response = (ExportSolutionResponse)rawResponse;
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

    /// <summary>
    /// Extracts the HTTP response body from Dataverse SDK exceptions.
    /// The SDK wraps HTTP errors in HttpOperationException which has a Response.Content property
    /// accessible via reflection (since the concrete type varies by SDK version).
    /// </summary>
    private static string? ExtractHttpResponseDetail(Exception ex)
    {
        // Walk the exception chain looking for HTTP response details
        var current = ex;
        while (current != null)
        {
            var typeName = current.GetType().Name;

            // HttpOperationException from Microsoft.Rest.ClientRuntime
            if (typeName.Contains("HttpOperation", StringComparison.OrdinalIgnoreCase))
            {
                // Try Response.Content via reflection
                var responseProp = current.GetType().GetProperty("Response",
                    BindingFlags.Public | BindingFlags.Instance);
                if (responseProp?.GetValue(current) is { } response)
                {
                    var contentProp = response.GetType().GetProperty("Content",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (contentProp?.GetValue(response) is string content && !string.IsNullOrWhiteSpace(content))
                        return content;
                }
            }

            // FaultException<OrganizationServiceFault>
            if (typeName.Contains("FaultException", StringComparison.OrdinalIgnoreCase))
            {
                var detailProp = current.GetType().GetProperty("Detail",
                    BindingFlags.Public | BindingFlags.Instance);
                if (detailProp?.GetValue(current) is OrganizationServiceFault fault)
                    return fault.Message + (fault.InnerFault != null ? $" | Inner: {fault.InnerFault.Message}" : "");
            }

            current = current.InnerException;
        }

        return null;
    }
}
