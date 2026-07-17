using System.Text.Json;
using DG.Tools.XrmMockup;
using Microsoft.Extensions.Options;

namespace XrmEmulator.Services;

public class PluginExecutionHistoryOptions
{
    public string FilePath { get; set; } = "./xrm-emulator-plugin-executions.jsonl";
    public int MaxEntries { get; set; } = 500;
}

public class PluginExecutionHistoryEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string PluginTypeName { get; set; } = "";
    public string EntityLogicalName { get; set; } = "";
    public Guid EntityId { get; set; }
    public string MessageName { get; set; } = "";
    public string Stage { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Subscribes to <see cref="PluginExecutionAudit.Executed"/> and persists a rolling window of plugin
/// executions as JSON lines, so the `/plugins` dev-tool route (CrmController) can show what actually
/// ran — across process restarts too, as long as the snapshot data directory is reused.
/// </summary>
public class PluginExecutionHistoryStore
{
    private readonly PluginExecutionHistoryOptions _options;
    private readonly ILogger<PluginExecutionHistoryStore> _logger;
    private readonly object _lock = new();
    private List<PluginExecutionHistoryEntry> _entries;

    public PluginExecutionHistoryStore(IOptions<PluginExecutionHistoryOptions> options, ILogger<PluginExecutionHistoryStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _entries = Load();
        PluginExecutionAudit.Executed += OnExecuted;
    }

    private List<PluginExecutionHistoryEntry> Load()
    {
        if (!File.Exists(_options.FilePath))
            return [];

        try
        {
            var entries = new List<PluginExecutionHistoryEntry>();
            foreach (var line in File.ReadAllLines(_options.FilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = JsonSerializer.Deserialize<PluginExecutionHistoryEntry>(line);
                if (entry != null)
                    entries.Add(entry);
            }
            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load plugin execution history from {Path}", _options.FilePath);
            return [];
        }
    }

    private void OnExecuted(PluginExecutionRecord record)
    {
        var entry = new PluginExecutionHistoryEntry
        {
            Timestamp = record.Timestamp,
            PluginTypeName = record.PluginTypeName,
            EntityLogicalName = record.EntityLogicalName,
            EntityId = record.EntityId,
            MessageName = record.MessageName,
            Stage = record.Stage,
            Success = record.Success,
            Error = record.Error,
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
                _logger.LogWarning(ex, "Failed to persist plugin execution history to {Path}", _options.FilePath);
            }
        }
    }

    public IReadOnlyList<PluginExecutionHistoryEntry> GetForEntity(string entityLogicalName, Guid? recordId = null)
    {
        lock (_lock)
        {
            return _entries
                .Where(e => e.EntityLogicalName == entityLogicalName)
                .Where(e => recordId == null || e.EntityId == recordId)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
    }
}
