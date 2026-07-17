using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using XrmEmulator.Models.CrmMetadata;
using XrmEmulator.Services;

namespace XrmEmulator.Controllers;

/// <summary>
/// Lists Custom APIs defined in the solution and lets a developer manually trigger one — a
/// stand-in for the scheduled Cloud Flows that call these in production. App-agnostic (unlike
/// <see cref="CrmController"/>'s /crm/{app}/... routes) since Custom APIs are global/unbound in
/// this solution, not entity- or app-scoped.
/// </summary>
[ApiController]
[Route("customapis")]
public sealed class CustomApiController : ControllerBase
{
    private readonly SolutionMetadataService _metadata;
    private readonly CustomApiExecutionService _executionService;
    private readonly CustomApiExecutionHistoryStore _historyStore;
    private readonly IConfiguration _configuration;

    public CustomApiController(
        SolutionMetadataService metadata,
        CustomApiExecutionService executionService,
        CustomApiExecutionHistoryStore historyStore,
        IConfiguration configuration)
    {
        _metadata = metadata;
        _executionService = executionService;
        _historyStore = historyStore;
        _configuration = configuration;
    }

    [HttpGet("")]
    public IActionResult List()
    {
        var html = new StringBuilder();
        AppendHtmlHead(html, "Custom APIs");
        html.AppendLine("<body>");
        html.AppendLine("<div class='app-picker'>");
        html.AppendLine("<nav><a href='/'>Home</a></nav>");
        html.AppendLine("<h1>Custom APIs</h1>");
        html.AppendLine("<p>Manually trigger a Custom API &mdash; a stand-in for the scheduled Cloud Flows that call these in production.</p>");

        var apis = _metadata.CustomApis;
        if (apis.Count == 0)
        {
            html.AppendLine("<div class='empty-grid'>No Custom APIs found in the solution exports.</div>");
        }
        else
        {
            var groups = apis
                .GroupBy(a => a.SolutionName)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                html.AppendLine($"<h2>{Encode(group.Key)} <span class='entity-count'>({group.Count()})</span></h2>");
                html.AppendLine("<table class='view-grid'>");
                html.AppendLine("<thead><tr><th>Display Name</th><th>Unique Name</th><th>Binding</th><th>Plugin Type</th><th>Parameters</th><th></th></tr></thead>");
                html.AppendLine("<tbody>");
                foreach (var api in group.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    var pluginTypeStyle = string.IsNullOrEmpty(api.PluginTypeName) ? " style='color:#b00020'" : "";
                    html.AppendLine("<tr>");
                    html.AppendLine($"<td><a href='/customapis/{Encode(api.UniqueName)}/trigger'>{Encode(api.DisplayName)}</a></td>");
                    html.AppendLine($"<td>{Encode(api.UniqueName)}</td>");
                    html.AppendLine($"<td>{Encode(DescribeBinding(api))}</td>");
                    html.AppendLine($"<td{pluginTypeStyle}>{Encode(api.PluginTypeName ?? "(not resolved)")}</td>");
                    html.AppendLine($"<td>{api.RequestParameters.Count} in / {api.ResponseProperties.Count} out</td>");
                    html.AppendLine($"<td><a href='/customapis/{Encode(api.UniqueName)}/trigger' class='btn btn-secondary'>Trigger</a></td>");
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</tbody></table>");
            }
        }

        html.AppendLine("<h2>Recent executions</h2>");
        var history = _historyStore.GetAll().Take(50).ToList();
        if (history.Count == 0)
        {
            html.AppendLine("<div class='empty-grid'>No recorded executions yet. Trigger a Custom API above and refresh this page.</div>");
        }
        else
        {
            html.AppendLine("<table class='view-grid'>");
            html.AppendLine("<thead><tr><th>Timestamp (UTC)</th><th>Custom API</th><th>Plugin Type</th><th>Result</th></tr></thead>");
            html.AppendLine("<tbody>");
            foreach (var h in history)
            {
                var result = h.Success ? "OK" : $"Error: {Encode(h.Error ?? "")}";
                var resultStyle = h.Success ? "" : " style='color:#b00020'";
                html.AppendLine("<tr>");
                html.AppendLine($"<td>{h.Timestamp:yyyy-MM-dd HH:mm:ss}</td>");
                html.AppendLine($"<td><a href='/customapis/{Encode(h.CustomApiUniqueName)}/trigger'>{Encode(h.CustomApiUniqueName)}</a></td>");
                html.AppendLine($"<td>{Encode(h.PluginTypeName)}</td>");
                html.AppendLine($"<td{resultStyle}>{result}</td>");
                html.AppendLine("</tr>");
            }
            html.AppendLine("</tbody></table>");
        }

        html.AppendLine("</div>");
        html.AppendLine("</body></html>");
        return Content(html.ToString(), "text/html");
    }

