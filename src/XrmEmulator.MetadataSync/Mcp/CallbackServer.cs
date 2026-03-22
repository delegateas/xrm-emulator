using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using XrmEmulator.MetadataSync.Commit;
using XrmEmulator.MetadataSync.Connection;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Mcp;

public class CallbackServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly string _hmacSigningKey;
    private readonly Dictionary<string, ApprovalRequest> _approvals;
    private readonly string _approvalsDir;
    private readonly SemaphoreSlim _commitLock;
    private readonly Func<Task<IOrganizationService>> _connectToCrm;
    private readonly ConnectionMetadata _metadata;
    private readonly string _baseDir;
    private readonly Action<string>? _log;
    private CancellationTokenSource? _cts;

    public CallbackServer(
        int port,
        string hmacSigningKey,
        Dictionary<string, ApprovalRequest> approvals,
        string approvalsDir,
        SemaphoreSlim commitLock,
        Func<Task<IOrganizationService>> connectToCrm,
        ConnectionMetadata metadata,
        string baseDir,
        Action<string>? log = null)
    {
        _port = port;
        _hmacSigningKey = hmacSigningKey;
        _approvals = approvals;
        _approvalsDir = approvalsDir;
        _commitLock = commitLock;
        _connectToCrm = connectToCrm;
        _metadata = metadata;
        _baseDir = baseDir;
        _log = log;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public int Port => _port;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _ = ListenLoopAsync(_cts.Token);
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Callback server error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "";
        var query = context.Request.QueryString;

        try
        {
            if (path.StartsWith("/approve/"))
            {
                var approvalId = path["/approve/".Length..].Trim('/');
                var token = query["token"] ?? "";
                await HandleApproveAsync(context, approvalId, token);
            }
            else if (path.StartsWith("/reject/"))
            {
                var approvalId = path["/reject/".Length..].Trim('/');
                var token = query["token"] ?? "";
                var comment = query["comment"] ?? "";
                await HandleRejectAsync(context, approvalId, token, comment);
            }
            else if (path.StartsWith("/status/"))
            {
                var approvalId = path["/status/".Length..].Trim('/');
                await HandleStatusAsync(context, approvalId);
            }
            else
            {
                await SendResponse(context, 404, "Not found");
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Callback handler error: {ex.Message}");
            await SendResponse(context, 500, $"Internal error: {ex.Message}");
        }
    }

    private async Task HandleApproveAsync(HttpListenerContext context, string approvalId, string token)
    {
        if (!HmacHelper.ValidateHmac(_hmacSigningKey, $"{approvalId}:approve", token))
        {
            await SendResponse(context, 403, "Invalid token");
            return;
        }

        if (!_approvals.TryGetValue(approvalId, out var approval))
        {
            await SendResponse(context, 404, "Approval not found");
            return;
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            await SendHtmlResponse(context, 200,
                $"<h2>Already processed</h2><p>This approval is already {approval.Status}.</p>");
            return;
        }

        approval.Status = ApprovalStatus.Approved;
        approval.RespondedAt = DateTimeOffset.UtcNow;
        approval.Save(_approvalsDir);
        _log?.Invoke($"Approval {approvalId} approved. Starting commit...");

        await SendHtmlResponse(context, 200,
            "<h2>Approved!</h2><p>Commit is executing. You can close this tab.</p>" +
            "<script>setTimeout(()=>window.close(),3000)</script>");

        // Execute commit in background
        _ = ExecuteCommitAsync(approval);
    }

    private async Task HandleRejectAsync(HttpListenerContext context, string approvalId, string token, string comment)
    {
        if (!HmacHelper.ValidateHmac(_hmacSigningKey, $"{approvalId}:reject", token))
        {
            await SendResponse(context, 403, "Invalid token");
            return;
        }

        if (!_approvals.TryGetValue(approvalId, out var approval))
        {
            await SendResponse(context, 404, "Approval not found");
            return;
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            await SendHtmlResponse(context, 200,
                $"<h2>Already processed</h2><p>This approval is already {approval.Status}.</p>");
            return;
        }

        approval.Status = ApprovalStatus.Rejected;
        approval.Comment = string.IsNullOrEmpty(comment) ? null : comment;
        approval.RespondedAt = DateTimeOffset.UtcNow;
        approval.Save(_approvalsDir);
        _log?.Invoke($"Approval {approvalId} rejected. Comment: {comment}");

        await SendHtmlResponse(context, 200,
            "<h2>Rejected</h2><p>The approval has been rejected. Pending files are left unchanged for retry.</p>" +
            "<script>setTimeout(()=>window.close(),3000)</script>");
    }

    private async Task HandleStatusAsync(HttpListenerContext context, string approvalId)
    {
        if (!_approvals.TryGetValue(approvalId, out var approval))
        {
            await SendResponse(context, 404, "Approval not found");
            return;
        }

        var json = JsonSerializer.Serialize(new
        {
            approval.ApprovalId,
            Status = approval.Status.ToString(),
            approval.Comment,
            approval.CommitResultInfo
        }, new JsonSerializerOptions { WriteIndented = true });

        context.Response.ContentType = "application/json";
        await SendResponse(context, 200, json);
    }

    private async Task ExecuteCommitAsync(ApprovalRequest approval)
    {
        await _commitLock.WaitAsync();
        try
        {
            approval.Status = ApprovalStatus.Executing;
            approval.Save(_approvalsDir);

            var pendingDir = Path.Combine(_baseDir, "SolutionExport", "_pending");

            // Re-discover items to get fresh file paths and parsed data
            var allItems = CommitPipeline.DiscoverPendingItems(pendingDir);

            // Filter to only the items in this approval
            var approvedPaths = new HashSet<string>(
                approval.Items.Select(i => i.FilePath),
                StringComparer.OrdinalIgnoreCase);
            var selected = allItems.Where(i => approvedPaths.Contains(i.FilePath)).ToList();

            if (selected.Count == 0)
            {
                approval.Status = ApprovalStatus.Failed;
                approval.CommitResultInfo = new CommitResultInfo(0, null, "No matching pending items found");
                approval.Save(_approvalsDir);
                return;
            }

            _log?.Invoke($"Executing commit for approval {approval.ApprovalId}: {selected.Count} item(s)");

            using var client = (IDisposable)await _connectToCrm();
            var result = CommitPipeline.ExecuteCommit(
                (IOrganizationService)client,
                _metadata,
                _baseDir,
                selected,
                _log);

            approval.CommitResultInfo = new CommitResultInfo(
                result.Committed.Count,
                result.FailedItem?.DisplayName,
                result.FailedException?.Message);

            approval.Status = result.FailedItem != null ? ApprovalStatus.Failed : ApprovalStatus.Completed;
            approval.Save(_approvalsDir);

            _log?.Invoke($"Commit {approval.ApprovalId} finished: {result.Committed.Count} committed, " +
                $"failed: {result.FailedItem?.DisplayName ?? "none"}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Commit execution error: {ex.Message}");
            approval.Status = ApprovalStatus.Failed;
            approval.CommitResultInfo = new CommitResultInfo(0, null, ex.Message);
            approval.Save(_approvalsDir);
        }
        finally
        {
            _commitLock.Release();
        }
    }

    private static async Task SendResponse(HttpListenerContext context, int statusCode, string body)
    {
        context.Response.StatusCode = statusCode;
        var buffer = Encoding.UTF8.GetBytes(body);
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.Close();
    }

    private static async Task SendHtmlResponse(HttpListenerContext context, int statusCode, string htmlBody)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/html";
        var html = $"<html><body>{htmlBody}</body></html>";
        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.Close();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { /* best effort */ }
        _cts?.Dispose();
    }
}
