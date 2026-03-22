using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using XrmEmulator.MetadataSync.Commit;
using XrmEmulator.MetadataSync.Connection;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Mcp;

public class McpServer
{
    private readonly McpConfig _config;
    private readonly string _baseDir;
    private readonly Dictionary<string, ApprovalRequest> _approvals = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _commitLock = new(1, 1);
    private readonly string _approvalsDir;
    private TeamsNotifier? _notifier;
    private DevtunnelManager? _tunnelManager;
    private CallbackServer? _callbackServer;
    private string? _callbackBaseUrl;

    public McpServer(McpConfig config, string baseDir)
    {
        _config = config;
        _baseDir = baseDir;
        _approvalsDir = McpConfig.GetApprovalsDir(baseDir);
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Restore pending approvals from disk
        RestoreApprovals();

        // Initialize Teams notifier
        _notifier = new TeamsNotifier(_config.GraphClientId, _config.GraphTenantId, _config.RefreshToken);

        // Start callback HTTP server
        var callbackPort = FindFreePort();
        var connectToCrm = CreateCrmConnector();
        var metadata = LoadConnectionMetadata();

        _callbackServer = new CallbackServer(
            callbackPort, _config.HmacSigningKey, _approvals, _approvalsDir,
            _commitLock, connectToCrm, metadata, _baseDir,
            msg => Console.Error.WriteLine($"[mcp] {msg}"));
        _callbackServer.Start();

        // Start devtunnel
        if (!string.IsNullOrEmpty(_config.DevtunnelId))
        {
            _tunnelManager = new DevtunnelManager();
            await _tunnelManager.StartHostingAsync(_config.DevtunnelId, callbackPort, ct);
            _callbackBaseUrl = _tunnelManager.PublicUrl
                ?? DevtunnelManager.GetPublicUrl(_config.DevtunnelId);
            Console.Error.WriteLine($"[mcp] Devtunnel active: {_callbackBaseUrl}");
        }
        else
        {
            _callbackBaseUrl = $"http://localhost:{callbackPort}";
            Console.Error.WriteLine($"[mcp] No devtunnel configured. Using local callback: {_callbackBaseUrl}");
        }

        Console.Error.WriteLine("[mcp] MCP server ready. Listening on stdio...");

        // Enter stdio JSON-RPC loop
        await StdioLoopAsync(ct);
    }

    private async Task StdioLoopAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break; // EOF

            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
                var id = root.TryGetProperty("id", out var idProp) ? idProp.Clone() : (JsonElement?)null;

                var response = method switch
                {
                    "initialize" => HandleInitialize(id),
                    "tools/list" => HandleToolsList(id),
                    "tools/call" => await HandleToolsCallAsync(root, id),
                    "notifications/initialized" => null, // No response needed
                    "ping" => BuildResponse(id, new { }),
                    _ => BuildErrorResponse(id, -32601, $"Method not found: {method}")
                };

