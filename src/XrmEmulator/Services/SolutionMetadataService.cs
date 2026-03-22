using System.Xml.Linq;
using XrmEmulator.Models.CrmMetadata;

namespace XrmEmulator.Services;

public class SolutionMetadataService
{
    private const int DefaultLcid = 1030;

    private readonly string? _basePath;
    private readonly Lazy<ParsedMetadata> _metadata;

    public SolutionMetadataService(string? basePath)
    {
        _basePath = basePath;
        _metadata = new Lazy<ParsedMetadata>(Parse);
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_basePath);
    public IReadOnlyList<CrmApp> Apps => _metadata.Value.Apps;

    public CrmSiteMap? GetSiteMap(string uniqueName) =>
        _metadata.Value.SiteMaps.GetValueOrDefault(uniqueName);

    public CrmEntity? GetEntity(string logicalName) =>
        _metadata.Value.Entities.GetValueOrDefault(logicalName);

    public IReadOnlyList<CrmView> GetViewsForEntity(string entityName) =>
        _metadata.Value.ViewsByEntity.TryGetValue(entityName, out var views) ? views : [];

    public IReadOnlyList<CrmView> GetViewsForApp(CrmApp app, string entityName) =>
        GetViewsForEntity(entityName)
            .Where(v => app.ViewIds.Count == 0 || app.ViewIds.Contains(v.Id))
            .ToList();

    public CrmView? GetView(Guid viewId) =>
        _metadata.Value.ViewsById.GetValueOrDefault(viewId);

    public CrmForm? GetMainForm(string entityName) =>
        _metadata.Value.FormsByEntity.TryGetValue(entityName, out var forms) ? forms.FirstOrDefault() : null;

    public IReadOnlyList<CrmForm> GetFormsForApp(CrmApp app, string entityName) =>
        (_metadata.Value.FormsByEntity.TryGetValue(entityName, out var forms) ? forms : [])
            .Where(f => app.FormIds.Count == 0 || app.FormIds.Contains(f.Id))
            .ToList();

    public CrmForm? GetQuickCreateForm(string entityName) =>
        _metadata.Value.QuickFormsByEntity.TryGetValue(entityName, out var forms)
            ? forms.OrderByDescending(f => f.FormPresentation).FirstOrDefault()
            : null;

    public string? GetWebResourcePath(string name) =>
        _metadata.Value.WebResources.GetValueOrDefault(name);

    public IReadOnlyList<CrmAppAction> GetAppActions(string entityName, int location, string? appUniqueName = null)
    {
        var key = $"{entityName}|{location}";
        if (!_metadata.Value.AppActionsByEntityLocation.TryGetValue(key, out var actions))
            return [];
        if (appUniqueName != null)
            return actions.Where(a => a.AppModuleUniqueName == null || string.Equals(a.AppModuleUniqueName, appUniqueName, StringComparison.OrdinalIgnoreCase)).ToList();
        return actions;
    }

    private ParsedMetadata Parse()
    {
        var result = new ParsedMetadata();
        if (string.IsNullOrEmpty(_basePath) || !Directory.Exists(_basePath))
            return result;

        // Find all SolutionExport directories
        var solutionDirs = FindSolutionDirectories(_basePath);

        foreach (var (solDir, metadataRoot) in solutionDirs)
        {
            ParseSolutionDirectory(solDir, metadataRoot, result);
        }

        return result;
    }

    private static List<(string SolDir, string MetadataRoot)> FindSolutionDirectories(string basePath)
    {
        var dirs = new List<(string, string)>();

        // Look for SolutionExport/*/ directories (skip _committed, _pending, _outputs.json)
        foreach (var exportDir in Directory.GetDirectories(basePath, "SolutionExport", SearchOption.AllDirectories))
        {
            // Metadata root is the parent of SolutionExport/ (e.g., kf-sales/)
            var metadataRoot = Path.GetDirectoryName(exportDir)!;

            foreach (var solDir in Directory.GetDirectories(exportDir))
            {
                var dirName = Path.GetFileName(solDir);
                if (dirName.StartsWith('_'))
                    continue;
                dirs.Add((solDir, metadataRoot));
            }
        }

        return dirs;
    }

