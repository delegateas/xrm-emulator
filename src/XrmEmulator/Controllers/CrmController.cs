using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Serilog;
using XrmEmulator.Models.CrmMetadata;
using XrmEmulator.Services;

namespace XrmEmulator.Controllers;

[ApiController]
[Route("crm")]
public sealed class CrmController : ControllerBase
{
    private static readonly Serilog.ILogger _log = Log.ForContext<CrmController>();
    private readonly SolutionMetadataService _metadata;
    private readonly OrganizationServiceResolver _serviceResolver;

    public CrmController(SolutionMetadataService metadata, OrganizationServiceResolver serviceResolver)
    {
        _metadata = metadata;
        _serviceResolver = serviceResolver;
    }

    [HttpGet("")]
    public IActionResult AppPicker()
    {
        if (!_metadata.IsConfigured)
            return Content("Solution exports not configured. Use WithSolutionExports() to enable the CRM UI.", "text/plain");

        var html = new StringBuilder();
        AppendHtmlHead(html, "CRM Apps");
        html.AppendLine("<body>");
        html.AppendLine("<div class='app-picker'>");
        html.AppendLine("<h1>Dynamics CRM Apps</h1>");
        html.AppendLine("<div class='app-grid'>");

        foreach (var app in _metadata.Apps)
        {
            html.AppendLine("<div class='app-card'>");
            html.AppendLine($"<a href='/crm/{Encode(app.UniqueName)}'>");
            html.AppendLine($"<h2>{Encode(app.DisplayName)}</h2>");
            if (!string.IsNullOrEmpty(app.Description))
                html.AppendLine($"<p>{Encode(app.Description)}</p>");
            html.AppendLine($"<span class='entity-count'>{app.EntityNames.Count} entities</span>");
            html.AppendLine("</a>");
            html.AppendLine("</div>");
        }

        html.AppendLine("</div>");
        html.AppendLine("</div>");
        html.AppendLine("</body></html>");
        return Content(html.ToString(), "text/html");
    }

    [HttpGet("_webresource/{name}")]
    public IActionResult WebResource(string name)
    {
        var path = _metadata.GetWebResourcePath(name);
        if (path == null || !System.IO.File.Exists(path)) return NotFound();
        var content = System.IO.File.ReadAllText(path);
        return Content(content, "application/javascript");
    }

    [HttpGet("{appName:regex(^[[a-zA-Z0-9_]]+$)}")]
    public IActionResult AppRedirect(string appName)
    {
        var app = FindApp(appName);
        if (app == null) return NotFound("App not found");

        var siteMap = app.SiteMapUniqueName != null ? _metadata.GetSiteMap(app.SiteMapUniqueName) : null;

        // Find first entity from sitemap
        var firstEntity = siteMap?.Areas
            .SelectMany(a => a.Groups)
            .SelectMany(g => g.SubAreas)
            .FirstOrDefault(s => !string.IsNullOrEmpty(s.Entity))?.Entity;

        firstEntity ??= app.EntityNames.FirstOrDefault();

        if (firstEntity == null)
            return Redirect($"/crm/");

        return Redirect($"/crm/{appName}/{firstEntity}");
    }

    [HttpGet("{appName}/{entityName}")]
    public async Task<IActionResult> ViewGrid(string appName, string entityName, [FromQuery] Guid? view = null)
    {
        var app = FindApp(appName);
        if (app == null) return NotFound("App not found");

        var siteMap = app.SiteMapUniqueName != null ? _metadata.GetSiteMap(app.SiteMapUniqueName) : null;
        var entity = _metadata.GetEntity(entityName);
        var views = _metadata.GetViewsForApp(app, entityName);

        CrmView? activeView = null;
        if (view.HasValue)
            activeView = _metadata.GetView(view.Value);
        activeView ??= views.FirstOrDefault();

        var html = new StringBuilder();
        var entityDisplay = entity?.DisplayName ?? entityName;
        AppendHtmlHead(html, $"{entityDisplay} - {app.DisplayName}");
        html.AppendLine("<body>");
        AppendAppShell(html, app, siteMap, entityName);

        // Content
        html.AppendLine("<div class='app-content'>");

        // Content header with view selector
        html.AppendLine("<div class='content-header'>");
        html.AppendLine($"<h1>{Encode(entityDisplay)}</h1>");
        html.AppendLine($"<a href='/crm/{Encode(appName)}/{Encode(entityName)}/new' class='btn btn-primary'>+ New</a>");
        if (_metadata.GetQuickCreateForm(entityName) != null)
            html.AppendLine($"<a href='/crm/{Encode(appName)}/{Encode(entityName)}/new?formType=quick' class='btn btn-secondary'>Quick Create</a>");
        // Custom appactions for Main Grid (location=1)
        foreach (var action in _metadata.GetAppActions(entityName, CrmAppAction.LocationMainGrid, appName).OrderBy(a => a.Sequence))
            html.AppendLine($"<span class='btn btn-secondary cmdbar-btn-custom' title='{Encode(action.Label)}'>{Encode(action.Label)}</span>");
        if (views.Count > 0)
        {
            html.AppendLine($"<form method='get' action='/crm/{Encode(appName)}/{Encode(entityName)}'>");
            html.AppendLine("<select name='view' class='view-selector' onchange='this.form.submit()'>");
            foreach (var v in views)
            {
                var selected = v.Id == activeView?.Id ? " selected" : "";
                html.AppendLine($"<option value='{v.Id}'{selected}>{Encode(v.Name)}</option>");
            }
            html.AppendLine("</select>");
            html.AppendLine("</form>");
        }
        html.AppendLine("</div>");

        // Data grid
        if (activeView != null)
        {
            var orgService = _serviceResolver.GetForApp(app);
            await RenderViewGrid(html, app, activeView, entity, orgService).ConfigureAwait(false);
        }
        else
        {
            html.AppendLine("<div class='empty-grid'>No views available for this entity.</div>");
        }

        html.AppendLine("</div>"); // app-content
        html.AppendLine("</div>"); // app-shell
        html.AppendLine("</body></html>");
        return Content(html.ToString(), "text/html");
    }