                if (response != null)
                {
                    await writer.WriteLineAsync(response);
                }
            }
            catch (Exception ex)
            {
                var errorResponse = BuildErrorResponse(null, -32700, $"Parse error: {ex.Message}");
                await writer.WriteLineAsync(errorResponse);
            }
        }
    }

    private string HandleInitialize(JsonElement? id)
    {
        return BuildResponse(id, new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "metadatasync",
                version = "1.0.0"
            }
        });
    }

    private string HandleToolsList(JsonElement? id)
    {
        var tools = new object[]
        {
            new
            {
                name = "request-approval",
                description = "Request approval to commit pending CRM changes. Sends an adaptive card to Teams for human review. The agent cannot commit directly — approval is always required.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["items"] = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Optional filter: display names of items to include. Omit to include all pending items."
                        }
                    }
                }
            },
            new
            {
                name = "check-approval-status",
                description = "Check the status of a previously requested approval. Returns the current status (Pending, Approved, Rejected, Executing, Completed, Failed) and commit results if available.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["approval_id"] = new
                        {
                            type = "string",
                            description = "The approval ID returned by request-approval."
                        }
                    },
                    required = new[] { "approval_id" }
                }
            }
        };

        return BuildResponse(id, new { tools });
    }

    private async Task<string> HandleToolsCallAsync(JsonElement root, JsonElement? id)
    {
        var toolName = root.GetProperty("params").GetProperty("name").GetString();
        var arguments = root.GetProperty("params").TryGetProperty("arguments", out var args)
            ? args
            : default;

        return toolName switch
        {
            "request-approval" => await HandleRequestApprovalAsync(arguments, id),
            "check-approval-status" => HandleCheckApprovalStatus(arguments, id),
            _ => BuildErrorResponse(id, -32602, $"Unknown tool: {toolName}")
        };
    }

    private async Task<string> HandleRequestApprovalAsync(JsonElement arguments, JsonElement? id)
    {
        try
        {
            var pendingDir = Path.Combine(_baseDir, "SolutionExport", "_pending");
            var allItems = CommitPipeline.DiscoverPendingItems(pendingDir);

            if (allItems.Count == 0)
            {
                return BuildToolResult(id, false, "No pending changes found in _pending/.");
            }

            // Filter if item names specified
            List<CommitItem> selected;
            if (arguments.ValueKind != JsonValueKind.Undefined
                && arguments.TryGetProperty("items", out var itemsFilter)
                && itemsFilter.ValueKind == JsonValueKind.Array)
            {
                var filterNames = new HashSet<string>(
                    itemsFilter.EnumerateArray().Select(e => e.GetString()!),
                    StringComparer.OrdinalIgnoreCase);
                selected = allItems.Where(i => filterNames.Contains(i.DisplayName)).ToList();

                if (selected.Count == 0)
                {
                    return BuildToolResult(id, false,
                        $"No matching items found. Available: {string.Join(", ", allItems.Select(i => i.DisplayName))}");
                }
            }
            else
            {
                selected = allItems;
            }

            // Create approval
            var approvalId = Guid.NewGuid().ToString("N")[..12];
            var approval = new ApprovalRequest
            {
                ApprovalId = approvalId,
                Items = selected.Select(i => new ApprovalItemInfo(
                    i.Type.ToString(), i.DisplayName, i.FilePath)).ToList()
            };

            _approvals[approvalId] = approval;
            approval.Save(_approvalsDir);

            // Send Teams notification
            await _notifier!.SendApprovalCardAsync(
                approval, _config.ApproverEmail, _callbackBaseUrl!, _config.HmacSigningKey);

            // Update config with potentially rotated refresh token
            UpdateRefreshToken(_notifier.CurrentRefreshToken);

            var itemList = string.Join("\n", selected.Select(i => $"  - {i.DisplayName}"));
            return BuildToolResult(id, false,
                $"Approval requested. ID: {approvalId}\n" +
                $"Status: Pending\n" +
                $"Items ({selected.Count}):\n{itemList}\n\n" +
                $"An adaptive card has been sent to {_config.ApproverEmail} in Teams.\n" +
                $"Use check-approval-status with approval_id \"{approvalId}\" to monitor.");
        }
        catch (Exception ex)
        {
            return BuildToolResult(id, true, $"Failed to request approval: {ex.Message}");
        }
    }

    private string HandleCheckApprovalStatus(JsonElement arguments, JsonElement? id)
    {
        if (arguments.ValueKind == JsonValueKind.Undefined
            || !arguments.TryGetProperty("approval_id", out var approvalIdProp))
        {
            return BuildToolResult(id, true, "Missing required parameter: approval_id");
        }

        var approvalId = approvalIdProp.GetString()!;

        if (!_approvals.TryGetValue(approvalId, out var approval))
        {
            return BuildToolResult(id, true, $"Approval not found: {approvalId}");
        }

        var result = new StringBuilder();
        result.AppendLine($"Approval ID: {approval.ApprovalId}");
        result.AppendLine($"Status: {approval.Status}");
        result.AppendLine($"Created: {approval.CreatedAt:u}");

        if (approval.RespondedAt.HasValue)
            result.AppendLine($"Responded: {approval.RespondedAt:u}");

        if (approval.Comment != null)
            result.AppendLine($"Comment: {approval.Comment}");

        if (approval.CommitResultInfo != null)
        {
            result.AppendLine($"Committed: {approval.CommitResultInfo.CommittedCount} item(s)");
            if (approval.CommitResultInfo.FailedItem != null)
                result.AppendLine($"Failed item: {approval.CommitResultInfo.FailedItem}");
            if (approval.CommitResultInfo.Error != null)
                result.AppendLine($"Error: {approval.CommitResultInfo.Error}");
        }

        return BuildToolResult(id, false, result.ToString());
    }

    private void RestoreApprovals()
    {
        var restored = ApprovalRequest.LoadAll(_approvalsDir);
        foreach (var approval in restored)
        {
            // Only restore pending/executing approvals (executing may have been interrupted)
            if (approval.Status == ApprovalStatus.Pending || approval.Status == ApprovalStatus.Executing)
            {
                if (approval.Status == ApprovalStatus.Executing)
                    approval.Status = ApprovalStatus.Pending; // Reset interrupted executions
                _approvals[approval.ApprovalId] = approval;
            }
        }
        if (_approvals.Count > 0)
            Console.Error.WriteLine($"[mcp] Restored {_approvals.Count} pending approval(s) from previous session.");
    }

    private Func<Task<IOrganizationService>> CreateCrmConnector()
    {
        return async () =>
        {
            var metadata = LoadConnectionMetadata();
            var settings = new ConnectionSettings
            {
                Url = metadata.Environment.Url,
                AuthMode = Enum.Parse<AuthMode>(metadata.AuthMode, ignoreCase: true),
                ClientId = metadata.ClientId,
                NoCache = false
            };
            var client = await ConnectionFactory.CreateAsync(settings);
            return client;
        };
    }

    private ConnectionMetadata LoadConnectionMetadata()
    {
        var path = Path.Combine(_baseDir, ".metadatasync", "connection_metadata.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"connection_metadata.json not found at {path}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ConnectionMetadata>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? throw new InvalidOperationException("Failed to deserialize connection metadata");
    }

    private void UpdateRefreshToken(string newRefreshToken)
    {
        if (newRefreshToken == _config.RefreshToken) return;

        var configPath = McpConfig.GetConfigPath(_baseDir);
        var updatedConfig = _config with { RefreshToken = newRefreshToken };
        var json = JsonSerializer.Serialize(updatedConfig, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);
    }

    private static string BuildResponse(JsonElement? id, object result)
    {
        var response = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.ValueKind == JsonValueKind.Number ? id?.GetInt64() : (object?)id?.GetString(),
            ["result"] = result
        };
        return JsonSerializer.Serialize(response);
    }

    private static string BuildErrorResponse(JsonElement? id, int code, string message)
    {
        var response = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.ValueKind == JsonValueKind.Number ? id?.GetInt64() : (object?)id?.GetString(),
            ["error"] = new { code, message }
        };
        return JsonSerializer.Serialize(response);
    }

    private static string BuildToolResult(JsonElement? id, bool isError, string text)
    {
        return BuildResponse(id, new
        {
            content = new[]
            {
                new { type = "text", text }
            },
            isError
        });
    }

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