    private static void ParseSolutionDirectory(string solDir, string metadataRoot, ParsedMetadata result)
    {
        // Parse AppModules
        var appModulesDir = Path.Combine(solDir, "AppModules");
        if (Directory.Exists(appModulesDir))
        {
            foreach (var appDir in Directory.GetDirectories(appModulesDir))
            {
                var appFile = Path.Combine(appDir, "AppModule.xml");
                if (File.Exists(appFile))
                {
                    var app = ParseAppModule(appFile, metadataRoot);
                    if (app != null)
                        result.Apps.Add(app);
                }
            }
        }

        // Parse AppModuleSiteMaps
        var siteMapsDir = Path.Combine(solDir, "AppModuleSiteMaps");
        if (Directory.Exists(siteMapsDir))
        {
            foreach (var smDir in Directory.GetDirectories(siteMapsDir))
            {
                var smFile = Path.Combine(smDir, "AppModuleSiteMap.xml");
                if (File.Exists(smFile))
                {
                    var siteMap = ParseSiteMap(smFile);
                    if (siteMap != null)
                        result.SiteMaps[siteMap.UniqueName] = siteMap;
                }
            }
        }

        // Parse AppActions (command bar buttons)
        var appActionsDir = Path.Combine(solDir, "appactions");
        if (Directory.Exists(appActionsDir))
        {
            foreach (var actionDir in Directory.GetDirectories(appActionsDir))
            {
                var actionFile = Path.Combine(actionDir, "appaction.xml");
                if (File.Exists(actionFile))
                {
                    var action = ParseAppAction(actionFile);
                    if (action != null && !action.Hidden)
                    {
                        var key = $"{action.EntityLogicalName}|{action.Location}";
                        if (!result.AppActionsByEntityLocation.TryGetValue(key, out var actionList))
                        {
                            actionList = [];
                            result.AppActionsByEntityLocation[key] = actionList;
                        }
                        actionList.Add(action);
                    }
                }
            }
        }

        // Index WebResources (JS files for form scripts)
        var webResourcesDir = Path.Combine(solDir, "WebResources");
        if (Directory.Exists(webResourcesDir))
        {
            foreach (var wrFile in Directory.GetFiles(webResourcesDir))
            {
                var wrName = Path.GetFileName(wrFile);
                if (!wrName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    !wrName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    result.WebResources.TryAdd(wrName, wrFile);
                }
            }
        }

        // Parse Entities
        var entitiesDir = Path.Combine(solDir, "Entities");
        if (Directory.Exists(entitiesDir))
        {
            foreach (var entityDir in Directory.GetDirectories(entitiesDir))
            {
                var entityFile = Path.Combine(entityDir, "Entity.xml");
                if (File.Exists(entityFile))
                {
                    var entity = ParseEntity(entityFile);
                    if (entity != null)
                    {
                        // Merge: keep existing attributes, add new ones
                        if (result.Entities.TryGetValue(entity.LogicalName, out var existing))
                        {
                            foreach (var attr in entity.Attributes)
                            {
                                existing.Attributes.TryAdd(attr.Key, attr.Value);
                            }
                        }
                        else
                        {
                            result.Entities[entity.LogicalName] = entity;
                        }
                    }
                }

                // Parse SavedQueries
                var savedQueriesDir = Path.Combine(entityDir, "SavedQueries");
                if (Directory.Exists(savedQueriesDir))
                {
                    foreach (var sqFile in Directory.GetFiles(savedQueriesDir, "*.xml"))
                    {
                        var view = ParseSavedQuery(sqFile, entityDir);
                        if (view != null && !result.ViewsById.ContainsKey(view.Id))
                        {
                            result.ViewsById[view.Id] = view;
                            if (!result.ViewsByEntity.TryGetValue(view.EntityName, out var viewList))
                            {
                                viewList = [];
                                result.ViewsByEntity[view.EntityName] = viewList;
                            }
                            viewList.Add(view);
                        }
                    }
                }

                // Parse FormXml/main
                var formsDir = Path.Combine(entityDir, "FormXml", "main");
                if (Directory.Exists(formsDir))
                {
                    foreach (var formFile in Directory.GetFiles(formsDir, "*.xml"))
                    {
                        var form = ParseForm(formFile, entityDir);
                        if (form != null)
                        {
                            if (!result.FormsByEntity.TryGetValue(form.EntityName, out var formList))
                            {
                                formList = [];
                                result.FormsByEntity[form.EntityName] = formList;
                            }
                            if (!formList.Any(f => f.Id == form.Id))
                                formList.Add(form);
                        }
                    }
                }

                // Parse FormXml/quick and FormXml/quickCreate (CRM uses both directories)
                foreach (var quickDirName in new[] { "quick", "quickCreate" })
                {
                    var quickFormsDir = Path.Combine(entityDir, "FormXml", quickDirName);
                    if (!Directory.Exists(quickFormsDir)) continue;
                    foreach (var formFile in Directory.GetFiles(quickFormsDir, "*.xml"))
                    {
                        var form = ParseForm(formFile, entityDir, "quick");
                        if (form != null)
                        {
                            if (!result.QuickFormsByEntity.TryGetValue(form.EntityName, out var formList))
                            {
                                formList = [];
                                result.QuickFormsByEntity[form.EntityName] = formList;
                            }
                            if (!formList.Any(f => f.Id == form.Id))
                                formList.Add(form);
                        }
                    }
                }
            }
        }
    }