    [HttpGet("{uniqueName}/trigger")]
    public IActionResult TriggerForm(string uniqueName)
    {
        var api = _metadata.GetCustomApi(uniqueName);
        if (api == null) return NotFound($"Custom API '{uniqueName}' not found.");

        var html = new StringBuilder();
        AppendHtmlHead(html, $"Trigger {api.DisplayName}");
        html.AppendLine("<body>");
        html.AppendLine("<div class='app-picker'>");
        AppendBreadcrumbAndHeader(html, api);
        RenderTriggerFormBody(html, api, LoadExampleValues(uniqueName));
        html.AppendLine("</div>");
        html.AppendLine("</body></html>");
        return Content(html.ToString(), "text/html");
    }

    [HttpPost("{uniqueName}/trigger")]
    public async Task<IActionResult> Trigger(string uniqueName)
    {
        var api = _metadata.GetCustomApi(uniqueName);
        if (api == null) return NotFound($"Custom API '{uniqueName}' not found.");

        var formData = await ReadFormDataAsync().ConfigureAwait(false);
        var result = _executionService.Execute(api, formData);

        var html = new StringBuilder();
        AppendHtmlHead(html, $"Trigger {api.DisplayName}");
        html.AppendLine("<body>");
        html.AppendLine("<div class='app-picker'>");
        AppendBreadcrumbAndHeader(html, api);

        html.AppendLine(result.Success
            ? "<div class='flash-success'>Executed successfully.</div>"
            : $"<div class='flash-error'>{Encode(result.ErrorMessage ?? "Execution failed.")}</div>");

        if (result.OutputParameters.Count > 0)
        {
            html.AppendLine("<h2>Output parameters</h2>");
            html.AppendLine("<table class='view-grid'>");
            html.AppendLine("<thead><tr><th>Name</th><th>Value</th></tr></thead>");
            html.AppendLine("<tbody>");
            foreach (var (key, value) in result.OutputParameters)
            {
                html.AppendLine("<tr>");
                html.AppendLine($"<td>{Encode(key)}</td>");
                html.AppendLine($"<td>{Encode(value ?? "")}</td>");
                html.AppendLine("</tr>");
            }
            html.AppendLine("</tbody></table>");
        }

        if (result.TraceLog.Count > 0)
        {
            html.AppendLine("<h2>Trace log</h2>");
            html.AppendLine("<pre style='background:#f5f5f5;padding:8px;overflow-x:auto;font-size:12px'>");
            foreach (var line in result.TraceLog)
                html.AppendLine(Encode(line));
            html.AppendLine("</pre>");
        }

        html.AppendLine("<h2>Trigger again</h2>");
        RenderTriggerFormBody(html, api, formData);

        html.AppendLine("</div>");
        html.AppendLine("</body></html>");
        return Content(html.ToString(), "text/html");
    }

    private static void AppendBreadcrumbAndHeader(StringBuilder html, CrmCustomApi api)
    {
        html.AppendLine("<nav><a href='/'>Home</a> &rsaquo; <a href='/customapis'>Custom APIs</a></nav>");
        html.AppendLine($"<h1>{Encode(api.DisplayName)}</h1>");
        if (!string.IsNullOrEmpty(api.Description))
            html.AppendLine($"<p>{Encode(api.Description)}</p>");
        html.AppendLine($"<p><code>{Encode(api.UniqueName)}</code> &middot; {Encode(DescribeBinding(api))} &middot; {Encode(api.PluginTypeName ?? "(plugin type not resolved)")}</p>");
    }

    private static void RenderTriggerFormBody(StringBuilder html, CrmCustomApi api, IReadOnlyDictionary<string, string> prefill)
    {
        html.AppendLine($"<form method='post' action='/customapis/{Encode(api.UniqueName)}/trigger'>");
        html.AppendLine("<div class='form-actions'>");
        html.AppendLine("<button type='submit' class='btn btn-primary'>Trigger</button>");
        html.AppendLine("<a href='/customapis' class='btn btn-secondary'>Back</a>");
        html.AppendLine("</div>");

        if (api.RequestParameters.Count == 0)
        {
            html.AppendLine("<div class='empty-grid'>This Custom API takes no request parameters.</div>");
        }
        else
        {
            html.AppendLine("<div class='form-section'>");
            foreach (var param in api.RequestParameters)
            {
                prefill.TryGetValue(param.UniqueName, out var value);
                RenderParameterField(html, param, value);
            }
            html.AppendLine("</div>");
        }
        html.AppendLine("</form>");

        if (api.ResponseProperties.Count > 0)
        {
            html.AppendLine("<h2>Response properties</h2>");
            html.AppendLine("<table class='view-grid'>");
            html.AppendLine("<thead><tr><th>Name</th><th>Type</th></tr></thead>");
            html.AppendLine("<tbody>");
            foreach (var prop in api.ResponseProperties)
            {
                html.AppendLine("<tr>");
                html.AppendLine($"<td>{Encode(prop.DisplayName)} <span class='entity-count'>({Encode(prop.UniqueName)})</span></td>");
                html.AppendLine($"<td>{Encode(DescribeParamType(prop.Type))}</td>");
                html.AppendLine("</tr>");
            }
            html.AppendLine("</tbody></table>");
        }
    }