    [HttpGet("{appName}/{entityName}/new")]
    public async Task<IActionResult> NewRecordForm(string appName, string entityName,
        [FromQuery] string? error = null, [FromQuery] string? formType = null,
        [FromQuery] string? parentEntity = null, [FromQuery] Guid? parentId = null,
        [FromQuery] string? relationship = null)
    {
        var app = FindApp(appName);
        if (app == null) return NotFound("App not found");

        var siteMap = app.SiteMapUniqueName != null ? _metadata.GetSiteMap(app.SiteMapUniqueName) : null;
        var entity = _metadata.GetEntity(entityName);

        // Quick-create form — right-side flyout panel (Dynamics-style)
        if (formType == "quick")
        {
            var quickForm = _metadata.GetQuickCreateForm(entityName);
            if (quickForm == null)
                return NotFound("No quick-create form available for this entity.");

            var html = new StringBuilder();
            var entityDisplay = entity?.DisplayName ?? entityName;
            AppendHtmlHead(html, $"Quick Create {entityDisplay} - {app.DisplayName}");
            html.AppendLine($"<body data-app-name='{Encode(appName)}' data-entity-name='{Encode(entityName)}' data-record-id=''>");
            AppendAppShell(html, app, siteMap, entityName);

            html.AppendLine("<div class='app-content'>");

            // Overlay scrim + right-side panel
            html.AppendLine($"<a href='/crm/{Encode(appName)}/{Encode(entityName)}' class='qc-scrim'></a>");
            html.AppendLine("<div class='qc-panel'>");
            html.AppendLine("<div class='qc-panel-header'>");
            html.AppendLine($"<h2>Quick Create: {Encode(entityDisplay)}</h2>");
            html.AppendLine($"<a href='/crm/{Encode(appName)}/{Encode(entityName)}' class='qc-panel-close'>&times;</a>");
            html.AppendLine("</div>");
            var postParams = "formType=quick";
            if (parentEntity != null && parentId.HasValue)
                postParams += $"&parentEntity={Encode(parentEntity)}&parentId={parentId.Value}&relationship={Encode(relationship ?? "")}";
            html.AppendLine($"<form method='post' action='/crm/{Encode(appName)}/{Encode(entityName)}/new?{postParams}'>");
            html.AppendLine("<div class='qc-panel-body'>");

            var emptyRecord = new Entity(entityName);
            await RenderForm(html, quickForm, emptyRecord, entity).ConfigureAwait(false);

            html.AppendLine("</div>"); // qc-panel-body
            html.AppendLine("<div class='qc-panel-footer'>");
            html.AppendLine("<button type='submit' class='btn btn-primary'>Save and Close</button>");
            html.AppendLine($"<a href='/crm/{Encode(appName)}/{Encode(entityName)}' class='btn btn-secondary'>Discard</a>");
            html.AppendLine("</div>");
            html.AppendLine("</form>");
            html.AppendLine("</div>"); // qc-panel

            html.AppendLine("</div>"); // app-content
            html.AppendLine("</div>"); // app-shell
            InjectFormScripts(html, quickForm, appName, entityName, "");
            html.AppendLine("</body></html>");
            return Content(html.ToString(), "text/html");
        }

        // Main form
        {
            var forms = _metadata.GetFormsForApp(app, entityName);
            var form = forms.FirstOrDefault() ?? _metadata.GetMainForm(entityName);

            var html = new StringBuilder();
            var entityDisplay = entity?.DisplayName ?? entityName;
            AppendHtmlHead(html, $"New {entityDisplay} - {app.DisplayName}");
            html.AppendLine($"<body data-app-name='{Encode(appName)}' data-entity-name='{Encode(entityName)}' data-record-id=''>");
            AppendAppShell(html, app, siteMap, entityName);

            html.AppendLine("<div class='app-content'>");

            if (!string.IsNullOrEmpty(error))
                html.AppendLine($"<div class='flash-error'>{Encode(error)}</div>");

            html.AppendLine("<div class='form-container'>");
            html.AppendLine($"<p><a href='/crm/{Encode(appName)}/{Encode(entityName)}'>&larr; Back to list</a></p>");
            html.AppendLine($"<h1>New {Encode(entityDisplay)}</h1>");

            html.AppendLine($"<form method='post' action='/crm/{Encode(appName)}/{Encode(entityName)}/new'>");
            html.AppendLine("<div class='form-actions'>");
            html.AppendLine("<button type='submit' class='btn btn-primary'>Save</button>");
            html.AppendLine($"<a href='/crm/{Encode(appName)}/{Encode(entityName)}' class='btn btn-secondary'>Cancel</a>");
            html.AppendLine("</div>");

            var emptyRecord = new Entity(entityName);
            if (form != null)
            {
                await RenderForm(html, form, emptyRecord, entity).ConfigureAwait(false);
            }
            else
            {
                RenderFallbackNewForm(html, entity);
            }

            html.AppendLine("</form>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
            if (form != null) InjectFormScripts(html, form, appName, entityName, "");
            html.AppendLine("</body></html>");
            return Content(html.ToString(), "text/html");
        }
    }

    [HttpPost("{appName}/{entityName}/new")]
    public async Task<IActionResult> CreateRecord(string appName, string entityName,
        [FromQuery] string? formType = null,
        [FromQuery] string? parentEntity = null, [FromQuery] Guid? parentId = null,
        [FromQuery] string? relationship = null)
    {
        var app = FindApp(appName);
        var entity = _metadata.GetEntity(entityName);
        var formData = await ReadFormDataAsync().ConfigureAwait(false);

        try
        {
            // Determine form ID for form-scoped business rule filtering
            var form = formType == "quick"
                ? _metadata.GetQuickCreateForm(entityName)
                : _metadata.GetMainForm(entityName);
            var orgService = form != null
                ? _serviceResolver.GetForForm(form.Id)
                : (app != null ? _serviceResolver.GetForApp(app) : _serviceResolver.Default);
            var newEntity = new Entity(entityName);

            foreach (var kvp in formData)
            {
                var fieldName = kvp.Key;
                var rawValue = kvp.Value;

                if (string.IsNullOrEmpty(fieldName) || fieldName.StartsWith('_'))
                    continue;

                var attrMeta = entity?.Attributes.GetValueOrDefault(fieldName);
                var attrType = attrMeta?.Type ?? "nvarchar";

                var typedValue = ConvertToTypedValue(rawValue, attrType);
                if (typedValue != null)
                    newEntity[fieldName] = typedValue;
            }

            // Set parent lookup when creating from a subgrid context
            if (parentEntity != null && parentId.HasValue)
            {
                var lookupAttr = GuessRelationshipAttribute(relationship, parentEntity);
                if (lookupAttr != null)
                    newEntity[lookupAttr] = new EntityReference(parentEntity, parentId.Value);
            }

            if (newEntity.Attributes.Count > 0)
            {
                var newId = await orgService.CreateAsync(newEntity).ConfigureAwait(false);
                // Quick-create from subgrid redirects back to the parent record; otherwise back to the grid
                if (formType == "quick" && parentEntity != null && parentId.HasValue)
                    return Redirect($"/crm/{appName}/{parentEntity}/{parentId.Value}?saved=true");
                if (formType == "quick")
                    return Redirect($"/crm/{appName}/{entityName}");
                return Redirect($"/crm/{appName}/{entityName}/{newId}?saved=true");
            }

            return Redirect($"/crm/{appName}/{entityName}/new?error={Uri.EscapeDataString("No fields provided")}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "CrmController: Failed to create {Entity} record", entityName);
            return Redirect($"/crm/{appName}/{entityName}/new?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    [HttpGet("{appName}/{entityName}/{recordId:guid}")]
    public async Task<IActionResult> RecordForm(string appName, string entityName, Guid recordId,
        [FromQuery] bool saved = false, [FromQuery] string? error = null)
    {
        var app = FindApp(appName);
        if (app == null) return NotFound("App not found");

        var siteMap = app.SiteMapUniqueName != null ? _metadata.GetSiteMap(app.SiteMapUniqueName) : null;
        var entity = _metadata.GetEntity(entityName);
        var forms = _metadata.GetFormsForApp(app, entityName);
        var form = forms.FirstOrDefault() ?? _metadata.GetMainForm(entityName);

        var orgService = _serviceResolver.GetForApp(app);
        Entity record;
        try
        {
            record = await orgService.RetrieveAsync(entityName, recordId, new ColumnSet(true)).ConfigureAwait(false);
        }
        catch
        {
            return NotFound("Record not found");
        }

        var html = new StringBuilder();
        var entityDisplay = entity?.DisplayName ?? entityName;
        var recordName = GetRecordName(record, entity);
        AppendHtmlHead(html, $"{recordName} - {entityDisplay}");
        html.AppendLine($"<body data-app-name='{Encode(appName)}' data-entity-name='{Encode(entityName)}' data-record-id='{recordId}'>");
        AppendAppShell(html, app, siteMap, entityName);

        html.AppendLine("<div class='app-content'>");

        // Flash messages
        if (saved)
            html.AppendLine("<div class='flash-success'>Record saved successfully.</div>");
        if (!string.IsNullOrEmpty(error))
            html.AppendLine($"<div class='flash-error'>{Encode(error)}</div>");

        html.AppendLine("<div class='form-container'>");

        // Back link
        html.AppendLine($"<p><a href='/crm/{Encode(appName)}/{Encode(entityName)}'>&larr; Back to list</a></p>");

        html.AppendLine($"<h1>{Encode(recordName)}</h1>");

        // Form with save
        html.AppendLine($"<form method='post' action='/crm/{Encode(appName)}/{Encode(entityName)}/{recordId}'>");
        html.AppendLine("<div class='form-actions'>");
        html.AppendLine("<button type='submit' class='btn btn-primary'>Save</button>");
        html.AppendLine($"<a href='/crm/{Encode(appName)}/{Encode(entityName)}' class='btn btn-secondary'>Cancel</a>");
        html.AppendLine("<span class='header-spacer'></span>");
        html.AppendLine($"<button type='submit' formaction='/crm/{Encode(appName)}/{Encode(entityName)}/{recordId}/delete' formmethod='post' class='btn btn-danger' onclick=\"return confirm('Delete this record?')\">Delete</button>");
        html.AppendLine("</div>");

        if (form != null)
        {
            await RenderForm(html, form, record, entity, _metadata, orgService, app).ConfigureAwait(false);
        }
        else
        {
            RenderFallbackForm(html, record, entity);
        }

        html.AppendLine("</form>");
        html.AppendLine("</div>"); // form-container
        html.AppendLine("</div>"); // app-content
        html.AppendLine("</div>"); // app-shell
        if (form != null) InjectFormScripts(html, form, appName, entityName, recordId.ToString());
        html.AppendLine("</body></html>");
        return Content(html.ToString(), "text/html");
    }

    [HttpPost("{appName}/{entityName}/{recordId:guid}")]
    public async Task<IActionResult> SaveRecord(string appName, string entityName, Guid recordId)
    {
        var app = FindApp(appName);
        var entity = _metadata.GetEntity(entityName);
        var formData = await ReadFormDataAsync().ConfigureAwait(false);

        try
        {
            var orgService = app != null ? _serviceResolver.GetForApp(app) : _serviceResolver.Default;
            var updateEntity = new Entity(entityName, recordId);

            foreach (var kvp in formData)
            {
                var fieldName = kvp.Key;
                var rawValue = kvp.Value;

                if (string.IsNullOrEmpty(fieldName) || fieldName.StartsWith('_'))
                    continue;

                var attrMeta = entity?.Attributes.GetValueOrDefault(fieldName);
                var attrType = attrMeta?.Type ?? "nvarchar";

                var typedValue = ConvertToTypedValue(rawValue, attrType);
                if (typedValue != null)
                    updateEntity[fieldName] = typedValue;
                else if (string.IsNullOrEmpty(rawValue))
                    updateEntity[fieldName] = null;
            }

            if (updateEntity.Attributes.Count > 0)
                await orgService.UpdateAsync(updateEntity).ConfigureAwait(false);

            return Redirect($"/crm/{appName}/{entityName}/{recordId}?saved=true");
        }
        catch (Exception ex)
        {
            return Redirect($"/crm/{appName}/{entityName}/{recordId}?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    [HttpPost("{appName}/{entityName}/{recordId:guid}/delete")]
    public async Task<IActionResult> DeleteRecord(string appName, string entityName, Guid recordId)
    {
        var app = FindApp(appName);
        try
        {
            var orgService = app != null ? _serviceResolver.GetForApp(app) : _serviceResolver.Default;
            await orgService.DeleteAsync(entityName, recordId).ConfigureAwait(false);
            return Redirect($"/crm/{appName}/{entityName}");
        }
        catch (Exception ex)
        {
            return Redirect($"/crm/{appName}/{entityName}/{recordId}?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    // --- HTML helpers ---

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

    private static void AppendAppShell(StringBuilder html, CrmApp app, CrmSiteMap? siteMap, string activeEntity)
    {
        html.AppendLine("<div class='app-shell'>");

        // Header
        html.AppendLine("<div class='app-header'>");
        html.AppendLine($"<span class='app-title'>{Encode(app.DisplayName)}</span>");
        html.AppendLine("<span class='header-spacer'></span>");
        html.AppendLine("<a href='/crm/'>Switch App</a>");
        html.AppendLine("<a href='/debug/data'>Debug Data</a>");
        html.AppendLine("</div>");

        // Sidebar
        html.AppendLine("<div class='app-sidebar'>");
        if (siteMap != null)
        {
            foreach (var area in siteMap.Areas)
            {
                html.AppendLine($"<div class='sidebar-area-title'>{Encode(area.Title)}</div>");
                foreach (var group in area.Groups)
                {
                    html.AppendLine($"<div class='sidebar-group-title'>{Encode(group.Title)}</div>");
                    foreach (var subArea in group.SubAreas)
                    {
                        if (string.IsNullOrEmpty(subArea.Entity)) continue;
                        var isActive = string.Equals(subArea.Entity, activeEntity, StringComparison.OrdinalIgnoreCase);
                        var activeClass = isActive ? " active" : "";
                        var label = subArea.Title ?? subArea.Entity;
                        html.AppendLine($"<a class='sidebar-item{activeClass}' href='/crm/{Encode(app.UniqueName)}/{Encode(subArea.Entity)}'>{Encode(label)}</a>");
                    }
                }
            }
        }
        else
        {
            foreach (var entityName in app.EntityNames)
            {
                var isActive = string.Equals(entityName, activeEntity, StringComparison.OrdinalIgnoreCase);
                var activeClass = isActive ? " active" : "";
                html.AppendLine($"<a class='sidebar-item{activeClass}' href='/crm/{Encode(app.UniqueName)}/{Encode(entityName)}'>{Encode(entityName)}</a>");
            }
        }
        html.AppendLine("</div>");
    }

    private static async Task RenderViewGrid(StringBuilder html, CrmApp app, CrmView activeView, CrmEntity? entity, IOrganizationServiceAsync orgService)
    {
        html.AppendLine("<table class='view-grid'>");
        html.AppendLine("<thead><tr>");
        foreach (var col in activeView.Columns)
        {
            var header = entity?.Attributes.GetValueOrDefault(col.Name)?.DisplayName ?? col.Name;
            html.AppendLine($"<th style='width:{col.Width}px'>{Encode(header)}</th>");
        }
        html.AppendLine("</tr></thead>");
        html.AppendLine("<tbody>");

        try
        {
            var result = await orgService.RetrieveMultipleAsync(new FetchExpression(activeView.FetchXml)).ConfigureAwait(false);

            if (result.Entities.Count == 0)
            {
                html.AppendLine($"<tr><td colspan='{activeView.Columns.Count}' class='empty-grid'>No records found.</td></tr>");
            }
            else
            {
                foreach (var record in result.Entities)
                {
                    html.AppendLine("<tr>");
                    var isFirst = true;
                    foreach (var col in activeView.Columns)
                    {
                        var value = record.Contains(col.Name) ? FormatGridValue(record[col.Name]) : "";
                        if (isFirst)
                        {
                            html.AppendLine($"<td><a href='/crm/{Encode(app.UniqueName)}/{Encode(activeView.EntityName)}/{record.Id}'>{Encode(value)}</a></td>");
                            isFirst = false;
                        }
                        else
                        {
                            html.AppendLine($"<td>{Encode(value)}</td>");
                        }
                    }
                    html.AppendLine("</tr>");
                }
            }
        }
        catch (Exception ex)
        {
            html.AppendLine($"<tr><td colspan='{activeView.Columns.Count}' class='empty-grid'>Error: {Encode(ex.Message)}</td></tr>");
        }

        html.AppendLine("</tbody>");
        html.AppendLine("</table>");
    }

    private static async Task RenderForm(StringBuilder html, CrmForm form, Entity record, CrmEntity? entityMeta,
        SolutionMetadataService? metadata = null, IOrganizationServiceAsync? orgService = null, CrmApp? app = null)
    {
        // Horizontal tab strip (Dynamics-style) using hidden radio buttons for no-JS tab switching.
        // All radios, labels, and panels are flat siblings so CSS ~ combinator works.
        var formId = $"form_{form.Id:N}";
        html.AppendLine("<div class='form-tabs-wrapper'>");

        // Interleave: radio + label for each tab, then all panels after
        for (var i = 0; i < form.Tabs.Count; i++)
        {
            var tab = form.Tabs[i];
            var tabId = $"{formId}_tab{i}";
            var checkedAttr = i == 0 ? " checked" : "";
            html.AppendLine($"<input type='radio' name='_{formId}_tabs' id='{tabId}' class='form-tab-radio'{checkedAttr} />");
            html.AppendLine($"<label for='{tabId}' class='form-tab-label'>{Encode(tab.Label)}</label>");
        }

        // Tab content panels
        for (var i = 0; i < form.Tabs.Count; i++)
        {
            var tab = form.Tabs[i];

            html.AppendLine("<div class='form-tab-panel'>");

            var colCount = tab.Columns.Count;
            var colClass = colCount switch { 2 => "form-columns-2", 3 => "form-columns-3", _ => "" };
            html.AppendLine($"<div class='form-columns {colClass}'>");

            foreach (var col in tab.Columns)
            {
                html.AppendLine("<div>");
                foreach (var section in col.Sections)
                {
                    if (section.Fields.Count == 0) continue;
                    html.AppendLine("<div class='form-section'>");
                    if (section.ShowLabel && !string.IsNullOrEmpty(section.Label))
                        html.AppendLine($"<div class='form-section-header'>{Encode(section.Label)}</div>");

                    foreach (var field in section.Fields)
                    {
                        if (field.IsSubgrid)
                        {
                            await RenderSubgrid(html, field, record, metadata, orgService, app).ConfigureAwait(false);
                            continue;
                        }

                        if (string.IsNullOrEmpty(field.DataFieldName)) continue;

                        if (field.IsHidden)
                        {
                            var hiddenValue = record.Contains(field.DataFieldName) ? record[field.DataFieldName] : null;
                            html.AppendLine($"<input type='hidden' name='{Encode(field.DataFieldName)}' value='{Encode(hiddenValue?.ToString() ?? "")}' />");
                            continue;
                        }

                        var label = field.Label
                            ?? entityMeta?.Attributes.GetValueOrDefault(field.DataFieldName)?.DisplayName
                            ?? field.DataFieldName;
                        var attrType = entityMeta?.Attributes.GetValueOrDefault(field.DataFieldName)?.Type ?? "nvarchar";
                        var value = record.Contains(field.DataFieldName) ? record[field.DataFieldName] : null;

                        RenderFormField(html, field.DataFieldName, label, attrType, value);
                    }
                    html.AppendLine("</div>");
                }
                html.AppendLine("</div>");
            }

            html.AppendLine("</div>"); // form-columns
            html.AppendLine("</div>"); // form-tab-panel
        }

        html.AppendLine("</div>"); // form-tabs-wrapper
    }

    private static async Task RenderSubgrid(StringBuilder html, CrmFormField field, Entity parentRecord,
        SolutionMetadataService? metadata, IOrganizationServiceAsync? orgService, CrmApp? app)
    {
        var label = field.Label ?? field.SubgridEntityType ?? "Related Records";
        var targetEntity = field.SubgridEntityType;

        html.AppendLine("<div class='form-subgrid'>");
        html.AppendLine("<div class='subgrid-header'>");
        html.AppendLine($"<span class='subgrid-title'>{Encode(label)}</span>");

        if (targetEntity == null || orgService == null || metadata == null)
        {
            html.AppendLine("</div>");
            html.AppendLine("<div class='empty-grid'>Subgrid not available.</div>");
            html.AppendLine("</div>");
            return;
        }

        // Subgrid command bar from appactions (location=2 = SubGrid)
        if (app != null)
        {
            var actions = metadata.GetAppActions(targetEntity, CrmAppAction.LocationSubGrid, app.UniqueName);
            if (actions.Count > 0)
            {
                var subgridEntityMeta = metadata.GetEntity(targetEntity);
                var entityDisplayName = subgridEntityMeta?.DisplayName ?? targetEntity;
                var hasQuickCreate = metadata.GetQuickCreateForm(targetEntity) != null;

                html.AppendLine("<div class='subgrid-cmdbar'>");
                foreach (var action in actions.OrderBy(a => a.Sequence))
                {
                    if (action.Hidden) continue;
                    var actionLabel = ResolveCommandLabel(action.Label, entityDisplayName);
                    var isNewRecord = action.UniqueName.Contains("NewRecord", StringComparison.OrdinalIgnoreCase);

                    if (isNewRecord)
                    {
                        var parentCtx = $"&parentEntity={Encode(parentRecord.LogicalName)}&parentId={parentRecord.Id}";
                        if (!string.IsNullOrEmpty(field.SubgridRelationshipName))
                            parentCtx += $"&relationship={Encode(field.SubgridRelationshipName)}";
                        var newUrl = hasQuickCreate
                            ? $"/crm/{Encode(app.UniqueName)}/{Encode(targetEntity)}/new?formType=quick{parentCtx}"
                            : $"/crm/{Encode(app.UniqueName)}/{Encode(targetEntity)}/new?{parentCtx.TrimStart('&')}";
                        html.AppendLine($"<a href='{newUrl}' class='cmdbar-btn cmdbar-btn-primary' title='{Encode(actionLabel)}'>{Encode(actionLabel)}</a>");
                    }
                    else
                    {
                        html.AppendLine($"<span class='cmdbar-btn cmdbar-btn-custom' title='{Encode(actionLabel)}'>{Encode(actionLabel)}</span>");
                    }
                }
                html.AppendLine("</div>");
            }
        }

        // Try to get the view for column definitions
        CrmView? view = field.SubgridViewId.HasValue ? metadata.GetView(field.SubgridViewId.Value) : null;
        var entityMeta = metadata.GetEntity(targetEntity);

        // Build FetchXml to query related records
        var relationshipAttr = GuessRelationshipAttribute(field.SubgridRelationshipName, parentRecord.LogicalName);
        var fetchXml = view?.FetchXml;

        // If we have a view with FetchXml, inject the parent filter
        if (fetchXml != null && relationshipAttr != null)
        {
            try
            {
                var fetchDoc = System.Xml.Linq.XDocument.Parse(fetchXml);
                var entityEl = fetchDoc.Root?.Element("entity");
                if (entityEl != null)
                {
                    var filterEl = new System.Xml.Linq.XElement("filter",
                        new System.Xml.Linq.XAttribute("type", "and"),
                        new System.Xml.Linq.XElement("condition",
                            new System.Xml.Linq.XAttribute("attribute", relationshipAttr),
                            new System.Xml.Linq.XAttribute("operator", "eq"),
                            new System.Xml.Linq.XAttribute("value", parentRecord.Id.ToString())));
                    entityEl.Add(filterEl);
                    fetchXml = fetchDoc.ToString();
                }
            }
            catch
            {
                // Fall through to simple fetch
            }
        }
        else if (relationshipAttr != null)
        {
            // Build a simple FetchXml
            fetchXml = $@"<fetch top='10'><entity name='{targetEntity}'><all-attributes/><filter><condition attribute='{relationshipAttr}' operator='eq' value='{parentRecord.Id}'/></filter></entity></fetch>";
        }

        if (fetchXml == null)
        {
            html.AppendLine("</div>");
            html.AppendLine("<div class='empty-grid'>Cannot determine subgrid query.</div>");
            html.AppendLine("</div>");
            return;
        }

        try
        {
            var result = await orgService.RetrieveMultipleAsync(new FetchExpression(fetchXml)).ConfigureAwait(false);
            html.AppendLine($"<span class='subgrid-count'>({result.Entities.Count})</span>");
            html.AppendLine("</div>"); // subgrid-header

            // Determine columns from view or from returned data
            var columns = view?.Columns ?? [];
            if (columns.Count == 0 && result.Entities.Count > 0)
            {
                columns = result.Entities[0].Attributes.Keys
                    .Where(k => !k.EndsWith("id") || k == "name")
                    .Take(5)
                    .Select(k => new CrmViewColumn { Name = k, Width = 150 })
                    .ToList();
            }

            if (columns.Count > 0 && result.Entities.Count > 0)
            {
                html.AppendLine("<table class='subgrid-table'>");
                html.AppendLine("<thead><tr>");
                foreach (var col in columns)
                {
                    var header = entityMeta?.Attributes.GetValueOrDefault(col.Name)?.DisplayName ?? col.Name;
                    html.AppendLine($"<th>{Encode(header)}</th>");
                }
                html.AppendLine("</tr></thead>");
                html.AppendLine("<tbody>");
                foreach (var rec in result.Entities)
                {
                    html.AppendLine("<tr>");
                    var isFirst = true;
                    foreach (var col in columns)
                    {
                        var value = rec.Contains(col.Name) ? FormatGridValue(rec[col.Name]) : "";
                        if (isFirst && app != null)
                        {
                            html.AppendLine($"<td><a href='/crm/{Encode(app.UniqueName)}/{Encode(targetEntity)}/{rec.Id}'>{Encode(value)}</a></td>");
                            isFirst = false;
                        }
                        else
                        {
                            html.AppendLine($"<td>{Encode(value)}</td>");
                        }
                    }
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</tbody></table>");
            }
            else
            {
                html.AppendLine("<div class='empty-grid'>No records found.</div>");
            }
        }
        catch (Exception ex)
        {
            html.AppendLine("</div>"); // subgrid-header
            html.AppendLine($"<div class='empty-grid'>Error: {Encode(ex.Message)}</div>");
        }

        html.AppendLine("</div>"); // form-subgrid
    }

    private static string ResolveCommandLabel(string label, string entityDisplayName) =>
        label.Replace("{EntityLogicalNameMarker}", entityDisplayName);

    private static string? GuessRelationshipAttribute(string? relationshipName, string parentEntityName)
    {
        if (string.IsNullOrEmpty(relationshipName)) return null;

        // Common patterns: "contact_customer_accounts" -> parentcustomerid
        // "opportunity_parent_account" -> parentaccountid
        // "cr_customerrelation_account_cr_FirstCustomerId" -> cr_firstcustomerid
        // Try to extract the lookup attribute from the relationship name
        var parts = relationshipName.Split('_');

        // If the relationship name ends with an attribute-like name (contains "Id")
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i].EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
                // Rejoin from this part onwards as the attribute name
                return string.Join("_", parts.Skip(i)).ToLowerInvariant();
            }
        }

        // Fallback: try parententityid pattern
        return $"parent{parentEntityName}id";
    }

    private static void RenderFallbackForm(StringBuilder html, Entity record, CrmEntity? entityMeta)
    {
        html.AppendLine("<div class='form-tab'>");
        html.AppendLine("<div class='form-tab-header'>Record Details</div>");
        html.AppendLine("<div class='form-section'>");

        foreach (var attr in record.Attributes.OrderBy(a => a.Key))
        {
            var label = entityMeta?.Attributes.GetValueOrDefault(attr.Key)?.DisplayName ?? attr.Key;
            var attrType = entityMeta?.Attributes.GetValueOrDefault(attr.Key)?.Type ?? "nvarchar";
            RenderFormField(html, attr.Key, label, attrType, attr.Value);
        }

        html.AppendLine("</div>");
        html.AppendLine("</div>");
    }

    private static void RenderFallbackNewForm(StringBuilder html, CrmEntity? entityMeta)
    {
        html.AppendLine("<div class='form-tab'>");
        html.AppendLine("<div class='form-tab-header'>Record Details</div>");
        html.AppendLine("<div class='form-section'>");

        if (entityMeta != null)
        {
            // Show editable attributes, skip system fields
            foreach (var attr in entityMeta.Attributes.Values
                .Where(a => a.Type is not ("primarykey" or "uniqueidentifier" or "lookup" or "owner" or "customer"))
                .OrderBy(a => a.LogicalName))
            {
                RenderFormField(html, attr.LogicalName, attr.DisplayName ?? attr.LogicalName, attr.Type, null);
            }
        }

        html.AppendLine("</div>");
        html.AppendLine("</div>");
    }

    private static void RenderFormField(StringBuilder html, string fieldName, string label, string attrType, object? value)
    {
        html.AppendLine("<div class='form-field'>");
        html.AppendLine($"<label for='{Encode(fieldName)}'>{Encode(label)}</label>");

        switch (attrType.ToLowerInvariant())
        {
            case "memo":
                html.AppendLine($"<textarea name='{Encode(fieldName)}' id='{Encode(fieldName)}'>{Encode(FormatRawValue(value))}</textarea>");
                break;
            case "int":
            case "integer":
                html.AppendLine($"<input type='number' name='{Encode(fieldName)}' id='{Encode(fieldName)}' step='1' value='{Encode(FormatRawValue(value))}' />");
                break;
            case "decimal":
            case "float":
            case "double":
                html.AppendLine($"<input type='number' name='{Encode(fieldName)}' id='{Encode(fieldName)}' step='any' value='{Encode(FormatRawValue(value))}' />");
                break;
            case "money":
                var moneyVal = value is Money m ? m.Value.ToString() : FormatRawValue(value);
                html.AppendLine($"<input type='number' name='{Encode(fieldName)}' id='{Encode(fieldName)}' step='0.01' value='{Encode(moneyVal)}' />");
                break;
            case "datetime":
                var dtVal = value is DateTime dt ? dt.ToString("yyyy-MM-ddTHH:mm") : "";
                html.AppendLine($"<input type='datetime-local' name='{Encode(fieldName)}' id='{Encode(fieldName)}' value='{dtVal}' />");
                break;
            case "bit":
            case "boolean":
                var isChecked = value is bool b && b ? " checked" : "";
                html.AppendLine($"<input type='hidden' name='{Encode(fieldName)}' value='false' />");
                html.AppendLine($"<input type='checkbox' name='{Encode(fieldName)}' id='{Encode(fieldName)}' value='true'{isChecked} />");
                break;
            case "picklist":
            case "state":
            case "status":
                var optVal = value is OptionSetValue osv ? osv.Value.ToString() : "";
                html.AppendLine($"<input type='number' name='{Encode(fieldName)}' id='{Encode(fieldName)}' step='1' value='{Encode(optVal)}' />");
                break;
            case "lookup":
            case "owner":
            case "customer":
                if (value is EntityReference er)
                {
                    html.AppendLine($"<span class='readonly-value'><span class='lookup-link'>{Encode(er.Name ?? er.Id.ToString())}</span> ({Encode(er.LogicalName)})</span>");
                }
                else
                {
                    html.AppendLine("<span class='readonly-value'></span>");
                }
                break;
            case "primarykey":
            case "uniqueidentifier":
                html.AppendLine($"<span class='readonly-value'>{Encode(FormatRawValue(value))}</span>");
                break;
            default: // nvarchar, string, etc.
                html.AppendLine($"<input type='text' name='{Encode(fieldName)}' id='{Encode(fieldName)}' value='{Encode(FormatRawValue(value))}' />");
                break;
        }

        html.AppendLine("</div>");
    }

    private static string GetRecordName(Entity record, CrmEntity? entityMeta)
    {
        // Try common primary name attributes
        string[] primaryNameCandidates = ["name", "fullname", "subject", "title"];
        if (entityMeta?.PrimaryNameAttribute != null)
            primaryNameCandidates = [entityMeta.PrimaryNameAttribute, .. primaryNameCandidates];

        foreach (var attr in primaryNameCandidates)
        {
            if (record.Contains(attr) && record[attr] is string s && !string.IsNullOrEmpty(s))
                return s;
        }

        return record.Id.ToString();
    }

    private static string FormatGridValue(object? value) => value switch
    {
        null => "",
        EntityReference er => er.Name ?? er.Id.ToString(),
        OptionSetValue osv => osv.Value.ToString(),
        Money m => m.Value.ToString("N2"),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"),
        bool b => b ? "Yes" : "No",
        _ => value.ToString() ?? ""
    };

    private static string FormatRawValue(object? value) => value switch
    {
        null => "",
        EntityReference er => er.Name ?? er.Id.ToString(),
        OptionSetValue osv => osv.Value.ToString(),
        Money m => m.Value.ToString(),
        DateTime dt => dt.ToString("O"),
        bool b => b.ToString().ToLowerInvariant(),
        _ => value.ToString() ?? ""
    };

    private static object? ConvertToTypedValue(string rawValue, string attrType)
    {
        if (string.IsNullOrEmpty(rawValue)) return null;

        return attrType.ToLowerInvariant() switch
        {
            "int" or "integer" => int.TryParse(rawValue, out var i) ? i : null,
            "decimal" => decimal.TryParse(rawValue, out var d) ? d : null,
            "float" or "double" => double.TryParse(rawValue, out var f) ? f : null,
            "money" => decimal.TryParse(rawValue, out var m) ? new Money(m) : null,
            "datetime" => DateTime.TryParse(rawValue, out var dt) ? dt : null,
            "bit" or "boolean" => bool.TryParse(rawValue, out var b) ? b : null,
            "picklist" or "state" or "status" => int.TryParse(rawValue, out var osv) ? new OptionSetValue(osv) : null,
            _ => rawValue
        };
    }

    private async Task<Dictionary<string, string>> ReadFormDataAsync()
    {
        var form = await Request.ReadFormAsync().ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in form.Keys)
        {
            result[key] = form[key].ToString();
        }
        return result;
    }

    private CrmApp? FindApp(string appName) =>
        _metadata.Apps.FirstOrDefault(a => string.Equals(a.UniqueName, appName, StringComparison.OrdinalIgnoreCase));

    private static string Encode(string? value) =>
        WebUtility.HtmlEncode(value ?? "");

    /// <summary>
    /// Injects Xrm shim + form library scripts + onload handler calls into the page.
    /// </summary>
    private static void InjectFormScripts(StringBuilder html, CrmForm form, string appName, string entityName, string recordId)
    {
        if (form.Events.Count == 0 && form.Libraries.Count == 0) return;

        var onloadHandlers = form.Events
            .Where(e => e.EventName == "onload" && e.Enabled)
            .ToList();
        if (onloadHandlers.Count == 0) return;

        // Xrm shim + data attributes are already on body
        html.AppendLine("<script src='/crm/xrm-shim.js'></script>");

        // Load form libraries
        foreach (var lib in form.Libraries)
            html.AppendLine($"<script src='/crm/_webresource/{Encode(lib)}'></script>");

        // Call onload handlers
        html.AppendLine("<script>");
        html.AppendLine("document.addEventListener('DOMContentLoaded', function() {");
        foreach (var handler in onloadHandlers)
        {
            if (handler.PassExecutionContext)
                html.AppendLine($"  if (typeof {handler.FunctionName} === 'function') {handler.FunctionName}(window.__xrmExecutionContext);");
            else
                html.AppendLine($"  if (typeof {handler.FunctionName} === 'function') {handler.FunctionName}();");
        }
        html.AppendLine("});");
        html.AppendLine("</script>");
    }
}