    private static CrmApp? ParseAppModule(string filePath, string metadataRoot)
    {
        var doc = XDocument.Load(filePath);
        var root = doc.Root;
        if (root == null) return null;

        var uniqueName = root.Element("UniqueName")?.Value;
        if (string.IsNullOrEmpty(uniqueName)) return null;

        var displayName = root.Element("LocalizedNames")
            ?.Elements("LocalizedName")
            .FirstOrDefault(e => (int?)e.Attribute("languagecode") == DefaultLcid)
            ?.Attribute("description")?.Value ?? uniqueName;

        var description = root.Element("Descriptions")
            ?.Elements("Description")
            .FirstOrDefault(e => (int?)e.Attribute("languagecode") == DefaultLcid)
            ?.Attribute("description")?.Value ?? "";

        var entities = new List<string>();
        var viewIds = new List<Guid>();
        var formIds = new List<Guid>();
        string? siteMapName = null;

        foreach (var comp in root.Element("AppModuleComponents")?.Elements("AppModuleComponent") ?? [])
        {
            var type = (int?)comp.Attribute("type");
            switch (type)
            {
                case 1: // Entity
                    var schemaName = comp.Attribute("schemaName")?.Value;
                    if (!string.IsNullOrEmpty(schemaName))
                        entities.Add(schemaName.ToLowerInvariant());
                    break;
                case 26: // View (SavedQuery)
                    if (Guid.TryParse(comp.Attribute("id")?.Value, out var viewId))
                        viewIds.Add(viewId);
                    break;
                case 60: // Form
                    if (Guid.TryParse(comp.Attribute("id")?.Value, out var formId))
                        formIds.Add(formId);
                    break;
                case 62: // SiteMap
                    siteMapName = comp.Attribute("schemaName")?.Value;
                    break;
            }
        }

        return new CrmApp
        {
            UniqueName = uniqueName,
            DisplayName = displayName,
            Description = description,
            EntityNames = entities,
            ViewIds = viewIds,
            FormIds = formIds,
            SiteMapUniqueName = siteMapName,
            MetadataRootPath = metadataRoot
        };
    }

    private static CrmSiteMap? ParseSiteMap(string filePath)
    {
        var doc = XDocument.Load(filePath);
        var root = doc.Root;
        if (root == null) return null;

        var uniqueName = root.Element("SiteMapUniqueName")?.Value;
        if (string.IsNullOrEmpty(uniqueName)) return null;

        var siteMapEl = root.Element("SiteMap");
        if (siteMapEl == null) return null;

        var areas = new List<CrmArea>();
        foreach (var areaEl in siteMapEl.Elements("Area"))
        {
            var areaId = areaEl.Attribute("Id")?.Value ?? "";
            var areaTitle = GetLocalizedTitle(areaEl) ?? areaId;

            var groups = new List<CrmGroup>();
            foreach (var groupEl in areaEl.Elements("Group"))
            {
                var groupId = groupEl.Attribute("Id")?.Value ?? "";
                var groupTitle = GetLocalizedTitle(groupEl) ?? groupId;

                var subAreas = new List<CrmSubArea>();
                foreach (var subAreaEl in groupEl.Elements("SubArea"))
                {
                    var subAreaId = subAreaEl.Attribute("Id")?.Value ?? "";
                    var entity = subAreaEl.Attribute("Entity")?.Value;
                    var subAreaTitle = GetLocalizedTitle(subAreaEl);

                    subAreas.Add(new CrmSubArea
                    {
                        Id = subAreaId,
                        Entity = entity,
                        Title = subAreaTitle
                    });
                }

                groups.Add(new CrmGroup
                {
                    Id = groupId,
                    Title = groupTitle,
                    SubAreas = subAreas
                });
            }

            areas.Add(new CrmArea
            {
                Id = areaId,
                Title = areaTitle,
                Groups = groups
            });
        }

        return new CrmSiteMap
        {
            UniqueName = uniqueName,
            Areas = areas
        };
    }

