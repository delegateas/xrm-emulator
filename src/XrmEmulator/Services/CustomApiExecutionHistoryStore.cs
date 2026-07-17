using System.Text.Json;
using Microsoft.Extensions.Options;

namespace XrmEmulator.Services;

public class CustomApiExecutionHistoryOptions
{
    public string FilePath { get; set; } = "./xrm-emulator-customapi-executions.jsonl";
    public int MaxEntries { get; set; } = 500;
}

public class CustomApiExecutionHistoryEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string CustomApiUniqueName { get; set; } = "";
    public string PluginTypeName { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, string?> InputParameters { get; set; } = [];
    public Dictionary<string, string?> OutputParameters { get; set; } = [];
}

/// <summary>
/// Records manual Custom API trigger attempts made via the `/customapis` dev-tool route. Independent
/// of <see cref="PluginExecutionHistoryStore"/>/PluginExecutionAudit — Custom API execution never goes
/// through XrmMockup's CRUD-triggered plugin pipeline, so there's no cross-assembly event to hook into;
/// this store is written to directly by <see cref="CustomApiExecutionService"/>.
/// </summary>
public class CustomApiExecutionHistoryStore
{
    private readonly CustomApiExecutionHistoryOptions _options;
    private readonly ILogger<CustomApiExecutionHistoryStore> _logger;
    private readonly object _lock = new();
    private List<CustomApiExecutionHistoryEntry> _entries;

    public CustomApiExecutionHistoryStore(IOptions<CustomApiExecutionHistoryOptions> options, ILogger<CustomApiExecutionHistoryStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _entries = Load();
    }

    private List<CustomApiExecutionHistoryEntry> Load()
    {
        if (!File.Exists(_options.FilePath))
            return [];

        try
        {
            var entries = new List<CustomApiExecutionHistoryEntry>();
            foreach (var line in File.ReadAllLines(_options.FilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = JsonSerializer.Deserialize<CustomApiExecutionHistoryEntry>(line);
                if (entry != null)
                    entries.Add(entry);
            }
            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Custom API execution history from {Path}", _options.FilePath);
            return [];
        }
    }

    public void Record(
        string customApiUniqueName,
        string pluginTypeName,
        bool success,
        string? error,
        Dictionary<string, string?> inputParameters,
        Dictionary<string, string?> outputParameters)
    {
        var entry = new CustomApiExecutionHistoryEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            CustomApiUniqueName = customApiUniqueName,
            PluginTypeName = pluginTypeName,
            Success = success,
            Error = error,
            InputParameters = inputParameters,
            OutputParameters = outputParameters,
        };

        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > _options.MaxEntries)
                _entries = _entries.Skip(_entries.Count - _options.MaxEntries).ToList();

            try
            {
                var directory = Path.GetDirectoryName(_options.FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllLines(_options.FilePath, _entries.Select(e => JsonSerializer.Serialize(e)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist Custom API execution history to {Path}", _options.FilePath);
            }
        }
    }

    public IReadOnlyList<CustomApiExecutionHistoryEntry> GetAll()
    {
        lock (_lock)
        {
            return _entries.OrderByDescending(e => e.Timestamp).ToList();
        }
    }

    public IReadOnlyList<CustomApiExecutionHistoryEntry> GetForApi(string uniqueName)
    {
        lock (_lock)
        {
            return _entries
                .Where(e => string.Equals(e.CustomApiUniqueName, uniqueName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
    }
}
