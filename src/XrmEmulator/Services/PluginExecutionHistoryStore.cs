using System.Text.Json;
using Microsoft.Extensions.Options;
// NOTE: The live CRUD-plugin feed depends on DG.Tools.XrmMockup's PluginExecutionAudit hook, which
// only exists as a local (unpushed) patch to the XrmMockup submodule and therefore breaks the CI
// build. Disabled for now — see the commented block below to re-enable once that hook lands upstream
// (or in a fork the team controls). Custom API history (CustomApiExecutionHistoryStore) and the
// registered-step list (PluginRegistrationService) are unaffected — they need no XrmMockup change.
// using DG.Tools.XrmMockup;

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
/// Persists a rolling window of CRUD-triggered plugin executions as JSON lines, so the `/plugins`
/// dev-tool route (CrmController) can show what actually ran — across process restarts too, as long as
/// the snapshot data directory is reused. The live feed (subscribing to XrmMockup's PluginExecutionAudit
/// hook) is currently disabled; see the file header. Reads back whatever history is already on disk.
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
        // Disabled — depends on the XrmMockup PluginExecutionAudit patch (see file header).
        // PluginExecutionAudit.Executed += OnExecuted;
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

    // Disabled — re-enable together with the `PluginExecutionAudit.Executed += OnExecuted` subscription
    // above once the XrmMockup PluginExecutionAudit hook is available on CI (upstream or a team fork).
    // PluginExecutionRecord is the XrmMockup-side type raised by that hook.
    /*
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
    */

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