    private static string? GetLocalizedTitle(XElement element)
    {
        return element.Element("Titles")
            ?.Elements("Title")
            .FirstOrDefault(t => (int?)t.Attribute("LCID") == DefaultLcid)
            ?.Attribute("Title")?.Value;
    }

    private static CrmEntity? ParseEntity(string filePath)
    {
        var doc = XDocument.Load(filePath);
        var root = doc.Root;
        if (root == null) return null;

        var nameEl = root.Element("Name");
        if (nameEl == null) return null;

        var logicalName = nameEl.Value.ToLowerInvariant();
        var displayName = nameEl.Attribute("LocalizedName")?.Value ?? logicalName;

        var entityEl = root.Element("EntityInfo")?.Element("entity");
        if (entityEl == null) return null;

        var attributes = new Dictionary<string, CrmAttribute>();
        foreach (var attrEl in entityEl.Element("attributes")?.Elements("attribute") ?? [])
        {
            var attrLogicalName = attrEl.Element("LogicalName")?.Value;
            if (string.IsNullOrEmpty(attrLogicalName)) continue;

            var attrDisplayName = attrEl.Element("displaynames")
                ?.Elements("displayname")
                .FirstOrDefault(d => (int?)d.Attribute("languagecode") == DefaultLcid)
                ?.Attribute("description")?.Value ?? attrLogicalName;

            var attrType = attrEl.Element("Type")?.Value ?? "nvarchar";

            attributes[attrLogicalName] = new CrmAttribute
            {
                LogicalName = attrLogicalName,
                DisplayName = attrDisplayName,
                Type = attrType
            };
        }

        return new CrmEntity
        {
            LogicalName = logicalName,
            DisplayName = displayName,
            Attributes = attributes
        };
    }

    private static CrmView? ParseSavedQuery(string filePath, string entityDir)
    {
        var doc = XDocument.Load(filePath);
        var savedQuery = doc.Root?.Element("savedquery");
        if (savedQuery == null) return null;

        // Only include public views (querytype=0)
        var queryType = savedQuery.Element("querytype")?.Value;
        if (queryType != "0") return null;

        var idStr = savedQuery.Element("savedqueryid")?.Value;
        if (!Guid.TryParse(idStr, out var id)) return null;

        var name = savedQuery.Element("LocalizedNames")
            ?.Elements("LocalizedName")
            .FirstOrDefault(e => (int?)e.Attribute("languagecode") == DefaultLcid)
            ?.Attribute("description")?.Value ?? "";

        var fetchXml = savedQuery.Element("fetchxml")?.ToString() ?? "";
        // Extract inner fetch element content
        var fetchEl = savedQuery.Element("fetchxml")?.Element("fetch");
        if (fetchEl != null)
            fetchXml = fetchEl.ToString();

        var entityName = Path.GetFileName(entityDir).ToLowerInvariant();

        // Parse layoutxml columns
        var columns = new List<CrmViewColumn>();
        var layoutXml = savedQuery.Element("layoutxml");
        if (layoutXml != null)
        {
            foreach (var cell in layoutXml.Descendants("cell"))
            {
                var cellName = cell.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(cellName)) continue;

                var width = 100;
                if (int.TryParse(cell.Attribute("width")?.Value, out var w))
                    width = w;

                columns.Add(new CrmViewColumn { Name = cellName, Width = width });
            }
        }

