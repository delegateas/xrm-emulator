using System.Text.RegularExpressions;

namespace XrmEmulator.MetadataSync.Commit;

public static class PendingVariableResolver
{
    private static readonly Regex VariablePattern =
        new(@"\{\{_pending/(?<path>[^#}]+)#(?<prop>[^}]+)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Extract dependency paths from content (the file paths referenced by variables).
    /// </summary>
    public static HashSet<string> ExtractDependencies(string content)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in VariablePattern.Matches(content))
            deps.Add(NormalizePath(match.Groups["path"].Value));
        return deps;
    }

    /// <summary>
    /// Extract dependency paths from a file on disk.
    /// </summary>
    public static HashSet<string> ExtractDependenciesFromFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return ExtractDependencies(content);
    }

    /// <summary>
    /// Returns true if the file contains any pending variable references.
    /// </summary>
    public static bool HasVariables(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return VariablePattern.IsMatch(content);
    }

    /// <summary>
    /// Replace all {{_pending/...#prop}} in content with resolved values from the outputs dictionary.
    /// </summary>
    public static string ResolveVariables(
        string content,
        Dictionary<string, Dictionary<string, string>> resolvedOutputs)
    {
        return VariablePattern.Replace(content, match =>
        {
            var path = NormalizePath(match.Groups["path"].Value);
            var prop = match.Groups["prop"].Value;

            if (!resolvedOutputs.TryGetValue(path, out var outputs))
                throw new InvalidOperationException(
                    $"Unresolved variable reference: {{{{_pending/{match.Groups["path"].Value}#{prop}}}}}. " +
                    $"The referenced file has not been committed yet.");

            if (!outputs.TryGetValue(prop, out var value))
                throw new InvalidOperationException(
                    $"Unknown property '#{prop}' for {{{{_pending/{match.Groups["path"].Value}}}}}. " +
                    $"Available properties: {string.Join(", ", outputs.Keys)}");

            return value;
        });
    }

    /// <summary>
    /// Read a file, resolve all variables, and return the resolved content.
    /// </summary>
    public static string ResolveFileContent(
        string filePath,
        Dictionary<string, Dictionary<string, string>> resolvedOutputs)
    {
        var content = File.ReadAllText(filePath);
        return ResolveVariables(content, resolvedOutputs);
    }

    /// <summary>
    /// Reorder commit items so that dependencies are processed before dependents.
    /// Items without dependencies keep their original relative order.
    /// </summary>
    public static List<T> ReorderByDependencies<T>(
        string pendingDir,
        List<T> items,
        Func<T, string> getFilePath)
    {
        // Build a map: normalized relative path → item index
        var pathToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            var relPath = NormalizePath(Path.GetRelativePath(pendingDir, getFilePath(items[i])));
            pathToIndex[relPath] = i;
        }

        // Build adjacency: index → set of indices it depends on
        var dependencies = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < items.Count; i++)
        {
            var filePath = getFilePath(items[i]);
            var deps = ExtractDependenciesFromFile(filePath);
            var depIndices = new HashSet<int>();
            foreach (var dep in deps)
            {
                if (pathToIndex.TryGetValue(dep, out var depIndex))
                    depIndices.Add(depIndex);
                // If dependency is not in the commit set, ignore — it may already be committed
            }
            dependencies[i] = depIndices;
        }

        // Topological sort (Kahn's algorithm), preserving original order for ties
        var inDegree = new int[items.Count];
        var dependents = new Dictionary<int, List<int>>();
        for (int i = 0; i < items.Count; i++)
            dependents[i] = new List<int>();

        foreach (var (node, deps) in dependencies)
        {
            inDegree[node] = deps.Count;
            foreach (var dep in deps)
                dependents[dep].Add(node);
        }

        // Use a sorted set to maintain original order for items with same priority
        var queue = new SortedSet<int>();
        for (int i = 0; i < items.Count; i++)
        {
            if (inDegree[i] == 0)
                queue.Add(i);
        }

        var result = new List<T>(items.Count);
        while (queue.Count > 0)
        {
            var current = queue.Min;
            queue.Remove(current);
            result.Add(items[current]);

            foreach (var dependent in dependents[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Add(dependent);
            }
        }

        if (result.Count != items.Count)
        {
            // Find the cycle for a useful error message
            var remaining = Enumerable.Range(0, items.Count)
                .Where(i => inDegree[i] > 0)
                .Select(i => Path.GetFileName(getFilePath(items[i])));
            throw new InvalidOperationException(
                $"Circular dependency detected among pending files: {string.Join(", ", remaining)}. " +
                "Remove the circular references and try again.");
        }

        return result;
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');
}