    private static void RenderParameterField(StringBuilder html, CrmCustomApiParameter param, string? value)
    {
        html.AppendLine("<div class='form-field'>");
        var requiredMarker = param.IsOptional ? "" : " *";
        html.AppendLine($"<label for='{Encode(param.UniqueName)}'>{Encode(param.DisplayName)}{requiredMarker} <span class='entity-count'>({Encode(DescribeParamType(param.Type))})</span></label>");

        switch (param.Type)
        {
            case CrmCustomApiParameter.TypeBoolean:
                var isChecked = value is "true" or "1" or "on" ? " checked" : "";
                html.AppendLine($"<input type='hidden' name='{Encode(param.UniqueName)}' value='false' />");
                html.AppendLine($"<input type='checkbox' name='{Encode(param.UniqueName)}' id='{Encode(param.UniqueName)}' value='true'{isChecked} />");
                break;
            case CrmCustomApiParameter.TypeDateTime:
                html.AppendLine($"<input type='datetime-local' name='{Encode(param.UniqueName)}' id='{Encode(param.UniqueName)}' value='{Encode(value)}' />");
                break;
            case CrmCustomApiParameter.TypeInteger:
            case CrmCustomApiParameter.TypePicklist:
                html.AppendLine($"<input type='number' step='1' name='{Encode(param.UniqueName)}' id='{Encode(param.UniqueName)}' value='{Encode(value)}' />");
                break;
            case CrmCustomApiParameter.TypeFloat:
            case CrmCustomApiParameter.TypeDecimal:
            case CrmCustomApiParameter.TypeMoney:
                html.AppendLine($"<input type='number' step='any' name='{Encode(param.UniqueName)}' id='{Encode(param.UniqueName)}' value='{Encode(value)}' />");
                break;
            case CrmCustomApiParameter.TypeEntity:
            case CrmCustomApiParameter.TypeEntityCollection:
            case CrmCustomApiParameter.TypeEntityReference:
                html.AppendLine($"<input type='text' name='{Encode(param.UniqueName)}' id='{Encode(param.UniqueName)}' value='{Encode(value)}' disabled placeholder='Not supported for manual triggering' />");
                break;
            default: // String, StringArray, Guid
                html.AppendLine($"<input type='text' name='{Encode(param.UniqueName)}' id='{Encode(param.UniqueName)}' value='{Encode(value)}' />");
                break;
        }

        if (!string.IsNullOrEmpty(param.Description))
            html.AppendLine($"<small style='color:#666'>{Encode(param.Description)}</small>");

        html.AppendLine("</div>");
    }

    private Dictionary<string, string> LoadExampleValues(string uniqueName)
    {
        var json = _configuration[$"CustomApiExamples:{uniqueName}:Parameters"];
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    _ => prop.Value.GetRawText(),
                };
            }
        }
        catch (JsonException)
        {
            // Malformed example config — fall back to an empty form rather than failing the page.
        }

        return result;
    }

    private static string DescribeBinding(CrmCustomApi api) => api.BindingType switch
    {
        CrmCustomApi.BindingEntity => $"Bound to {api.BoundEntityLogicalName}",
        CrmCustomApi.BindingEntityCollection => $"Bound to {api.BoundEntityLogicalName} (collection)",
        _ => "Global " + (api.IsFunction ? "Function" : "Action"),
    };

    private static string DescribeParamType(int type) => type switch
    {
        CrmCustomApiParameter.TypeBoolean => "Boolean",
        CrmCustomApiParameter.TypeDateTime => "DateTime",
        CrmCustomApiParameter.TypeDecimal => "Decimal",
        CrmCustomApiParameter.TypeEntity => "Entity",
        CrmCustomApiParameter.TypeEntityCollection => "EntityCollection",
        CrmCustomApiParameter.TypeEntityReference => "EntityReference",
        CrmCustomApiParameter.TypeFloat => "Float",
        CrmCustomApiParameter.TypeInteger => "Integer",
        CrmCustomApiParameter.TypeMoney => "Money",
        CrmCustomApiParameter.TypePicklist => "Picklist",
        CrmCustomApiParameter.TypeString => "String",
        CrmCustomApiParameter.TypeStringArray => "StringArray",
        CrmCustomApiParameter.TypeGuid => "Guid",
        _ => "Unknown",
    };

    private async Task<Dictionary<string, string>> ReadFormDataAsync()
    {
        var form = await Request.ReadFormAsync().ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in form.Keys)
            result[key] = form[key].ToString();
        return result;
    }

    private static void AppendHtmlHead(StringBuilder html, string title)
    {
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine($"<title>{Encode(title)}</title>");
        html.AppendLine("<meta charset='utf-8'>");
        html.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1'>");
        html.AppendLine("<link rel='stylesheet' href='/crm/styles.css'>");
        html.AppendLine("</head>");
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");
}