        return new CrmView
        {
            Id = id,
            Name = name,
            EntityName = entityName,
            FetchXml = fetchXml,
            Columns = columns
        };
    }

    private const string SubgridClassId = "E7A81278-8635-4D9E-8D4D-59480B391C5B";

    private static CrmAppAction? ParseAppAction(string filePath)
    {
        var doc = XDocument.Load(filePath);
        var root = doc.Root;
        if (root == null) return null;

        var uniqueName = root.Attribute("uniquename")?.Value ?? root.Element("name")?.Value;
        if (string.IsNullOrEmpty(uniqueName)) return null;

        var label = root.Element("buttonlabeltext")
            ?.Elements("label")
            .FirstOrDefault(l => (int?)l.Attribute("languagecode") == DefaultLcid)
            ?.Attribute("description")?.Value
            ?? root.Element("buttonlabeltext")?.Attribute("default")?.Value
            ?? uniqueName;

        var entityLogicalName = root.Element("contextentity")?.Element("logicalname")?.Value
            ?? root.Element("contextvalue")?.Value;
        if (string.IsNullOrEmpty(entityLogicalName)) return null;

        var location = int.TryParse(root.Element("location")?.Value, out var loc) ? loc : 0;
        var hidden = root.Element("hidden")?.Value == "1";
        var sequence = decimal.TryParse(root.Element("sequence")?.Value, out var seq) ? seq : 0m;
        var fontIcon = root.Element("fonticon")?.Value;
        var appModuleUniqueName = root.Element("appmoduleid")?.Element("uniquename")?.Value;
        var onClickEventType = int.TryParse(root.Element("onclickeventtype")?.Value, out var evt) ? evt : 0;
        var jsFunctionName = root.Element("onclickeventjavascriptfunctionname")?.Value;

        return new CrmAppAction
        {
            UniqueName = uniqueName,
            Label = label,
            FontIcon = fontIcon,
            Location = location,
            EntityLogicalName = entityLogicalName,
            AppModuleUniqueName = appModuleUniqueName,
            Sequence = sequence,
            Hidden = hidden,
            OnClickEventType = onClickEventType,
            JsFunctionName = jsFunctionName
        };
    }

    private static CrmForm? ParseForm(string filePath, string entityDir, string formType = "main")
    {
        var doc = XDocument.Load(filePath);
        var systemForm = doc.Root?.Element("systemform");
        if (systemForm == null) return null;

        var idStr = systemForm.Element("formid")?.Value;
        if (!Guid.TryParse(idStr, out var id)) return null;

        var entityName = Path.GetFileName(entityDir).ToLowerInvariant();

        var formPresentation = (int?)systemForm.Element("FormPresentation") ?? 0;

        var formEl = systemForm.Element("form");
        if (formEl == null) return null;

        var name = systemForm.Element("LocalizedNames")
            ?.Elements("LocalizedName")
            .FirstOrDefault(e => (int?)e.Attribute("languagecode") == DefaultLcid)
            ?.Attribute("description")?.Value ?? entityName;

        var tabs = new List<CrmTab>();
        foreach (var tabEl in formEl.Element("tabs")?.Elements("tab") ?? [])
        {
            var tabName = tabEl.Attribute("name")?.Value;
            var tabLabel = tabEl.Element("labels")
                ?.Elements("label")
                .FirstOrDefault(l => (int?)l.Attribute("languagecode") == DefaultLcid)
                ?.Attribute("description")?.Value ?? tabName ?? "";

            var formColumns = new List<CrmFormColumn>();
            foreach (var colEl in tabEl.Element("columns")?.Elements("column") ?? [])
            {
                var colWidth = colEl.Attribute("width")?.Value ?? "100%";

                var sections = new List<CrmSection>();
                foreach (var secEl in colEl.Element("sections")?.Elements("section") ?? [])
                {
                    var secName = secEl.Attribute("name")?.Value;
                    var showLabel = secEl.Attribute("showlabel")?.Value != "false";
                    var secLabel = secEl.Element("labels")
                        ?.Elements("label")
                        .FirstOrDefault(l => (int?)l.Attribute("languagecode") == DefaultLcid)
                        ?.Attribute("description")?.Value;

                    var fields = new List<CrmFormField>();
                    foreach (var rowEl in secEl.Element("rows")?.Elements("row") ?? [])
                    {
                        foreach (var cellEl in rowEl.Elements("cell"))
                        {
                            var isHidden = cellEl.Attribute("visible")?.Value == "false";
                            var controlEl = cellEl.Element("control");
                            if (controlEl == null) continue;

                            var classId = controlEl.Attribute("classid")?.Value;
                            var dataFieldName = controlEl.Attribute("datafieldname")?.Value;

                            var fieldLabel = cellEl.Element("labels")
                                ?.Elements("label")
                                .FirstOrDefault(l => (int?)l.Attribute("languagecode") == DefaultLcid)
                                ?.Attribute("description")?.Value;

                            // Check if this is a subgrid control
                            var isSubgrid = classId != null &&
                                classId.Trim('{', '}').Equals(SubgridClassId, StringComparison.OrdinalIgnoreCase);

                            if (isSubgrid)
                            {
                                var parameters = controlEl.Element("parameters");
                                var targetEntity = parameters?.Element("TargetEntityType")?.Value;
                                var viewIdStr = parameters?.Element("ViewId")?.Value;
                                var relationshipName = parameters?.Element("RelationshipName")?.Value;
                                Guid? viewId = Guid.TryParse(viewIdStr?.Trim('{', '}'), out var vid) ? vid : null;

                                fields.Add(new CrmFormField
                                {
                                    DataFieldName = null,
                                    Label = fieldLabel,
                                    ControlClassId = classId,
                                    IsSubgrid = true,
                                    SubgridEntityType = targetEntity,
                                    SubgridViewId = viewId,
                                    SubgridRelationshipName = relationshipName
                                });
                            }
                            else if (!string.IsNullOrEmpty(dataFieldName))
                            {
                                fields.Add(new CrmFormField
                                {
                                    DataFieldName = dataFieldName,
                                    Label = fieldLabel,
                                    ControlClassId = classId,
                                    IsHidden = isHidden
                                });
                            }
                        }
                    }

                    sections.Add(new CrmSection
                    {
                        Name = secName,
                        Label = secLabel,
                        ShowLabel = showLabel,
                        Fields = fields
                    });
                }

                formColumns.Add(new CrmFormColumn
                {
                    Width = colWidth,
                    Sections = sections
                });
            }

            tabs.Add(new CrmTab
            {
                Name = tabName,
                Label = tabLabel,
                Columns = formColumns
            });
        }

        // Parse form events (onload handlers etc.)
        var events = new List<CrmFormEvent>();
        foreach (var eventEl in formEl.Element("events")?.Elements("event") ?? [])
        {
            var eventName = eventEl.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(eventName)) continue;
            foreach (var handler in eventEl.Element("Handlers")?.Elements("Handler") ?? [])
            {
                var funcName = handler.Attribute("functionName")?.Value;
                var libName = handler.Attribute("libraryName")?.Value;
                var enabled = handler.Attribute("enabled")?.Value != "false";
                var passCtx = handler.Attribute("passExecutionContext")?.Value == "true";
                if (!string.IsNullOrEmpty(funcName) && !string.IsNullOrEmpty(libName))
                {
                    events.Add(new CrmFormEvent
                    {
                        EventName = eventName,
                        FunctionName = funcName,
                        LibraryName = libName,
                        PassExecutionContext = passCtx,
                        Enabled = enabled
                    });
                }
            }
        }

        // Parse form libraries
        var libraries = (formEl.Element("formLibraries")?.Elements("Library") ?? [])
            .Select(l => l.Attribute("name")?.Value)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .ToList();

        return new CrmForm
        {
            Id = id,
            Name = name,
            EntityName = entityName,
            FormType = formType,
            FormPresentation = formPresentation,
            Tabs = tabs,
            Events = events,
            Libraries = libraries
        };
    }

    private class ParsedMetadata
    {
        public List<CrmApp> Apps { get; } = [];
        public Dictionary<string, CrmSiteMap> SiteMaps { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CrmEntity> Entities { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<Guid, CrmView> ViewsById { get; } = [];
        public Dictionary<string, List<CrmView>> ViewsByEntity { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<CrmForm>> FormsByEntity { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<CrmForm>> QuickFormsByEntity { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<CrmAppAction>> AppActionsByEntityLocation { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> WebResources { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
