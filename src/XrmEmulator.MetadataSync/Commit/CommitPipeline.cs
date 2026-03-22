using System.ServiceModel;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;
using XrmEmulator.MetadataSync.Readers;
using XrmEmulator.MetadataSync.Writers;

namespace XrmEmulator.MetadataSync.Commit;

/// <summary>
/// Non-interactive commit pipeline extracted from HandleCommitCommand.
/// Callable programmatically (no Spectre.Console dependency).
/// </summary>
public static class CommitPipeline
{
    /// <summary>
    /// Scan _pending/ and return all discoverable commit items.
    /// </summary>
    public static List<CommitItem> DiscoverPendingItems(string pendingDir)
    {
        if (!Directory.Exists(pendingDir))
            return [];

        var pendingViewFiles = Directory.GetFiles(pendingDir, "*.xml", SearchOption.AllDirectories)
            .Where(f => f.Contains("SavedQueries", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingSiteMapFiles = Directory.GetFiles(pendingDir, "AppModuleSiteMap.xml", SearchOption.AllDirectories)
            .Where(f => f.Contains("AppModuleSiteMaps", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingEntityFiles = Directory.GetFiles(pendingDir, "Entity.xml", SearchOption.AllDirectories)
            .Where(f => f.Contains("Entities", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingIconFiles = Directory.GetFiles(pendingDir, "*.json", SearchOption.AllDirectories)
            .Where(f => (f.Contains(Path.Combine("Icons"), StringComparison.OrdinalIgnoreCase)
                || f.Contains("Icons/", StringComparison.OrdinalIgnoreCase)
                || f.Contains("Icons\\", StringComparison.OrdinalIgnoreCase))
                && !f.Contains("AppModuleViews", StringComparison.OrdinalIgnoreCase)
                && !f.Contains("AppModuleEntities", StringComparison.OrdinalIgnoreCase)
                && !f.Contains("AppModuleForms", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingAppModuleEntityFiles = Directory.GetFiles(pendingDir, "*.json", SearchOption.AllDirectories)
            .Where(f => f.Contains("AppModuleEntities", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingAppModuleViewFiles = Directory.GetFiles(pendingDir, "*.json", SearchOption.AllDirectories)
            .Where(f => f.Contains("AppModuleViews", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingFormFiles = Directory.GetFiles(pendingDir, "*.xml", SearchOption.AllDirectories)
            .Where(f => f.Contains("FormXml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingAppModuleFormFiles = Directory.GetFiles(pendingDir, "*.json", SearchOption.AllDirectories)
            .Where(f => f.Contains("AppModuleForms", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingBusinessRuleFiles = Directory.GetFiles(pendingDir, "*.xaml.data.xml", SearchOption.AllDirectories)
            .Where(f => f.Contains("Workflows", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingDeleteFiles = Directory.GetFiles(pendingDir, "*.delete.json", SearchOption.AllDirectories)
            .ToList();

        var pendingDeprecateFiles = Directory.GetFiles(pendingDir, "*.deprecate.json", SearchOption.AllDirectories)
            .ToList();

        var pendingNewAttributeFiles = Directory.GetFiles(pendingDir, "*.attribute.json", SearchOption.AllDirectories)
            .ToList();

        var pendingWebResourceFiles = Directory.GetFiles(pendingDir, "*.json", SearchOption.AllDirectories)
            .Where(f => f.Contains(Path.Combine("WebResources"), StringComparison.OrdinalIgnoreCase)
                || f.Contains("WebResources/", StringComparison.OrdinalIgnoreCase)
                || f.Contains("WebResources\\", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains("AppModule", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingCommandBarFiles = Directory.GetFiles(pendingDir, "*.xml", SearchOption.AllDirectories)
            .Where(f => f.Contains("appactions", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var commitItems = new List<CommitItem>();

        foreach (var f in pendingViewFiles)
        {
            var parsed = SavedQueryFileReader.Parse(f);
            var viewLabel = parsed.SavedQueryId == Guid.Empty
                ? $"View (new): {parsed.Name}"
                : $"View: {parsed.Name} ({parsed.SavedQueryId})";
            commitItems.Add(new CommitItem(CommitItemType.SavedQuery, viewLabel, f, parsed));
        }

        foreach (var f in pendingSiteMapFiles)
        {
            var folderName = Path.GetFileName(Path.GetDirectoryName(f))!;
            var parsed = SiteMapFileReader.Parse(f, folderName);
            commitItems.Add(new CommitItem(CommitItemType.SiteMap, $"SiteMap: {parsed.Name} ({parsed.UniqueName})", f, parsed));
        }

        foreach (var f in pendingEntityFiles)
        {
            var parsed = EntityFileReader.Parse(f);
            var customCount = parsed.Attributes.Count(a => a.IsCustomField);
            commitItems.Add(new CommitItem(CommitItemType.Entity, $"Entity: {parsed.DisplayName} ({customCount} custom fields)", f, parsed));
        }

        foreach (var f in pendingIconFiles)
        {
            var jsonContent = File.ReadAllText(f);
            if (f.EndsWith(".icon.json", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = JsonSerializer.Deserialize<IconSetDefinition>(jsonContent)!;
                commitItems.Add(new CommitItem(CommitItemType.IconSet,
                    $"Icon: {parsed.EntityLogicalName} → {parsed.IconVectorName}", f, parsed));
            }
            else
            {
                var parsed = JsonSerializer.Deserialize<IconUploadDefinition>(jsonContent)!;
                var label = $"Icon Upload: {parsed.WebResourceName}";
                if (parsed.EntityLogicalName != null)
                    label += $" (+ set on {parsed.EntityLogicalName})";
                commitItems.Add(new CommitItem(CommitItemType.IconUpload, label, f, parsed));
            }
        }

        foreach (var f in pendingAppModuleEntityFiles)
        {
            var jsonContent = File.ReadAllText(f);
            var parsed = JsonSerializer.Deserialize<AppModuleEntityDefinition>(jsonContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })!;
            commitItems.Add(new CommitItem(CommitItemType.AppModuleEntity,
                $"AppModule Entity: {parsed.AppModuleUniqueName} / {parsed.EntityLogicalName}",
                f, parsed));
        }

        foreach (var f in pendingAppModuleViewFiles)
        {
            var jsonContent = File.ReadAllText(f);
            if (PendingVariableResolver.HasVariables(f))
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;
                var appName = root.GetProperty("appModuleUniqueName").GetString() ?? "?";
                var entityName = root.GetProperty("entityLogicalName").GetString() ?? "?";
                var viewCount = root.GetProperty("viewIds").GetArrayLength();
                commitItems.Add(new CommitItem(CommitItemType.AppModuleView,
                    $"AppModule Views: {appName} / {entityName} ({viewCount} views)",
                    f, null!));
            }
            else
            {
                var parsed = JsonSerializer.Deserialize<AppModuleViewDefinition>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })!;
                commitItems.Add(new CommitItem(CommitItemType.AppModuleView,
                    $"AppModule Views: {parsed.AppModuleUniqueName} / {parsed.EntityLogicalName} ({parsed.ViewIds.Count} views)",
                    f, parsed));
            }
        }

        foreach (var f in pendingFormFiles)
        {
            var parsed = SystemFormFileReader.Parse(f);
            var formLabel = parsed.FormId == Guid.Empty
                ? $"Form (new): {parsed.Name}"
                : $"Form: {parsed.Name} ({parsed.FormId})";
            commitItems.Add(new CommitItem(CommitItemType.SystemForm, formLabel, f, parsed));
        }

        foreach (var f in pendingAppModuleFormFiles)
        {
            var jsonContent = File.ReadAllText(f);
            if (PendingVariableResolver.HasVariables(f))
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;
                var appName = root.GetProperty("appModuleUniqueName").GetString() ?? "?";
                var entityName = root.GetProperty("entityLogicalName").GetString() ?? "?";
                var formCount = root.GetProperty("formIds").GetArrayLength();
                commitItems.Add(new CommitItem(CommitItemType.AppModuleForm,
                    $"AppModule Forms: {appName} / {entityName} ({formCount} forms)",
                    f, null!));
            }
            else
            {
                var parsed = JsonSerializer.Deserialize<AppModuleFormDefinition>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })!;
                commitItems.Add(new CommitItem(CommitItemType.AppModuleForm,
                    $"AppModule Forms: {parsed.AppModuleUniqueName} / {parsed.EntityLogicalName} ({parsed.FormIds.Count} forms)",
                    f, parsed));
            }
        }

        foreach (var f in pendingBusinessRuleFiles)
        {
            var parsed = BusinessRuleFileReader.Parse(f);
            var brLabel = parsed.WorkflowId == Guid.Empty
                ? $"Business Rule (new): {parsed.Name}"
                : $"Business Rule: {parsed.Name} ({parsed.WorkflowId})";
            commitItems.Add(new CommitItem(CommitItemType.BusinessRule, brLabel, f, parsed));
        }

        foreach (var f in pendingDeleteFiles)
        {
            var parsed = JsonSerializer.Deserialize<DeleteDefinition>(File.ReadAllText(f), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })!;
            commitItems.Add(new CommitItem(CommitItemType.Delete,
                $"Delete {parsed.EntityType}: {parsed.DisplayName} ({parsed.ComponentId})", f, parsed));
        }

        foreach (var f in pendingDeprecateFiles)
        {
            var parsed = JsonSerializer.Deserialize<DeprecateDefinition>(File.ReadAllText(f), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })!;
            commitItems.Add(new CommitItem(CommitItemType.Deprecate,
                $"Deprecate: {parsed.EntityLogicalName}.{parsed.AttributeLogicalName} → \"{parsed.NewDisplayName}\"", f, parsed));
        }

        foreach (var f in pendingNewAttributeFiles)
        {
            var parsed = JsonSerializer.Deserialize<NewAttributeDefinition>(File.ReadAllText(f), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })!;
            var typeLabel = parsed.AttributeType == "lookup"
                ? $"Lookup → {parsed.TargetEntityLogicalName}"
                : parsed.AttributeType;
            commitItems.Add(new CommitItem(CommitItemType.NewAttribute,
                $"New Attribute: {parsed.EntityLogicalName}.{parsed.AttributeLogicalName} ({typeLabel})", f, parsed));
        }

        foreach (var f in pendingWebResourceFiles)
        {
            var parsed = JsonSerializer.Deserialize<WebResourceUploadDefinition>(File.ReadAllText(f))!;
            commitItems.Add(new CommitItem(CommitItemType.WebResourceUpload,
                $"WebResource Upload: {parsed.WebResourceName}", f, parsed));
        }

        foreach (var f in pendingCommandBarFiles)
        {
            var parsed = AppActionFileReader.Parse(f);
            commitItems.Add(new CommitItem(CommitItemType.CommandBar,
                $"CommandBar: {parsed.Label ?? parsed.Name ?? parsed.UniqueName} ({parsed.EntityLogicalName}, {parsed.UniqueName})", f, parsed));
        }

        // Ribbon Workbench actions (hide, etc.)
        var pendingRibbonFiles = Directory.GetFiles(pendingDir, "*.json", SearchOption.AllDirectories)
            .Where(f => f.Contains(Path.Combine("RibbonWorkbench"), StringComparison.OrdinalIgnoreCase)
                || f.Contains("RibbonWorkbench/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var f in pendingRibbonFiles)
        {
            var parsed = JsonSerializer.Deserialize<RibbonWorkbenchAction>(File.ReadAllText(f))!;
            var label = parsed.Action switch
            {
                "hide" => $"Ribbon Hide: {parsed.ButtonId} ({parsed.EntityLogicalName})",
                _ => $"Ribbon {parsed.Action}: {parsed.ButtonId} ({parsed.EntityLogicalName})"
            };
            commitItems.Add(new CommitItem(CommitItemType.RibbonWorkbench, label, f, parsed));
        }

        return commitItems;
    }

    /// <summary>
    /// Execute the commit for the selected items. Non-interactive — no Spectre.Console.
    /// Returns a CommitResult with committed items and optional failure info.
    /// </summary>
    public static CommitResult ExecuteCommit(
        IOrganizationService client,
        ConnectionMetadata metadata,
        string baseDir,
        List<CommitItem> selected,
        Action<string>? log = null,
        Action<string>? onPhaseChanged = null,
        Func<string, bool>? confirm = null)
    {
        var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending");
        var outputsPath = Path.Combine(pendingDir, "_outputs.json");
        var resolvedOutputs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // Resume detection
        if (File.Exists(outputsPath))
        {
            var previousOutputs = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                File.ReadAllText(outputsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (previousOutputs != null)
            {
                foreach (var kvp in previousOutputs)
                    resolvedOutputs[kvp.Key] = kvp.Value;
            }
            log?.Invoke($"Resumed from _outputs.json with {resolvedOutputs.Count} previously committed item(s).");
        }

        // Separate ribbon workbench items (processed as batch after other items)
        var ribbonWorkbenchItems = selected.Where(i => i.Type == CommitItemType.RibbonWorkbench).ToList();
        var regularItems = selected.Where(i => i.Type != CommitItemType.RibbonWorkbench).ToList();

        // Reorder by dependency graph
        var orderedItems = PendingVariableResolver.ReorderByDependencies(
            pendingDir, regularItems, item => item.FilePath);

        CommitItem? failedItem = null;
        Exception? failedException = null;
        var committedItems = new List<CommitItem>();
        var archivedGitPaths = new List<string>(); // Track files for targeted git commit

        foreach (var item in orderedItems)
        {
            var hasVars = PendingVariableResolver.HasVariables(item.FilePath);
            string? resolvedContent = hasVars
                ? PendingVariableResolver.ResolveFileContent(item.FilePath, resolvedOutputs)
                : null;

            var relativePath = Path.GetRelativePath(pendingDir, item.FilePath).Replace('\\', '/');

            try
            {
                switch (item.Type)
                {
                    case CommitItemType.SavedQuery:
                    {
                        var query = resolvedContent != null
                            ? SavedQueryFileReader.ParseFromString(resolvedContent, item.FilePath)
                            : (SavedQueryDefinition)item.ParsedData;

                        var isNew = query.SavedQueryId == Guid.Empty;
                        if (isNew)
                        {
                            var entityLogicalName = query.ReturnedTypeCode
                                ?? InferEntityFromPath(item.FilePath);
                            log?.Invoke($"Creating new view: {item.DisplayName} (entity: {entityLogicalName})");
                            var newId = SavedQueryWriter.Create(client, query, entityLogicalName, metadata.Solution.UniqueName);
                            log?.Invoke($"  View created OK. ID: {newId}");
                            resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = newId.ToString() };
                        }
                        else
                        {
                            log?.Invoke($"Updating view: {item.DisplayName}");
                            SavedQueryWriter.Update(client, query);
                            log?.Invoke($"  View updated OK.");
                            resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = query.SavedQueryId.ToString() };
                        }
                        break;
                    }

                    case CommitItemType.SystemForm:
                    {
                        var form = resolvedContent != null
                            ? SystemFormFileReader.ParseFromString(resolvedContent, item.FilePath)
                            : SystemFormFileReader.Parse(item.FilePath);

                        var isNew = form.FormId == Guid.Empty;
                        if (isNew)
                        {
                            var entityLogicalName = form.ObjectTypeCode
                                ?? InferEntityFromPath(item.FilePath);
                            log?.Invoke($"Creating new form: {item.DisplayName} (entity: {entityLogicalName})");
                            var newId = SystemFormWriter.Create(client, form, entityLogicalName, metadata.Solution.UniqueName);
                            log?.Invoke($"  Form created OK. ID: {newId}");
                            resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = newId.ToString() };
                        }
                        else
                        {
                            log?.Invoke($"Updating form: {item.DisplayName}");
                            SystemFormWriter.Update(client, form);
                            log?.Invoke($"  Form updated OK.");
                            resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = form.FormId.ToString() };
                        }
                        break;
                    }

                    case CommitItemType.SiteMap:
                    {
                        var siteMapDef = (SiteMapDefinition)item.ParsedData;
                        var contentToLoad = resolvedContent ?? File.ReadAllText(item.FilePath);
                        var doc = resolvedContent != null
                            ? XDocument.Parse(contentToLoad)
                            : XDocument.Load(item.FilePath);
                        var siteMapElement = doc.Root?.Descendants("SiteMap").FirstOrDefault()
                            ?? doc.Root?.Descendants("sitemap").FirstOrDefault();
                        var siteMapXml = siteMapElement?.ToString() ?? string.Concat(doc.Root!.Nodes());

                        log?.Invoke($"Updating sitemap: {siteMapDef.UniqueName}");
                        var updatedDef = siteMapDef with { SiteMapXml = siteMapXml };
                        SiteMapWriter.Update(client, updatedDef);
                        log?.Invoke($"  SiteMap updated OK.");
                        break;
                    }

                    case CommitItemType.Entity:
                    {
                        var pendingEntity = (EntityDefinition)item.ParsedData;
                        log?.Invoke($"Updating entity: {pendingEntity.LogicalName}");
                        var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
                        var solutionFolder = GetSolutionFolder(solutionExportDir);
                        var snapshotRelative = Path.GetRelativePath(pendingDir, item.FilePath);
                        var snapshotPath = Path.Combine(solutionFolder, snapshotRelative);

                        if (!File.Exists(snapshotPath))
                            throw new InvalidOperationException($"Snapshot not found for comparison: {snapshotPath}");

                        var currentPending = EntityFileReader.Parse(item.FilePath);
                        var snapshot = EntityFileReader.Parse(snapshotPath);

                        var changes = EntityWriter.UpdateChangedAttributes(client, currentPending.LogicalName, currentPending, snapshot);
                        log?.Invoke($"  Entity attribute changes: [{string.Join(", ", changes)}]");
                        break;
                    }

                    case CommitItemType.IconUpload:
                    {
                        var def = (IconUploadDefinition)item.ParsedData;
                        log?.Invoke($"Uploading web resource: {def.WebResourceName}");
                        var svgPath = Path.Combine(Path.GetDirectoryName(item.FilePath)!, def.SvgFile);
                        var base64 = Convert.ToBase64String(File.ReadAllBytes(svgPath));
                        var created = IconWriter.UploadWebResource(client, def.WebResourceName, def.DisplayName, base64, metadata.Solution.UniqueName);
                        log?.Invoke($"  Web resource {(created ? "created" : "updated")}.");

                        if (def.EntityLogicalName != null)
                        {
                            log?.Invoke($"  Setting IconVectorName on {def.EntityLogicalName}");
                            IconWriter.SetEntityIcon(client, def.EntityLogicalName, def.WebResourceName);
                            log?.Invoke($"  Entity icon set OK.");
                        }
                        break;
                    }

                    case CommitItemType.IconSet:
                    {
                        var def = (IconSetDefinition)item.ParsedData;
                        log?.Invoke($"Setting IconVectorName on {def.EntityLogicalName} → {def.IconVectorName}");
                        IconWriter.SetEntityIcon(client, def.EntityLogicalName, def.IconVectorName);
                        log?.Invoke($"  Entity icon set OK.");
                        break;
                    }

                    case CommitItemType.AppModuleEntity:
                    {
                        var def = (AppModuleEntityDefinition)item.ParsedData;
                        log?.Invoke($"Adding entity to AppModule: {def.AppModuleUniqueName} / {def.EntityLogicalName}");
                        AppModuleWriter.AddEntity(client, def.AppModuleUniqueName, def.EntityLogicalName);
                        log?.Invoke($"  Entity added OK.");

                        if (def.IncludeAllViews)
                        {
                            var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
                            var entityFolderName = FindEntityFolderName(solutionExportDir, def.EntityLogicalName);
                            var viewCandidates = ScanLocalViewsForEntity(solutionExportDir, entityFolderName, def.EntityLogicalName);

                            if (viewCandidates.Count > 0)
                            {
                                var viewIds = viewCandidates.Select(v => v.Id).ToList();
                                log?.Invoke($"  Adding {viewIds.Count} views for {def.EntityLogicalName}");
                                AppModuleWriter.UpdateViewSelection(client, def.AppModuleUniqueName, viewIds, []);
                                log?.Invoke($"  Views added OK.");
                            }
                        }
                        break;
                    }

                    case CommitItemType.AppModuleView:
                    {
                        var jsonContent = resolvedContent ?? File.ReadAllText(item.FilePath);
                        var def = JsonSerializer.Deserialize<AppModuleViewDefinition>(jsonContent, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        })!;
                        log?.Invoke($"Updating AppModule views: {def.AppModuleUniqueName} / {def.EntityLogicalName}");

                        var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
                        var appModules = DiscoverAppModules(solutionExportDir);
                        var appMatch = appModules.FirstOrDefault(a =>
                            a.UniqueName.Equals(def.AppModuleUniqueName, StringComparison.OrdinalIgnoreCase));

                        var currentIds = appMatch != default
                            ? ReadAppModuleViewIds(appMatch.XmlPath)
                            : new HashSet<Guid>();

                        var desiredIds = new HashSet<Guid>(def.ViewIds);
                        var toAdd = desiredIds.Except(currentIds).ToList();
                        var toRemove = currentIds.Except(desiredIds).ToList();

                        if (toAdd.Count > 0 || toRemove.Count > 0)
                        {
                            log?.Invoke($"  Adding {toAdd.Count} views, removing {toRemove.Count} views");
                            AppModuleWriter.UpdateViewSelection(client, def.AppModuleUniqueName, toAdd, toRemove);
                            log?.Invoke($"  AppModule views updated OK.");
                        }
                        else
                        {
                            log?.Invoke($"  No changes needed for {def.AppModuleUniqueName}.");
                        }
                        break;
                    }

                    case CommitItemType.AppModuleForm:
                    {
                        var jsonContent = resolvedContent ?? File.ReadAllText(item.FilePath);
                        var def = JsonSerializer.Deserialize<AppModuleFormDefinition>(jsonContent, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        })!;
                        log?.Invoke($"Updating AppModule forms: {def.AppModuleUniqueName} / {def.EntityLogicalName}");

                        var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
                        var appModules = DiscoverAppModules(solutionExportDir);
                        var appMatch = appModules.FirstOrDefault(a =>
                            a.UniqueName.Equals(def.AppModuleUniqueName, StringComparison.OrdinalIgnoreCase));

                        var currentIds = appMatch != default
                            ? ReadAppModuleFormIds(appMatch.XmlPath)
                            : new HashSet<Guid>();

                        var desiredIds = new HashSet<Guid>(def.FormIds);
                        var toAdd = desiredIds.Except(currentIds).ToList();
                        var toRemove = currentIds.Except(desiredIds).ToList();

                        if (toAdd.Count > 0 || toRemove.Count > 0)
                        {
                            log?.Invoke($"  Adding {toAdd.Count} forms, removing {toRemove.Count} forms");
                            AppModuleWriter.UpdateFormSelection(client, def.AppModuleUniqueName, toAdd, toRemove);
                            log?.Invoke($"  AppModule forms updated OK.");
                        }
                        else
                        {
                            log?.Invoke($"  No changes needed for {def.AppModuleUniqueName}.");
                        }
                        break;
                    }

                    case CommitItemType.BusinessRule:
                    {
                        var rule = resolvedContent != null
                            ? BusinessRuleFileReader.ParseFromString(resolvedContent, item.FilePath)
                            : (BusinessRuleDefinition)item.ParsedData;

                        var isNew = rule.WorkflowId == Guid.Empty;
                        if (isNew)
                        {
                            // Check for existing business rule with the same name
                            var existingId = BusinessRuleWriter.FindExistingByName(client, rule.Name, rule.PrimaryEntity);
                            if (existingId.HasValue)
                            {
                                var shouldOverride = confirm?.Invoke(
                                    $"A business rule named '{rule.Name}' already exists on {rule.PrimaryEntity} (ID: {existingId.Value}). Override it?")
                                    ?? true; // Default to yes if no confirm callback (non-interactive)

                                if (!shouldOverride)
                                {
                                    log?.Invoke($"  Skipped: user chose not to override existing business rule.");
                                    break;
                                }

                                log?.Invoke($"Updating existing business rule: {rule.Name} ({existingId.Value})");
                                var updated = rule with { WorkflowId = existingId.Value };
                                BusinessRuleWriter.Update(client, updated);
                                log?.Invoke($"  Business rule updated OK. ID: {existingId.Value}");
                                resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = existingId.Value.ToString() };
                            }
                            else
                            {
                                log?.Invoke($"Creating new business rule: {item.DisplayName} (entity: {rule.PrimaryEntity})");
                                var newId = BusinessRuleWriter.Create(client, rule, metadata.Solution.UniqueName);
                                log?.Invoke($"  Business rule created OK. ID: {newId}");
                                resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = newId.ToString() };
                            }
                        }
                        else
                        {
                            log?.Invoke($"Updating business rule: {item.DisplayName}");
                            BusinessRuleWriter.Update(client, rule);
                            log?.Invoke($"  Business rule updated OK.");
                            resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = rule.WorkflowId.ToString() };
                        }
                        break;
                    }

                    case CommitItemType.Delete:
                    {
                        var def = (DeleteDefinition)item.ParsedData;
                        var componentId = def.ComponentId;

                        // Resolve uniquename → GUID at commit time if no GUID provided
                        if (componentId == Guid.Empty && !string.IsNullOrEmpty(def.UniqueName))
                        {
                            log?.Invoke($"Resolving {def.EntityType} uniquename '{def.UniqueName}' to GUID...");
                            var query = new QueryExpression(def.EntityType)
                            {
                                ColumnSet = new ColumnSet(false),
                                Criteria = new FilterExpression
                                {
                                    Conditions =
                                    {
                                        new ConditionExpression("uniquename", ConditionOperator.Equal, def.UniqueName)
                                    }
                                }
                            };
                            var result = client.RetrieveMultiple(query).Entities.FirstOrDefault()
                                ?? throw new InvalidOperationException(
                                    $"Cannot find {def.EntityType} with uniquename '{def.UniqueName}' in CRM.");
                            componentId = result.Id;
                            log?.Invoke($"  Resolved to {componentId}");
                        }

                        log?.Invoke($"Deleting {def.EntityType}: {def.DisplayName} ({componentId})");
                        try
                        {
                            client.Delete(def.EntityType, componentId);
                            log?.Invoke($"  Deleted OK.");
                        }
                        catch (Exception ex)
                        {
                            var detail = ex is FaultException<OrganizationServiceFault> fault
                                ? fault.Detail.Message
                                : ex.Message;
                            var message = $"Failed to delete {def.EntityType} '{def.DisplayName}' ({componentId}): {detail}";
                            throw new InvalidOperationException(message, ex);
                        }
                        break;
                    }

                    case CommitItemType.Deprecate:
                    {
                        var def = (DeprecateDefinition)item.ParsedData;
                        log?.Invoke($"Deprecating {def.EntityLogicalName}.{def.AttributeLogicalName}: \"{def.OriginalDisplayName}\" → \"{def.NewDisplayName}\"");

                        // Retrieve current attribute to get its type, then update DisplayName
                        var retrieveReq = new RetrieveAttributeRequest
                        {
                            EntityLogicalName = def.EntityLogicalName,
                            LogicalName = def.AttributeLogicalName
                        };
                        var retrieveResp = (RetrieveAttributeResponse)client.Execute(retrieveReq);
                        var attrMetadata = retrieveResp.AttributeMetadata;
                        attrMetadata.DisplayName = new Label(def.NewDisplayName, 1030);

                        var updateReq = new UpdateAttributeRequest
                        {
                            EntityName = def.EntityLogicalName,
                            Attribute = attrMetadata
                        };
                        client.Execute(updateReq);
                        log?.Invoke($"  Attribute deprecated OK.");
                        break;
                    }

                    case CommitItemType.NewAttribute:
                    {
                        var def = (NewAttributeDefinition)item.ParsedData;
                        var requiredLevel = def.RequiredLevel?.ToLowerInvariant() switch
                        {
                            "required" => AttributeRequiredLevel.ApplicationRequired,
                            "recommended" => AttributeRequiredLevel.Recommended,
                            _ => AttributeRequiredLevel.None
                        };

                        if (def.AttributeType.Equals("lookup", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetEntity = def.TargetEntityLogicalName
                                ?? throw new InvalidOperationException("targetEntityLogicalName is required for lookup attributes");

                            log?.Invoke($"Creating lookup: {def.EntityLogicalName}.{def.AttributeLogicalName} → {targetEntity}");

                            var lookupAttr = new LookupAttributeMetadata
                            {
                                SchemaName = def.AttributeSchemaName,
                                DisplayName = new Label(def.DisplayName, 1030),
                                RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
                                Description = string.IsNullOrEmpty(def.Description) ? new Label() : new Label(def.Description, 1030)
                            };

                            var relationship = new OneToManyRelationshipMetadata
                            {
                                SchemaName = def.RelationshipSchemaName
                                    ?? $"{def.AttributeLogicalName}_{def.EntityLogicalName}",
                                ReferencedEntity = targetEntity,
                                ReferencingEntity = def.EntityLogicalName,
                                CascadeConfiguration = new CascadeConfiguration
                                {
                                    Assign = CascadeType.NoCascade,
                                    Delete = CascadeType.RemoveLink,
                                    Merge = CascadeType.NoCascade,
                                    Reparent = CascadeType.NoCascade,
                                    Share = CascadeType.NoCascade,
                                    Unshare = CascadeType.NoCascade
                                }
                            };

                            var createReq = new CreateOneToManyRequest
                            {
                                OneToManyRelationship = relationship,
                                Lookup = lookupAttr
                            };
                            createReq.Parameters["SolutionUniqueName"] = def.SolutionUniqueName;

                            var resp = (CreateOneToManyResponse)client.Execute(createReq);
                            log?.Invoke($"  Lookup created OK. Attribute ID: {resp.AttributeId}, Relationship ID: {resp.RelationshipId}");
                        }
                        else
                        {
                            log?.Invoke($"Creating attribute: {def.EntityLogicalName}.{def.AttributeLogicalName} ({def.AttributeType})");

                            AttributeMetadata attrMeta = def.AttributeType.ToLowerInvariant() switch
                            {
                                "string" => new StringAttributeMetadata
                                {
                                    SchemaName = def.AttributeSchemaName,
                                    MaxLength = def.MaxLength ?? 100,
                                    FormatName = StringFormatName.Text,
                                    DisplayName = new Label(def.DisplayName, 1030),
                                    RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
                                    Description = string.IsNullOrEmpty(def.Description) ? new Label() : new Label(def.Description, 1030)
                                },
                                "memo" => new MemoAttributeMetadata
                                {
                                    SchemaName = def.AttributeSchemaName,
                                    MaxLength = def.MaxLength ?? 2000,
                                    DisplayName = new Label(def.DisplayName, 1030),
                                    RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
                                    Description = string.IsNullOrEmpty(def.Description) ? new Label() : new Label(def.Description, 1030)
                                },
                                "int" or "integer" => new IntegerAttributeMetadata
                                {
                                    SchemaName = def.AttributeSchemaName,
                                    MinValue = int.MinValue,
                                    MaxValue = int.MaxValue,
                                    DisplayName = new Label(def.DisplayName, 1030),
                                    RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
                                    Description = string.IsNullOrEmpty(def.Description) ? new Label() : new Label(def.Description, 1030)
                                },
                                "decimal" => new DecimalAttributeMetadata
                                {
                                    SchemaName = def.AttributeSchemaName,
                                    Precision = 2,
                                    DisplayName = new Label(def.DisplayName, 1030),
                                    RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
                                    Description = string.IsNullOrEmpty(def.Description) ? new Label() : new Label(def.Description, 1030)
                                },
                                "boolean" => new BooleanAttributeMetadata
                                {
                                    SchemaName = def.AttributeSchemaName,
                                    DisplayName = new Label(def.DisplayName, 1030),
                                    RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
                                    Description = string.IsNullOrEmpty(def.Description) ? new Label() : new Label(def.Description, 1030)
                                },
                                "datetime" => new DateTimeAttributeMetadata
                                {
                                    SchemaName = def.AttributeSchemaName,
                                    Format = DateTimeFormat.DateOnly,
                                    DisplayName = new Label(def.DisplayName, 1030),
                                    RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel),
                                    Description = string.IsNullOrEmpty(def.Description) ? new Label() : new Label(def.Description, 1030)
                                },
                                _ => throw new InvalidOperationException($"Unsupported attribute type: {def.AttributeType}")
                            };

                            var createReq = new CreateAttributeRequest
                            {
                                EntityName = def.EntityLogicalName,
                                Attribute = attrMeta
                            };
                            createReq.Parameters["SolutionUniqueName"] = def.SolutionUniqueName;

                            var resp = (CreateAttributeResponse)client.Execute(createReq);
                            log?.Invoke($"  Attribute created OK. ID: {resp.AttributeId}");
                        }
                        break;
                    }

                    case CommitItemType.WebResourceUpload:
                    {
                        var def = (WebResourceUploadDefinition)item.ParsedData;
                        log?.Invoke($"Uploading web resource: {def.WebResourceName} (type {def.WebResourceType})");
                        var resourcePath = Path.Combine(Path.GetDirectoryName(item.FilePath)!, def.ResourceFile);
                        var base64 = Convert.ToBase64String(File.ReadAllBytes(resourcePath));
                        var created = IconWriter.UploadWebResource(client, def.WebResourceName, def.DisplayName, base64, metadata.Solution.UniqueName, def.WebResourceType);
                        log?.Invoke($"  Web resource {(created ? "created" : "updated")}.");
                        break;
                    }

                    case CommitItemType.CommandBar:
                    {
                        var def = (CommandBarDefinition)item.ParsedData;
                        log?.Invoke($"Creating/updating command bar button: {def.UniqueName}");
                        var doc = XDocument.Load(item.FilePath);
                        var newId = CommandBarWriter.CreateOrUpdateFromXml(client, doc, metadata.Solution.UniqueName);
                        log?.Invoke($"  Command bar button OK. ID: {newId}");
                        resolvedOutputs[relativePath] = new Dictionary<string, string> { ["id"] = newId.ToString() };

                        // Hide legacy ribbon buttons via solution import (only for classic ribbon IDs)
                        var hideLegacy = CommandBarWriter.ReadHideLegacyButtons(doc);
                        if (hideLegacy?.Count > 0)
                        {
                            var entityLogicalName = def.EntityLogicalName
                                ?? throw new InvalidOperationException("entityLogicalName required for ribbon hide");

                            // Validate button IDs against Ribbon/ export — only classic ribbon IDs are valid
                            var ribbonDir = Path.Combine(baseDir, "Ribbon");
                            var validatedIds = ValidateRibbonButtonIds(hideLegacy, entityLogicalName, ribbonDir, log);

                            if (validatedIds.Count > 0)
                            {
                                onPhaseChanged?.Invoke("Importing ribbon changes...");
                                log?.Invoke($"  Hiding legacy buttons: {string.Join(", ", validatedIds)}");

                                try
                                {
                                    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
                                    var solutionFolder = GetSolutionFolder(solutionExportDir);
                                    var solutionXmlPath = Path.Combine(solutionFolder, "Other", "Solution.xml");
                                    var solutionXmlContent = File.ReadAllText(solutionXmlPath);

                                    var solDoc = XDocument.Parse(solutionXmlContent);
                                    var publisherPrefix = solDoc.Descendants("CustomizationPrefix").FirstOrDefault()?.Value
                                        ?? throw new InvalidOperationException("Cannot find CustomizationPrefix in Solution.xml");

                                    var entityFolderName = FindEntityFolderName(solutionExportDir, entityLogicalName);
                                    var ribbonDiffPath = Path.Combine(solutionFolder, "Entities", entityFolderName, "RibbonDiff.xml");
                                    var existingRibbonDiffXml = File.Exists(ribbonDiffPath) ? File.ReadAllText(ribbonDiffPath) : null;

                                    RibbonImportWriter.ImportHideActions(
                                        client,
                                        metadata.Solution.UniqueName,
                                        publisherPrefix,
                                        entityLogicalName,
                                        entityFolderName,
                                        validatedIds,
                                        existingRibbonDiffXml,
                                        solutionXmlContent);

                                    log?.Invoke($"  Ribbon hide actions imported for {entityFolderName}.");
                                }
                                catch (Exception ribbonEx)
                                {
                                    log?.Invoke($"  WARNING: Ribbon import failed for {def.EntityLogicalName}: {ribbonEx.Message}");
                                }
                            }
                        }
                        break;
                    }
                }

                // Archive immediately after successful CRM call
                MoveToCommitted(item.FilePath, pendingDir, baseDir, archivedGitPaths);

                // Move companion files
                if (item.Type == CommitItemType.BusinessRule)
                {
                    var xamlPath = item.FilePath[..^".data.xml".Length];
                    if (File.Exists(xamlPath))
                        MoveToCommitted(xamlPath, pendingDir, baseDir, archivedGitPaths);
                }
                if (item.Type == CommitItemType.IconUpload)
                {
                    var iconDef = (IconUploadDefinition)item.ParsedData;
                    var svgPath = Path.Combine(Path.GetDirectoryName(item.FilePath)!, iconDef.SvgFile);
                    if (File.Exists(svgPath))
                        MoveToCommitted(svgPath, pendingDir, baseDir, archivedGitPaths);
                }
                if (item.Type == CommitItemType.WebResourceUpload)
                {
                    var wrDef = (WebResourceUploadDefinition)item.ParsedData;
                    var resourcePath = Path.Combine(Path.GetDirectoryName(item.FilePath)!, wrDef.ResourceFile);
                    if (File.Exists(resourcePath))
                        MoveToCommitted(resourcePath, pendingDir, baseDir, archivedGitPaths);
                }

                // Persist outputs for crash-resilient resume
                if (!resolvedOutputs.ContainsKey(relativePath))
                    resolvedOutputs[relativePath] = new Dictionary<string, string>();
                WriteOutputsFile(outputsPath, resolvedOutputs);
                log?.Invoke($"  Archived to _committed/");
                committedItems.Add(item);
            }
            catch (Exception ex)
            {
                failedItem = item;
                failedException = ex;
                log?.Invoke($"  FAILED: {ex}");
                break; // Stop processing — later items may depend on this one
            }
        }

        // Process ribbon workbench items (grouped by entity, one import per entity)
        if (ribbonWorkbenchItems.Count > 0 && failedItem == null)
        {
            onPhaseChanged?.Invoke("Importing ribbon changes...");
            var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
            var solutionFolder = GetSolutionFolder(solutionExportDir);
            var solutionXmlPath = Path.Combine(solutionFolder, "Other", "Solution.xml");
            var solutionXmlContent = File.ReadAllText(solutionXmlPath);
            var solDoc = XDocument.Parse(solutionXmlContent);
            var publisherPrefix = solDoc.Descendants("CustomizationPrefix").FirstOrDefault()?.Value
                ?? throw new InvalidOperationException("Cannot find CustomizationPrefix in Solution.xml");

            // Group by entity
            var byEntity = ribbonWorkbenchItems
                .Select(i => ((RibbonWorkbenchAction)i.ParsedData, Item: i))
                .GroupBy(x => x.Item1.EntityLogicalName, StringComparer.OrdinalIgnoreCase);

            foreach (var entityGroup in byEntity)
            {
                var entityLogicalName = entityGroup.Key;
                var hideButtons = entityGroup
                    .Where(x => x.Item1.Action == "hide")
                    .Select(x => x.Item1.ButtonId)
                    .ToList();

                if (hideButtons.Count == 0) continue;

                try
                {
                    var entityFolderName = FindEntityFolderName(solutionExportDir, entityLogicalName);
                    var ribbonDiffPath = Path.Combine(solutionFolder, "Entities", entityFolderName, "RibbonDiff.xml");
                    var existingRibbonDiffXml = File.Exists(ribbonDiffPath) ? File.ReadAllText(ribbonDiffPath) : null;

                    log?.Invoke($"Importing ribbon hides for {entityLogicalName}: {string.Join(", ", hideButtons)}");
                    RibbonImportWriter.ImportHideActions(
                        client,
                        metadata.Solution.UniqueName,
                        publisherPrefix,
                        entityLogicalName,
                        entityFolderName,
                        hideButtons,
                        existingRibbonDiffXml,
                        solutionXmlContent);
                    log?.Invoke($"  Ribbon import OK for {entityFolderName}.");

                    // Archive all items in this entity group
                    foreach (var (_, item) in entityGroup)
                    {
                        MoveToCommitted(item.FilePath, pendingDir, baseDir, archivedGitPaths);
                        committedItems.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    failedItem = entityGroup.First().Item;
                    failedException = ex;
                    log?.Invoke($"  FAILED ribbon import for {entityLogicalName}: {ex.Message}");
                }
            }
        }

        // Publish + Re-export only if all items succeeded
        if (committedItems.Count > 0 && failedItem == null)
        {
            onPhaseChanged?.Invoke("Publishing customizations...");
            log?.Invoke("Publishing all customizations...");
            SavedQueryWriter.PublishAll(client);
            log?.Invoke("  Published OK.");

            onPhaseChanged?.Invoke("Re-exporting solution...");
            log?.Invoke("Re-exporting solution...");
            SolutionExporter.Export(client, metadata.Solution.UniqueName, baseDir);
            log?.Invoke("  Re-export OK.");
        }

        // Git commit after re-export (non-fatal)
        var solutionExportForGit = Path.Combine(baseDir, "SolutionExport");
        if (committedItems.Count > 0 && Git.GitHelper.IsGitRepo(solutionExportForGit))
        {
            try
            {
                var itemNames = string.Join(", ", committedItems.Select(s => s.DisplayName));
                Git.GitHelper.CommitAll(solutionExportForGit, $"Post-commit re-export: {itemNames}");
            }
            catch { /* non-fatal */ }
        }

        // Session complete — remove outputs file
        if (failedItem == null && File.Exists(outputsPath))
            File.Delete(outputsPath);

        // Clean up empty directories
        CleanEmptyDirectories(pendingDir);

        // Git archive commit — only commit files that were actually archived (not failed items still in _pending/)
        if (archivedGitPaths.Count > 0 && Git.GitHelper.IsGitRepo(solutionExportForGit))
        {
            try
            {
                Git.GitHelper.CommitFiles(solutionExportForGit, archivedGitPaths.ToArray(), "Archived verified changes to _committed/");
            }
            catch { /* non-fatal */ }
        }

        return new CommitResult(committedItems, failedItem, failedException);
    }

    // ── Helper methods (duplicated from Program.cs to avoid coupling) ──

    internal static string InferEntityFromPath(string filePath)
    {
        var parts = filePath.Replace('\\', '/').Split('/');
        for (var i = 0; i < parts.Length - 2; i++)
        {
            if (parts[i].Equals("Entities", StringComparison.OrdinalIgnoreCase)
                && i + 2 < parts.Length
                && (parts[i + 2].Equals("SavedQueries", StringComparison.OrdinalIgnoreCase)
                    || parts[i + 2].Equals("FormXml", StringComparison.OrdinalIgnoreCase)))
            {
                return parts[i + 1].ToLowerInvariant();
            }
        }
        throw new InvalidOperationException(
            $"Cannot infer entity logical name from path: {filePath}. Add <returnedtypecode> to the XML.");
    }

    internal static string GetSolutionFolder(string solutionExportDir)
    {
        return Directory.GetDirectories(solutionExportDir)
            .FirstOrDefault(d =>
            {
                var name = Path.GetFileName(d);
                return !name.StartsWith('.') && !name.StartsWith('_');
            })
            ?? throw new InvalidOperationException("No solution folder found in SolutionExport/");
    }

    /// <summary>
    /// Validates hideLegacyButtons IDs against the Ribbon/ export folder.
    /// Only classic ribbon button IDs (found in Ribbon/*.xml) are valid for hiding via solution import.
    /// Returns the list of validated IDs; logs warnings for any rejected IDs.
    /// </summary>
    internal static List<string> ValidateRibbonButtonIds(
        List<string> buttonIds,
        string entityLogicalName,
        string ribbonDir,
        Action<string>? log = null)
    {
        var validated = new List<string>();

        // Load known ribbon button IDs from Ribbon/<entity>.xml if available
        HashSet<string>? knownIds = null;
        var ribbonFile = Path.Combine(ribbonDir, $"{entityLogicalName}.xml");
        if (File.Exists(ribbonFile))
        {
            try
            {
                var ribbonXml = XDocument.Load(ribbonFile);
                knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in ribbonXml.Descendants())
                {
                    var id = el.Attribute("Id")?.Value;
                    if (!string.IsNullOrEmpty(id))
                        knownIds.Add(id);
                }
            }
            catch
            {
                // If we can't parse the ribbon file, fall back to pattern matching
            }
        }

        foreach (var buttonId in buttonIds)
        {
            // Classic ribbon IDs start with "Mscrm." — always valid pattern
            if (buttonId.StartsWith("Mscrm.", StringComparison.OrdinalIgnoreCase))
            {
                // If we have a ribbon export, validate the ID exists
                if (knownIds != null && !knownIds.Contains(buttonId))
                {
                    log?.Invoke($"  WARNING: Button ID '{buttonId}' not found in Ribbon/{entityLogicalName}.xml — skipping ribbon hide.");
                    continue;
                }
                validated.Add(buttonId);
            }
            else
            {
                // Non-Mscrm IDs are modern appaction names — cannot be hidden via ribbon import
                log?.Invoke($"  WARNING: '{buttonId}' is not a classic ribbon button ID — skipping ribbon hide. " +
                    "Only classic ribbon IDs (Mscrm.*) can be hidden via hideLegacyButtons.");
            }
        }

        return validated;
    }

    internal static string FindEntityFolderName(string solutionExportDir, string entityLogicalName)
    {
        if (!Directory.Exists(solutionExportDir))
            return ToPascalCase(entityLogicalName);

        foreach (var solDir in Directory.GetDirectories(solutionExportDir))
        {
            var dirName = Path.GetFileName(solDir);
            if (dirName.StartsWith('.') || dirName.StartsWith('_')) continue;

            var entitiesDir = Path.Combine(solDir, "Entities");
            if (!Directory.Exists(entitiesDir)) continue;

            foreach (var entityDir in Directory.GetDirectories(entitiesDir))
            {
                var folderName = Path.GetFileName(entityDir);
                if (folderName.Equals(entityLogicalName, StringComparison.OrdinalIgnoreCase))
                    return folderName;
            }
        }

        return ToPascalCase(entityLogicalName);
    }

    internal static List<(Guid Id, string Name)> ScanLocalViewsForEntity(
        string solutionExportDir, string entityFolderName, string entityLogicalName)
    {
        var views = new Dictionary<Guid, string>();
        if (!Directory.Exists(solutionExportDir)) return views.Select(kv => (kv.Key, kv.Value)).ToList();

        foreach (var dir in Directory.GetDirectories(solutionExportDir))
        {
            var dirName = Path.GetFileName(dir);
            if (!dirName.StartsWith('.'))
                ScanSavedQueriesInDir(dir, entityFolderName, views);
        }

        var pendingDir = Path.Combine(solutionExportDir, "_pending");
        if (Directory.Exists(pendingDir))
            ScanSavedQueriesInDir(pendingDir, entityFolderName, views);

        return views.Select(kv => (kv.Key, kv.Value)).OrderBy(v => v.Value).ToList();
    }

    internal static List<(string UniqueName, string XmlPath)> DiscoverAppModules(string solutionExportDir)
    {
        var result = new List<(string UniqueName, string XmlPath)>();
        if (!Directory.Exists(solutionExportDir)) return result;

        foreach (var solDir in Directory.GetDirectories(solutionExportDir))
        {
            var dirName = Path.GetFileName(solDir);
            if (dirName.StartsWith('.') || dirName.StartsWith('_')) continue;

            var appModulesDir = Path.Combine(solDir, "AppModules");
            if (!Directory.Exists(appModulesDir)) continue;

            foreach (var appDir in Directory.GetDirectories(appModulesDir))
            {
                var xmlPath = Path.Combine(appDir, "AppModule.xml");
                if (!File.Exists(xmlPath)) continue;

                try
                {
                    var doc = XDocument.Load(xmlPath);
                    var uniqueName = doc.Root?.Element("UniqueName")?.Value;
                    if (uniqueName != null)
                        result.Add((uniqueName, xmlPath));
                }
                catch { /* skip malformed */ }
            }
        }

        return result;
    }

    internal static HashSet<Guid> ReadAppModuleViewIds(string appModuleXmlPath)
    {
        var ids = new HashSet<Guid>();
        try
        {
            var doc = XDocument.Load(appModuleXmlPath);
            var components = doc.Root?.Element("AppModuleComponents")?.Elements("AppModuleComponent") ?? [];
            foreach (var comp in components)
            {
                var typeAttr = comp.Attribute("type")?.Value;
                var idAttr = comp.Attribute("id")?.Value;
                if (typeAttr == "26" && idAttr != null)
                {
                    var idText = idAttr.Trim('{', '}');
                    if (Guid.TryParse(idText, out var id))
                        ids.Add(id);
                }
            }
        }
        catch { /* ignore */ }
        return ids;
    }

    internal static HashSet<Guid> ReadAppModuleFormIds(string appModuleXmlPath)
    {
        var ids = new HashSet<Guid>();
        try
        {
            var doc = XDocument.Load(appModuleXmlPath);
            var components = doc.Root?.Element("AppModuleComponents")?.Elements("AppModuleComponent") ?? [];
            foreach (var comp in components)
            {
                var typeAttr = comp.Attribute("type")?.Value;
                var idAttr = comp.Attribute("id")?.Value;
                if (typeAttr == "60" && idAttr != null)
                {
                    var idText = idAttr.Trim('{', '}');
                    if (Guid.TryParse(idText, out var id))
                        ids.Add(id);
                }
            }
        }
        catch { /* ignore */ }
        return ids;
    }

    private static string ToPascalCase(string logicalName)
    {
        var parts = logicalName.Split('_');
        if (parts.Length <= 1)
            return char.ToUpperInvariant(logicalName[0]) + logicalName[1..];

        return parts[0] + "_" + string.Concat(parts.Skip(1).Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
    }

    private static void ScanSavedQueriesInDir(string rootDir, string entityFolderName,
        Dictionary<Guid, string> views)
    {
        var savedQueriesDir = Path.Combine(rootDir, "Entities", entityFolderName, "SavedQueries");
        if (!Directory.Exists(savedQueriesDir)) return;

        foreach (var xmlFile in Directory.GetFiles(savedQueriesDir, "*.xml"))
        {
            try
            {
                var parsed = SavedQueryFileReader.Parse(xmlFile);
                views.TryAdd(parsed.SavedQueryId, parsed.Name);
            }
            catch { /* skip malformed */ }
        }
    }

    private static void MoveToCommitted(string filePath, string pendingDir, string baseDir, List<string>? archivedGitPaths = null)
    {
        var relativePath = Path.GetRelativePath(pendingDir, filePath);
        var committedPath = Path.Combine(baseDir, "SolutionExport", "_committed", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(committedPath)!);

        // Track both the source (pending removal) and destination (committed addition) for git
        var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
        archivedGitPaths?.Add(Path.GetRelativePath(solutionExportDir, filePath));
        archivedGitPaths?.Add(Path.GetRelativePath(solutionExportDir, committedPath));

        File.Move(filePath, committedPath, overwrite: true);
    }

    private static void WriteOutputsFile(string path, Dictionary<string, Dictionary<string, string>> outputs)
    {
        var json = JsonSerializer.Serialize(outputs, new JsonSerializerOptions { WriteIndented = true });
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }

    private static void CleanEmptyDirectories(string rootDir)
    {
        if (!Directory.Exists(rootDir)) return;

        foreach (var dir in Directory.GetDirectories(rootDir, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length))
        {
            if (Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir);
        }

        if (Directory.GetFileSystemEntries(rootDir).Length == 0)
            Directory.Delete(rootDir);
    }
}
