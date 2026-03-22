using System.Diagnostics;

namespace XrmEmulator.MetadataSync.Git;

public static class GitHelper
{
    public static bool IsGitAvailable()
    {
        try
        {
            var (exitCode, _, _) = Run("--version", Directory.GetCurrentDirectory());
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsGitRepo(string directory)
    {
        return Directory.Exists(Path.Combine(directory, ".git"));
    }

    public static bool HasCommits(string directory)
    {
        var (exitCode, _, _) = Run("rev-parse HEAD", directory);
        return exitCode == 0;
    }

    /// <summary>
    /// Retry the initial commit for a partially-initialized repo (.git/ exists but no commits).
    /// </summary>
    public static void CompleteInit(string directory)
    {
        // Ensure .gitignore exists
        var gitignore = Path.Combine(directory, ".gitignore");
        if (!File.Exists(gitignore))
            File.WriteAllText(gitignore, "# Exclude solution zip files\n*.zip\n");

        Run("add -A", directory);
        var (commitExit, _, commitErr) = Run("commit -m \"Initial solution export snapshot\" --allow-empty", directory);
        if (commitExit != 0)
            throw new InvalidOperationException($"git initial commit failed: {commitErr}");
    }

    public static void Init(string directory)
    {
        Directory.CreateDirectory(directory);

        var (exitCode, _, stderr) = Run("init", directory);
        if (exitCode != 0)
            throw new InvalidOperationException($"git init failed: {stderr}");

        // Write .gitignore excluding the state directory
        var gitignore = Path.Combine(directory, ".gitignore");
        File.WriteAllText(gitignore, "# Exclude solution zip files\n*.zip\n");

        // Initial commit
        Run("add -A", directory);
        var (commitExit, _, commitErr) = Run("commit -m \"Initial solution export snapshot\" --allow-empty", directory);
        if (commitExit != 0)
            throw new InvalidOperationException($"git initial commit failed: {commitErr}");
    }

    public static bool CommitAll(string directory, string message)
    {
        Run("add -A", directory);

        // Check if there are staged changes
        var (statusExit, statusOut, _) = Run("status --porcelain", directory);
        if (statusExit != 0 || string.IsNullOrWhiteSpace(statusOut))
            return false; // Nothing to commit

        var (commitExit, _, commitErr) = Run($"commit -m \"{message.Replace("\"", "\\\"")}\"", directory);
        return commitExit == 0;
    }

    /// <summary>
    /// Stage and commit specific files only, leaving other changes untouched.
    /// </summary>
    public static bool CommitFiles(string directory, string[] relativePaths, string message)
    {
        foreach (var path in relativePaths)
        {
            var escaped = path.Replace("\"", "\\\"");
            Run($"add \"{escaped}\"", directory);
        }

        // Check if there are staged changes
        var (statusExit, statusOut, _) = Run("diff --cached --name-only", directory);
        if (statusExit != 0 || string.IsNullOrWhiteSpace(statusOut))
            return false; // Nothing staged

        var (commitExit, _, _) = Run($"commit -m \"{message.Replace("\"", "\\\"")}\"", directory);
        return commitExit == 0;
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout, stderr);
    }
}
