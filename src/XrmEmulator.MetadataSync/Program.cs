using System.Text.Json;
using System.Xml.Linq;
using DG.Tools.XrmMockup;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Spectre.Console;
using XrmEmulator.MetadataSync.Commit;
using XrmEmulator.MetadataSync.Connection;
using XrmEmulator.MetadataSync.Interactive;
using XrmEmulator.MetadataSync.Models;
using XrmEmulator.MetadataSync.Readers;
using XrmEmulator.MetadataSync.Serialization;
using XrmEmulator.MetadataSync.Git;
using XrmEmulator.MetadataSync.Writers;
using XrmEmulator.MetadataSync.Mcp;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;

// ──────────────────────────────────────────────────────────────
// Fast-path: hook and agent commands skip the figlet banner
// ──────────────────────────────────────────────────────────────
var positionalArgs = args.Where(a => !a.StartsWith("--")).ToArray();

if (positionalArgs.Length >= 2 && positionalArgs[0].Equals("hook", StringComparison.OrdinalIgnoreCase))
{
    if (positionalArgs[1].Equals("guard-readonly", StringComparison.OrdinalIgnoreCase))
    {
        await HandleHookGuardReadonly();
        return;
    }
    if (positionalArgs[1].Equals("guard-pending", StringComparison.OrdinalIgnoreCase))
    {
        await HandleHookGuardPending();
        return;
    }

    await Console.Error.WriteLineAsync($"Unknown hook subcommand: {positionalArgs[1]}");
    Environment.Exit(1);
}

if (positionalArgs.Length >= 2 && positionalArgs[0].Equals("agent", StringComparison.OrdinalIgnoreCase))
{
    if (positionalArgs[1].Equals("init", StringComparison.OrdinalIgnoreCase))
    {
        HandleAgentInit();
        return;
    }

    await Console.Error.WriteLineAsync($"Unknown agent subcommand: {positionalArgs[1]}");
    Environment.Exit(1);
}

if (positionalArgs.Length >= 2 && positionalArgs[0].Equals("mcp", StringComparison.OrdinalIgnoreCase))
{
    if (positionalArgs[1].Equals("init", StringComparison.OrdinalIgnoreCase))
    {
        await HandleMcpInit();
        return;
    }
    if (positionalArgs[1].Equals("serve", StringComparison.OrdinalIgnoreCase))
    {
        await HandleMcpServe();
        return;
    }

    await Console.Error.WriteLineAsync($"Unknown mcp subcommand: {positionalArgs[1]}");
    Environment.Exit(1);
}

try
{
    // 1. Parse configuration from user secrets + CLI args
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets<Program>(optional: true)
        .AddCommandLine(args)
        .Build();

    var noCache = configuration.GetValue<bool>("no-cache");
    var debug = configuration.GetValue<bool>("debug");

    if (HasFlag(args, "--help") || HasFlag(args, "-h"))
    {
        PrintHelp();
        return;
    }

    if (positionalArgs.Length > 0 && positionalArgs[0].Equals("views", StringComparison.OrdinalIgnoreCase))
    {
        await HandleViewsCommand(positionalArgs, args, configuration, noCache);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("sitemap", StringComparison.OrdinalIgnoreCase))
    {
        await HandleSiteMapCommand(positionalArgs, configuration, noCache);
    }
    else if (positionalArgs.Length >= 2 && positionalArgs[0].Equals("entity", StringComparison.OrdinalIgnoreCase)
        && positionalArgs[1].Equals("new", StringComparison.OrdinalIgnoreCase))
    {
        HandleEntityNewCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length >= 3 && positionalArgs[0].Equals("entity", StringComparison.OrdinalIgnoreCase)
        && positionalArgs[1].Equals("attribute", StringComparison.OrdinalIgnoreCase)
        && positionalArgs[2].Equals("add", StringComparison.OrdinalIgnoreCase))
    {
        HandleEntityAttributeAddCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("entity", StringComparison.OrdinalIgnoreCase))
    {
        await HandleEntityCommand(positionalArgs, configuration, noCache);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("icon", StringComparison.OrdinalIgnoreCase))
    {
        HandleIconCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("forms", StringComparison.OrdinalIgnoreCase))
    {
        await HandleFormsCommand(positionalArgs, args, configuration, noCache);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("appmodule", StringComparison.OrdinalIgnoreCase))
    {
        HandleAppModuleCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("businessrules", StringComparison.OrdinalIgnoreCase))
    {
        HandleBusinessRulesCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("webresource", StringComparison.OrdinalIgnoreCase))
    {
        HandleWebResourceCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("commandbar", StringComparison.OrdinalIgnoreCase))
    {
        HandleCommandBarCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("ribbonworkbench", StringComparison.OrdinalIgnoreCase))
    {
        HandleRibbonWorkbenchCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("deprecate", StringComparison.OrdinalIgnoreCase))
    {
        HandleDeprecateCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("plugin", StringComparison.OrdinalIgnoreCase))
    {
        HandlePluginCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("relationship", StringComparison.OrdinalIgnoreCase))
    {
        HandleRelationshipCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("import", StringComparison.OrdinalIgnoreCase))
    {
        HandleImportCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("pcf", StringComparison.OrdinalIgnoreCase))
    {
        HandlePcfCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("security-role", StringComparison.OrdinalIgnoreCase))
    {
        HandleSecurityRoleCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("optionset", StringComparison.OrdinalIgnoreCase))
    {
        HandleOptionSetCommand(positionalArgs, args);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("query", StringComparison.OrdinalIgnoreCase))
    {
        await HandleQueryCommand(positionalArgs, args, configuration, noCache);
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("pending", StringComparison.OrdinalIgnoreCase))
    {
        HandlePendingCommand();
    }
    else if (positionalArgs.Length > 0 && positionalArgs[0].Equals("commit", StringComparison.OrdinalIgnoreCase))
    {
        await HandleCommitCommand(configuration, noCache, debug);
    }
    else if (positionalArgs.Length >= 1 && positionalArgs[0].Equals("git-init", StringComparison.OrdinalIgnoreCase))
    {
        HandleGitInitCommand();
    }
    else
    {
        // Only show banner for interactive sync (no subcommand)
        AnsiConsole.Write(
            new FigletText("XRM Metadata Sync")
                .Color(Color.Blue));
        AnsiConsole.MarkupLine("[grey]Sync Dataverse metadata into XrmMockup format for XrmEmulator[/]");
        AnsiConsole.WriteLine();

        await HandleSyncCommand(configuration, noCache);
    }
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    Environment.Exit(1);
}

// ──────────────────────────────────────────────────────────────
// views <savedquery-id> — checkout a savedquery file for editing
// views new <entity> --name "<name>" — scaffold a new view
// ──────────────────────────────────────────────────────────────
static async Task HandleViewsCommand(string[] positionalArgs, string[] allArgs, IConfiguration configuration, bool noCache)
{
    if (positionalArgs.Length < 2 || HasFlag(allArgs, "--help") || HasFlag(allArgs, "-h"))
    {
        AnsiConsole.MarkupLine("[bold]MetadataSync views[/] — manage Dataverse saved queries (views)");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Commands:[/]");
        AnsiConsole.MarkupLine("  views <savedquery-guid>                              Checkout an existing view for editing");
        AnsiConsole.MarkupLine("  views new <entity> --name \"<name>\"                    Scaffold a new view");
        AnsiConsole.MarkupLine("  views delete <savedquery-guid>                       Delete a view from CRM");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Related:[/]");
        AnsiConsole.MarkupLine("  appmodule views <entity> [[--app <name>]]              Configure which views appear in an app");
        AnsiConsole.MarkupLine("  commit                                               Push pending changes to CRM");
        Environment.Exit(positionalArgs.Length < 2 ? 1 : 0);
    }

    // Route to "views new" subcommand
    if (positionalArgs[1].Equals("new", StringComparison.OrdinalIgnoreCase))
    {
        HandleViewsNewCommand(positionalArgs, allArgs);
        return;
    }

    // Route to "views delete" subcommand
    if (positionalArgs[1].Equals("delete", StringComparison.OrdinalIgnoreCase))
    {
        HandleViewsDeleteCommand(positionalArgs);
        return;
    }

    var idArg = positionalArgs[1].Trim('{', '}');
    if (!Guid.TryParse(idArg, out var savedQueryId))
    {
        AnsiConsole.MarkupLine($"[red]Unknown views subcommand:[/] {positionalArgs[1]}");
        AnsiConsole.MarkupLine("[grey]Expected a GUID (to checkout), 'new' (to scaffold), or 'delete'. Run with --help for usage.[/]");
        Environment.Exit(1);
    }

    // Find connection_metadata.json by scanning for it
    var metadataPath = FindConnectionMetadata();
    var metadata = ReadConnectionMetadata(metadataPath);
    var baseDir = GetBaseDir(metadataPath);

    // Find the savedquery XML in the snapshot
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var pattern = $"{savedQueryId.ToString().ToLowerInvariant()}.xml";

    var candidates = Directory.Exists(solutionExportDir)
        ? Directory.GetFiles(solutionExportDir, pattern, SearchOption.AllDirectories)
            .Where(f => f.Contains("SavedQueries", StringComparison.OrdinalIgnoreCase))
            .ToArray()
        : [];

    if (candidates.Length == 0)
    {
        // Also try with braces
        var bracePattern = $"{{{savedQueryId}}}.xml";
        candidates = Directory.Exists(solutionExportDir)
            ? Directory.GetFiles(solutionExportDir, bracePattern, SearchOption.AllDirectories)
                .Where(f => f.Contains("SavedQueries", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : [];
    }

    if (candidates.Length == 0)
    {
        AnsiConsole.MarkupLine($"[red]SavedQuery not found:[/] {savedQueryId}");
        AnsiConsole.MarkupLine($"[grey]Searched in: {solutionExportDir}[/]");
        Environment.Exit(1);
    }

    var sourceFile = candidates[0];

    // Determine relative path from the solution folder (keep from Entities/ onward)
    var solutionFolder = GetSolutionFolder(solutionExportDir);

    var relativePath = Path.GetRelativePath(solutionFolder, sourceFile);
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending");
    var destPath = Path.Combine(pendingDir, relativePath);

    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
    File.Copy(sourceFile, destPath, overwrite: true);

    var parsed = SavedQueryFileReader.Parse(destPath);
    AnsiConsole.MarkupLine($"[green]Checked out:[/] {parsed.Name}");
    AnsiConsole.MarkupLine($"[grey]  Source: {sourceFile}[/]");
    AnsiConsole.MarkupLine($"[grey]  Edit:   {destPath}[/]");
}

// ──────────────────────────────────────────────────────────────
// views new <entity> --name "<view name>" — scaffold a new view
// ──────────────────────────────────────────────────────────────
static void HandleViewsNewCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync views new <entity-logical-name> --name \"<view name>\"");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[2].ToLowerInvariant();

    // Parse --name from the raw args (positionalArgs has -- prefixed args stripped)
    string? viewName = null;
    for (var i = 0; i < allArgs.Length; i++)
    {
        if (allArgs[i].Equals("--name", StringComparison.OrdinalIgnoreCase) && i + 1 < allArgs.Length)
        {
            viewName = allArgs[i + 1];
            break;
        }
    }

    if (string.IsNullOrWhiteSpace(viewName))
    {
        AnsiConsole.MarkupLine("[red]--name is required.[/] Usage: MetadataSync views new <entity> --name \"<view name>\"");
        Environment.Exit(1);
    }

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    // Find entity folder name from snapshot (e.g., "Account" for "account")
    var entityFolderName = FindEntityFolderName(solutionExportDir, entityLogicalName);

    // Try to determine primary field from Entity.xml
    var (primaryIdField, primaryNameField) = GetEntityPrimaryFields(solutionExportDir, entityFolderName, entityLogicalName);

    // Look up ObjectTypeCode from Model/entities.md
    var objectTypeCode = GetObjectTypeCode(baseDir, entityLogicalName);
    if (objectTypeCode == null)
    {
        AnsiConsole.MarkupLine($"[red]ObjectTypeCode not found for entity '{entityLogicalName}'.[/]");
        AnsiConsole.MarkupLine("[grey]Run a metadata sync first to generate Model/entities.md.[/]");
        Environment.Exit(1);
    }

    // Scaffold the XML — no savedqueryid; Dataverse will assign one on create
    var xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<savedqueries xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <savedquery>
    <returnedtypecode>{entityLogicalName}</returnedtypecode>
    <querytype>0</querytype>
    <fetchxml>
      <fetch version=""1.0"" mapping=""logical"">
        <entity name=""{entityLogicalName}"">
          <attribute name=""{primaryIdField}"" />
          <attribute name=""{primaryNameField}"" />
          <attribute name=""createdon"" />
          <order attribute=""{primaryNameField}"" descending=""false"" />
          <filter type=""and"">
            <condition attribute=""statecode"" operator=""eq"" value=""0"" />
          </filter>
        </entity>
      </fetch>
    </fetchxml>
    <layoutxml>
      <grid name=""resultset"" object=""{objectTypeCode}"" jump=""{primaryNameField}"" select=""1"" icon=""1"" preview=""1"">
        <row name=""result"" id=""{primaryIdField}"">
          <cell name=""{primaryNameField}"" width=""300"" />
          <cell name=""createdon"" width=""125"" />
        </row>
      </grid>
    </layoutxml>
    <LocalizedNames>
      <LocalizedName description=""{System.Security.SecurityElement.Escape(viewName)}"" languagecode=""1030"" />
    </LocalizedNames>
  </savedquery>
</savedqueries>";

    // Write to _pending/ with a descriptive filename (no GUID — it's new)
    var safeName = viewName!.ToLowerInvariant()
        .Replace(' ', '-')
        .Replace("æ", "ae").Replace("ø", "oe").Replace("å", "aa");
    // Remove anything that's not alphanumeric, hyphen, or underscore
    safeName = System.Text.RegularExpressions.Regex.Replace(safeName, @"[^a-z0-9\-_]", "");

    var pendingDir = Path.Combine(solutionExportDir, "_pending", "Entities", entityFolderName, "SavedQueries");
    Directory.CreateDirectory(pendingDir);
    var destPath = Path.Combine(pendingDir, $"new_{safeName}.xml");
    File.WriteAllText(destPath, xml);

    AnsiConsole.MarkupLine($"[green]New view scaffolded:[/] {viewName}");
    AnsiConsole.MarkupLine($"[grey]  Entity:  {entityLogicalName}[/]");
    AnsiConsole.MarkupLine($"[grey]  File:    {destPath}[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Edit the fetchxml/layoutxml in the file above, then run [blue]commit[/] to push to CRM.[/]");
    AnsiConsole.MarkupLine("[grey]The view ID will be assigned by Dataverse on commit.[/]");
}

static string FindEntityFolderName(string solutionExportDir, string entityLogicalName)
{
    if (!Directory.Exists(solutionExportDir))
        return ToPascalCase(entityLogicalName);

    // Search all solution folders for an entity directory matching the logical name
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

    // Not found in snapshot — derive PascalCase from logical name
    return ToPascalCase(entityLogicalName);
}

static string ToPascalCase(string logicalName)
{
    // Handle prefixed names like "cr_partnerrole" → "cr_PartnerRole"
    // For simple names like "account" → "Account"
    var parts = logicalName.Split('_');
    if (parts.Length <= 1)
        return char.ToUpperInvariant(logicalName[0]) + logicalName[1..];

    // Keep prefix as-is, capitalize the rest
    return parts[0] + "_" + string.Concat(parts.Skip(1).Select(p =>
        p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
}

static (string primaryIdField, string primaryNameField) GetEntityPrimaryFields(
    string solutionExportDir, string entityFolderName, string entityLogicalName)
{
    // Default convention
    var defaultId = entityLogicalName + "id";
    var defaultName = "name";

    if (!Directory.Exists(solutionExportDir))
        return (defaultId, defaultName);

    // Try to find Entity.xml in any solution folder
    foreach (var solDir in Directory.GetDirectories(solutionExportDir))
    {
        var dirName = Path.GetFileName(solDir);
        if (dirName.StartsWith('.') || dirName.StartsWith('_')) continue;

        var entityXmlPath = Path.Combine(solDir, "Entities", entityFolderName, "Entity.xml");
        if (!File.Exists(entityXmlPath)) continue;

        try
        {
            var doc = XDocument.Load(entityXmlPath);
            var entityInfo = doc.Root?.Element("EntityInfo")?.Element("entity");
            if (entityInfo == null) continue;

            // Get primary id attribute from first attribute or convention
            var entityName = doc.Root?.Element("Name")?.Value?.ToLowerInvariant() ?? entityLogicalName;
            var idField = entityName + "id";

            // Find the primary name field: look for a "name" or displayname-like attribute
            var attrs = entityInfo.Element("attributes")?.Elements("attribute") ?? [];
            var nameAttr = attrs.FirstOrDefault(a =>
            {
                var ln = a.Element("LogicalName")?.Value;
                return ln != null && (ln.EndsWith("_name") || ln == "name" || ln == "fullname");
            });

            var nameField = nameAttr?.Element("LogicalName")?.Value ?? defaultName;
            return (idField, nameField);
        }
        catch
        {
            // Ignore parse errors, fall through to defaults
        }
    }

    return (defaultId, defaultName);
}

static string? GetObjectTypeCode(string baseDir, string entityLogicalName)
{
    var entitiesMd = Path.Combine(baseDir, "Model", "entities.md");
    if (!File.Exists(entitiesMd)) return null;

    // Parse the markdown table: | logicalname | display | ObjectTypeCode | ... |
    foreach (var line in File.ReadLines(entitiesMd))
    {
        if (!line.StartsWith('|') || line.StartsWith("|---") || line.StartsWith("| Logical")) continue;

        var cols = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (cols.Length < 3) continue;

        var logicalName = cols[0].Trim();
        if (logicalName.Equals(entityLogicalName, StringComparison.OrdinalIgnoreCase))
            return cols[2].Trim(); // ObjectTypeCode column
    }

    return null;
}

// ──────────────────────────────────────────────────────────────
// views delete <guid> — delete a view from CRM
// ──────────────────────────────────────────────────────────────
static void HandleViewsDeleteCommand(string[] positionalArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync views delete <savedquery-guid>");
        Environment.Exit(1);
    }

    var idArg = positionalArgs[2].Trim('{', '}');
    if (!Guid.TryParse(idArg, out var savedQueryId))
    {
        AnsiConsole.MarkupLine($"[red]Invalid GUID:[/] {positionalArgs[2]}");
        Environment.Exit(1);
    }

    var metadataPath = FindConnectionMetadata();

    // Try to find the view name from the local snapshot for display
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    string viewName = savedQueryId.ToString();

    var pattern = $"{savedQueryId.ToString().ToLowerInvariant()}.xml";
    var candidates = Directory.Exists(solutionExportDir)
        ? Directory.GetFiles(solutionExportDir, pattern, SearchOption.AllDirectories)
            .Where(f => f.Contains("SavedQueries", StringComparison.OrdinalIgnoreCase))
            .ToArray()
        : [];
    if (candidates.Length > 0)
    {
        try { viewName = SavedQueryFileReader.Parse(candidates[0]).Name; } catch { }
    }

    // Stage a delete marker to _pending/Deletes/
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending", "Deletes");
    Directory.CreateDirectory(pendingDir);

    var deleteDef = new DeleteDefinition
    {
        EntityType = "savedquery",
        ComponentId = savedQueryId,
        DisplayName = viewName
    };

    var fileName = $"savedquery_{savedQueryId.ToString().ToLowerInvariant()}.delete.json";
    var filePath = Path.Combine(pendingDir, fileName);
    File.WriteAllText(filePath, JsonSerializer.Serialize(deleteDef, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));

    AnsiConsole.MarkupLine($"[green]Staged delete:[/] {Markup.Escape(viewName)} ({savedQueryId})");
    AnsiConsole.MarkupLine($"[grey]File: {filePath}[/]");
    AnsiConsole.MarkupLine("[grey]Run 'commit' to execute the delete against CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// forms delete <guid> — delete a form from CRM
// ──────────────────────────────────────────────────────────────
static void HandleFormsDeleteCommand(string[] positionalArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync forms delete <form-guid>");
        Environment.Exit(1);
    }

    var idArg = positionalArgs[2].Trim('{', '}');
    if (!Guid.TryParse(idArg, out var formId))
    {
        AnsiConsole.MarkupLine($"[red]Invalid GUID:[/] {positionalArgs[2]}");
        Environment.Exit(1);
    }

    var metadataPath = FindConnectionMetadata();

    // Try to find the form name from the local snapshot for display
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    string formName = formId.ToString();

    var pattern = $"{formId.ToString().ToLowerInvariant()}.xml";
    var candidates = Directory.Exists(solutionExportDir)
        ? Directory.GetFiles(solutionExportDir, pattern, SearchOption.AllDirectories)
            .Where(f => f.Contains("FormXml", StringComparison.OrdinalIgnoreCase))
            .ToArray()
        : [];
    if (candidates.Length > 0)
    {
        try { formName = SystemFormFileReader.Parse(candidates[0]).Name; } catch { }
    }

    // Stage a delete marker to _pending/Deletes/
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending", "Deletes");
    Directory.CreateDirectory(pendingDir);

    var deleteDef = new DeleteDefinition
    {
        EntityType = "systemform",
        ComponentId = formId,
        DisplayName = formName
    };

    var fileName = $"systemform_{formId.ToString().ToLowerInvariant()}.delete.json";
    var filePath = Path.Combine(pendingDir, fileName);
    File.WriteAllText(filePath, JsonSerializer.Serialize(deleteDef, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));

    AnsiConsole.MarkupLine($"[green]Staged delete:[/] {Markup.Escape(formName)} ({formId})");
    AnsiConsole.MarkupLine($"[grey]File: {filePath}[/]");
    AnsiConsole.MarkupLine("[grey]Run 'commit' to execute the delete against CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// appmodule — top-level command router
// ──────────────────────────────────────────────────────────────
static void HandleAppModuleCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 2)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync appmodule <views|forms|entity|list> ...");
        Environment.Exit(1);
    }

    if (positionalArgs[1].Equals("views", StringComparison.OrdinalIgnoreCase))
    {
        HandleAppModuleViewsCommand(positionalArgs, allArgs);
    }
    else if (positionalArgs[1].Equals("forms", StringComparison.OrdinalIgnoreCase))
    {
        HandleAppModuleFormsCommand(positionalArgs, allArgs);
    }
    else if (positionalArgs[1].Equals("entity", StringComparison.OrdinalIgnoreCase))
    {
        HandleAppModuleEntityCommand(positionalArgs, allArgs);
    }
    else if (positionalArgs[1].Equals("list", StringComparison.OrdinalIgnoreCase))
    {
        HandleAppModuleListCommand(positionalArgs, allArgs);
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]Unknown appmodule subcommand:[/] {positionalArgs[1]}");
        AnsiConsole.MarkupLine("[grey]Available: views, forms, entity, list[/]");
        Environment.Exit(1);
    }
}

// ──────────────────────────────────────────────────────────────
// forms <guid> — checkout a form for editing
// forms new <entity> --name "..." [--copy-from <guid>] — scaffold a new form
// ──────────────────────────────────────────────────────────────
// ──────────────────────────────────────────────────────────────
// businessrules <workflow-guid> — checkout a business rule for editing
// businessrules new <entity> --name "<name>" — scaffold a new business rule
// ──────────────────────────────────────────────────────────────
static void HandleBusinessRulesCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 2 || HasFlag(allArgs, "--help") || HasFlag(allArgs, "-h"))
    {
        PrintBusinessRulesHelp();
        Environment.Exit(positionalArgs.Length < 2 ? 1 : 0);
    }

    // Route to "businessrules new" subcommand
    if (positionalArgs[1].Equals("new", StringComparison.OrdinalIgnoreCase))
    {
        HandleBusinessRulesNewCommand(positionalArgs, allArgs);
        return;
    }

    // Anything that's not "new" and not a GUID is an unknown subcommand
    var idArg = positionalArgs[1].Trim('{', '}');
    if (!Guid.TryParse(idArg, out _))
    {
        AnsiConsole.MarkupLine($"[red]Unknown businessrules subcommand:[/] {positionalArgs[1]}");
        AnsiConsole.WriteLine();
        PrintBusinessRulesHelp();
        Environment.Exit(1);
    }

    HandleBusinessRulesCheckoutCommand(positionalArgs);
}

static void PrintBusinessRulesHelp()
{
    AnsiConsole.MarkupLine("[bold]MetadataSync businessrules[/] — manage Dataverse business rules (workflow category=2)");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Commands:[/]");
    AnsiConsole.MarkupLine("  businessrules <workflow-guid>                         Checkout an existing business rule for editing");
    AnsiConsole.MarkupLine("  businessrules new <entity> --name \"<name>\"            Scaffold a new business rule");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Related:[/]");
    AnsiConsole.MarkupLine("  commit                                               Push pending changes to CRM");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Business rules are exported as two files: .xaml.data.xml (metadata) and .xaml (logic).[/]");
}

static void HandleBusinessRulesCheckoutCommand(string[] positionalArgs)
{
    var idArg = positionalArgs[1].Trim('{', '}');
    var workflowId = Guid.Parse(idArg);

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    // Find the .xaml.data.xml file in the snapshot — search all solution folders
    var guidLower = workflowId.ToString().ToUpperInvariant();

    var candidates = Directory.Exists(solutionExportDir)
        ? Directory.GetFiles(solutionExportDir, "*.xaml.data.xml", SearchOption.AllDirectories)
            .Where(f => f.Contains("Workflows", StringComparison.OrdinalIgnoreCase)
                && !f.Contains("_pending", StringComparison.OrdinalIgnoreCase)
                && !f.Contains("_committed", StringComparison.OrdinalIgnoreCase)
                && f.Contains(guidLower, StringComparison.OrdinalIgnoreCase))
            .ToArray()
        : [];

    if (candidates.Length == 0)
    {
        AnsiConsole.MarkupLine($"[red]Business rule not found:[/] {workflowId}");
        AnsiConsole.MarkupLine($"[grey]Searched for *{guidLower}*.xaml.data.xml in: {solutionExportDir}[/]");
        Environment.Exit(1);
    }

    var sourceDataXml = candidates[0];

    // Derive companion .xaml path
    var sourceXaml = sourceDataXml[..^".data.xml".Length];
    if (!File.Exists(sourceXaml))
    {
        AnsiConsole.MarkupLine($"[red]Companion XAML file not found:[/] {sourceXaml}");
        Environment.Exit(1);
    }

    // Copy both files to _pending/Workflows/
    var solutionFolder = GetSolutionFolder(solutionExportDir);
    var relativeDataXml = Path.GetRelativePath(solutionFolder, sourceDataXml);
    var relativeXaml = Path.GetRelativePath(solutionFolder, sourceXaml);
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending");

    var destDataXml = Path.Combine(pendingDir, relativeDataXml);
    var destXaml = Path.Combine(pendingDir, relativeXaml);

    Directory.CreateDirectory(Path.GetDirectoryName(destDataXml)!);
    File.Copy(sourceDataXml, destDataXml, overwrite: true);
    File.Copy(sourceXaml, destXaml, overwrite: true);

    var parsed = BusinessRuleFileReader.Parse(destDataXml);
    AnsiConsole.MarkupLine($"[green]Checked out:[/] {parsed.Name}");
    AnsiConsole.MarkupLine($"[grey]  Entity:   {parsed.PrimaryEntity}[/]");
    AnsiConsole.MarkupLine($"[grey]  Source:   {sourceDataXml}[/]");
    AnsiConsole.MarkupLine($"[grey]  Edit:     {destDataXml}[/]");
    AnsiConsole.MarkupLine($"[grey]  XAML:     {destXaml}[/]");
}

static void HandleBusinessRulesNewCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync businessrules new <entity-logical-name> --name \"<rule name>\"");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[2].ToLowerInvariant();

    string? ruleName = ParseNamedArg(allArgs, "--name");
    if (string.IsNullOrWhiteSpace(ruleName))
    {
        AnsiConsole.MarkupLine("[red]--name is required.[/] Usage: MetadataSync businessrules new <entity> --name \"<rule name>\"");
        Environment.Exit(1);
    }

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    // Safe filename
    var safeName = ruleName!.ToLowerInvariant()
        .Replace(' ', '-')
        .Replace("æ", "ae").Replace("ø", "oe").Replace("å", "aa");
    safeName = System.Text.RegularExpressions.Regex.Replace(safeName, @"[^a-z0-9\-_]", "");

    var pendingDir = Path.Combine(solutionExportDir, "_pending", "Workflows");
    Directory.CreateDirectory(pendingDir);

    // 1. Scaffold .xaml.data.xml
    var dataXml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Workflow Name=""{System.Security.SecurityElement.Escape(ruleName)}""
          xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <XamlFileName>/Workflows/new_{safeName}.xaml</XamlFileName>
  <Type>1</Type>
  <Subprocess>0</Subprocess>
  <Category>2</Category>
  <Mode>1</Mode>
  <Scope>4</Scope>
  <OnDemand>0</OnDemand>
  <TriggerOnCreate>0</TriggerOnCreate>
  <TriggerOnDelete>0</TriggerOnDelete>
  <AsyncAutodelete>0</AsyncAutodelete>
  <SyncWorkflowLogOnFailure>0</SyncWorkflowLogOnFailure>
  <StateCode>1</StateCode>
  <StatusCode>2</StatusCode>
  <RunAs>1</RunAs>
  <IsTransacted>1</IsTransacted>
  <IntroducedVersion>1.0.0.0</IntroducedVersion>
  <IsCustomizable>1</IsCustomizable>
  <BusinessProcessType>0</BusinessProcessType>
  <IsCustomProcessingStepAllowedForOtherPublishers>1</IsCustomProcessingStepAllowedForOtherPublishers>
  <PrimaryEntity>{System.Security.SecurityElement.Escape(entityLogicalName)}</PrimaryEntity>
  <LocalizedNames>
    <LocalizedName languagecode=""1030"" description=""{System.Security.SecurityElement.Escape(ruleName)}"" />
  </LocalizedNames>
  <Descriptions>
    <Description languagecode=""1030"" description="""" />
  </Descriptions>
</Workflow>";

    // 2. Scaffold .xaml template based on real working BR pattern
    var xaml = $@"<Activity x:Class=""XrmWorkflow00000000000000000000000000000000"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:mcwc=""clr-namespace:Microsoft.Crm.Workflow.ClientActivities;assembly=Microsoft.Crm.Workflow, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" xmlns:mva=""clr-namespace:Microsoft.VisualBasic.Activities;assembly=System.Activities, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" xmlns:mxs=""clr-namespace:Microsoft.Xrm.Sdk;assembly=Microsoft.Xrm.Sdk, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" xmlns:mxsq=""clr-namespace:Microsoft.Xrm.Sdk.Query;assembly=Microsoft.Xrm.Sdk, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" xmlns:mxswa=""clr-namespace:Microsoft.Xrm.Sdk.Workflow.Activities;assembly=Microsoft.Xrm.Sdk.Workflow, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" xmlns:s=""clr-namespace:System;assembly=mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"" xmlns:scg=""clr-namespace:System.Collections.Generic;assembly=mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"" xmlns:sco=""clr-namespace:System.Collections.ObjectModel;assembly=mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"" xmlns:srs=""clr-namespace:System.Runtime.Serialization;assembly=System.Runtime.Serialization, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"" xmlns:this=""clr-namespace:"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <x:Members>
    <x:Property Name=""InputEntities"" Type=""InArgument(scg:IDictionary(x:String, mxs:Entity))"" />
    <x:Property Name=""CreatedEntities"" Type=""InArgument(scg:IDictionary(x:String, mxs:Entity))"" />
  </x:Members>
  <this:XrmWorkflow00000000000000000000000000000000.InputEntities>
    <InArgument x:TypeArguments=""scg:IDictionary(x:String, mxs:Entity)"" />
  </this:XrmWorkflow00000000000000000000000000000000.InputEntities>
  <this:XrmWorkflow00000000000000000000000000000000.CreatedEntities>
    <InArgument x:TypeArguments=""scg:IDictionary(x:String, mxs:Entity)"" />
  </this:XrmWorkflow00000000000000000000000000000000.CreatedEntities>
  <mva:VisualBasic.Settings>Assembly references and imported namespaces for internal implementation</mva:VisualBasic.Settings>
  <mxswa:Workflow>
    <mxswa:ActivityReference AssemblyQualifiedName=""Microsoft.Crm.Workflow.Activities.ConditionSequence, Microsoft.Crm.Workflow, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" DisplayName=""ConditionStep1"">
      <mxswa:ActivityReference.Arguments>
        <InArgument x:TypeArguments=""x:Boolean"" x:Key=""Wait"">False</InArgument>
      </mxswa:ActivityReference.Arguments>
      <mxswa:ActivityReference.Properties>
        <sco:Collection x:TypeArguments=""Variable"" x:Key=""Variables"">
          <Variable x:TypeArguments=""x:Boolean"" Default=""False"" Name=""ConditionBranchStep2_condition"" />
          <Variable x:TypeArguments=""x:Object"" Name=""ConditionBranchStep2_1"" />
        </sco:Collection>
        <sco:Collection x:TypeArguments=""Activity"" x:Key=""Activities"">
          <!-- TODO: Replace ATTRIBUTE_TO_CHECK with the attribute to evaluate -->
          <mxswa:GetEntityProperty Attribute=""ATTRIBUTE_TO_CHECK"" Entity=""[InputEntities(&quot;primaryEntity&quot;)]"" EntityName=""{entityLogicalName}"" Value=""[ConditionBranchStep2_1]"">
            <mxswa:GetEntityProperty.TargetType>
              <InArgument x:TypeArguments=""s:Type"">
                <mxswa:ReferenceLiteral x:TypeArguments=""s:Type"">
                  <x:Null />
                </mxswa:ReferenceLiteral>
              </InArgument>
            </mxswa:GetEntityProperty.TargetType>
          </mxswa:GetEntityProperty>
          <mxswa:ActivityReference AssemblyQualifiedName=""Microsoft.Crm.Workflow.Activities.EvaluateCondition, Microsoft.Crm.Workflow, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" DisplayName=""EvaluateCondition"">
            <mxswa:ActivityReference.Arguments>
              <InArgument x:TypeArguments=""mxsq:ConditionOperator"" x:Key=""ConditionOperator"">NotNull</InArgument>
              <x:Null x:Key=""Parameters"" />
              <InArgument x:TypeArguments=""x:Object"" x:Key=""Operand"">[ConditionBranchStep2_1]</InArgument>
              <OutArgument x:TypeArguments=""x:Boolean"" x:Key=""Result"">[ConditionBranchStep2_condition]</OutArgument>
            </mxswa:ActivityReference.Arguments>
          </mxswa:ActivityReference>
          <mxswa:ActivityReference AssemblyQualifiedName=""Microsoft.Crm.Workflow.Activities.ConditionBranch, Microsoft.Crm.Workflow, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" DisplayName=""ConditionBranchStep2"">
            <mxswa:ActivityReference.Arguments>
              <InArgument x:TypeArguments=""x:Boolean"" x:Key=""Condition"">[ConditionBranchStep2_condition]</InArgument>
            </mxswa:ActivityReference.Arguments>
            <mxswa:ActivityReference.Properties>
              <mxswa:ActivityReference x:Key=""Then"" AssemblyQualifiedName=""Microsoft.Crm.Workflow.Activities.Composite, Microsoft.Crm.Workflow, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" DisplayName=""ConditionBranchStep2"">
                <mxswa:ActivityReference.Properties>
                  <sco:Collection x:TypeArguments=""Variable"" x:Key=""Variables"" />
                  <sco:Collection x:TypeArguments=""Activity"" x:Key=""Activities"">
                    <!-- TODO: Replace ATTRIBUTE_TO_SET with the target attribute, and adjust the value type/value -->
                    <Sequence DisplayName=""SetAttributeValueStep2: Set field value"">
                      <Sequence.Variables>
                        <Variable x:TypeArguments=""x:Object"" Name=""SetAttributeValueStep2_1"" />
                      </Sequence.Variables>
                      <Assign x:TypeArguments=""mxs:Entity"" To=""[CreatedEntities(&quot;primaryEntity#Temp&quot;)]"" Value=""[New Entity(&quot;{entityLogicalName}&quot;)]"" />
                      <Assign x:TypeArguments=""s:Guid"" To=""[CreatedEntities(&quot;primaryEntity#Temp&quot;).Id]"" Value=""[InputEntities(&quot;primaryEntity&quot;).Id]"" />
                      <mxswa:ActivityReference AssemblyQualifiedName=""Microsoft.Crm.Workflow.Activities.EvaluateExpression, Microsoft.Crm.Workflow, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"" DisplayName=""EvaluateExpression"">
                        <mxswa:ActivityReference.Arguments>
                          <InArgument x:TypeArguments=""x:String"" x:Key=""ExpressionOperator"">CreateCrmType</InArgument>
                          <InArgument x:TypeArguments=""s:Object[]"" x:Key=""Parameters"">[New Object() {{ Microsoft.Xrm.Sdk.Workflow.WorkflowPropertyType.Boolean, ""1"" }}]</InArgument>
                          <InArgument x:TypeArguments=""s:Type"" x:Key=""TargetType"">
                            <mxswa:ReferenceLiteral x:TypeArguments=""s:Type"" Value=""x:Boolean"" />
                          </InArgument>
                          <OutArgument x:TypeArguments=""x:Object"" x:Key=""Result"">[SetAttributeValueStep2_1]</OutArgument>
                        </mxswa:ActivityReference.Arguments>
                      </mxswa:ActivityReference>
                      <mxswa:SetEntityProperty Attribute=""ATTRIBUTE_TO_SET"" Entity=""[CreatedEntities(&quot;primaryEntity#Temp&quot;)]"" EntityName=""{entityLogicalName}"" Value=""[SetAttributeValueStep2_1]"">
                        <mxswa:SetEntityProperty.TargetType>
                          <InArgument x:TypeArguments=""s:Type"">
                            <mxswa:ReferenceLiteral x:TypeArguments=""s:Type"" Value=""x:Boolean"" />
                          </InArgument>
                        </mxswa:SetEntityProperty.TargetType>
                      </mxswa:SetEntityProperty>
                      <mcwc:SetAttributeValue DisplayName=""SetAttributeValueStep2"" Entity=""[CreatedEntities(&quot;primaryEntity#Temp&quot;)]"" EntityName=""{entityLogicalName}"" />
                      <Assign x:TypeArguments=""mxs:Entity"" To=""[InputEntities(&quot;primaryEntity&quot;)]"" Value=""[CreatedEntities(&quot;primaryEntity#Temp&quot;)]"" />
                    </Sequence>
                  </sco:Collection>
                </mxswa:ActivityReference.Properties>
              </mxswa:ActivityReference>
              <x:Null x:Key=""Else"" />
              <x:String x:Key=""Description"">Condition</x:String>
            </mxswa:ActivityReference.Properties>
          </mxswa:ActivityReference>
        </sco:Collection>
        <x:Boolean x:Key=""ContainsElseBranch"">False</x:Boolean>
      </mxswa:ActivityReference.Properties>
    </mxswa:ActivityReference>
  </mxswa:Workflow>
</Activity>";

    var destDataXml = Path.Combine(pendingDir, $"new_{safeName}.xaml.data.xml");
    var destXaml = Path.Combine(pendingDir, $"new_{safeName}.xaml");

    File.WriteAllText(destDataXml, dataXml);
    File.WriteAllText(destXaml, xaml);

    AnsiConsole.MarkupLine($"[green]New business rule scaffolded:[/] {ruleName}");
    AnsiConsole.MarkupLine($"[grey]  Entity:   {entityLogicalName}[/]");
    AnsiConsole.MarkupLine($"[grey]  Metadata: {destDataXml}[/]");
    AnsiConsole.MarkupLine($"[grey]  XAML:     {destXaml}[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Edit the XAML in the file above, then run [blue]commit[/] to push to CRM.[/]");
    AnsiConsole.MarkupLine("[grey]The workflow ID will be assigned by Dataverse on commit.[/]");
}

static async Task HandleFormsCommand(string[] positionalArgs, string[] allArgs, IConfiguration configuration, bool noCache)
{
    if (positionalArgs.Length < 2 || HasFlag(allArgs, "--help") || HasFlag(allArgs, "-h"))
    {
        PrintFormsHelp();
        Environment.Exit(positionalArgs.Length < 2 ? 1 : 0);
    }

    // Route to form type subcommands
    if (positionalArgs[1].Equals("main", StringComparison.OrdinalIgnoreCase))
    {
        HandleFormsTypeCommand(positionalArgs, allArgs, "main", 2);
        return;
    }

    if (positionalArgs[1].Equals("quickcreate", StringComparison.OrdinalIgnoreCase))
    {
        HandleFormsTypeCommand(positionalArgs, allArgs, "quickCreate", 7);
        return;
    }

    // Route to "forms delete" subcommand
    if (positionalArgs[1].Equals("delete", StringComparison.OrdinalIgnoreCase))
    {
        HandleFormsDeleteCommand(positionalArgs);
        return;
    }

    // Backward compat: bare GUID → treat as "forms main edit <guid>"
    var idArg = positionalArgs[1].Trim('{', '}');
    if (Guid.TryParse(idArg, out _))
    {
        // Rewrite as: forms main edit <guid>
        var rewritten = new[] { positionalArgs[0], "main", "edit", positionalArgs[1] };
        HandleFormsTypeEditCommand(rewritten, "main");
        return;
    }

    AnsiConsole.MarkupLine($"[red]Unknown forms subcommand:[/] {positionalArgs[1]}");
    AnsiConsole.WriteLine();
    PrintFormsHelp();
    Environment.Exit(1);
}

static void PrintFormsHelp()
{
    AnsiConsole.MarkupLine("[bold]MetadataSync forms[/] — manage Dataverse system forms");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Main forms (type 2):[/]");
    AnsiConsole.MarkupLine("  forms main new <entity> --name \"<name>\" [[--copy-from <guid>]]   Scaffold a new main form");
    AnsiConsole.MarkupLine("  forms main edit <form-guid>                                      Checkout an existing main form for editing");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Quick Create forms (type 7):[/]");
    AnsiConsole.MarkupLine("  forms quickcreate new <entity> --name \"<name>\" [[--copy-from <guid>]]  Scaffold a new Quick Create form");
    AnsiConsole.MarkupLine("  forms quickcreate edit <form-guid>                               Checkout an existing Quick Create form for editing");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Other:[/]");
    AnsiConsole.MarkupLine("  forms delete <form-guid>                                         Delete a form from CRM");
    AnsiConsole.MarkupLine("  forms <form-guid>                                                (backward compat) Same as: forms main edit <guid>");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Related:[/]");
    AnsiConsole.MarkupLine("  appmodule forms <entity> [[--app <name>]]              Configure which forms appear in an app");
    AnsiConsole.MarkupLine("  commit                                               Push pending changes to CRM");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Note: Forms must exist locally in SolutionExport/ first. Run a full sync if they are missing.[/]");
}

static void HandleFormsTypeCommand(string[] positionalArgs, string[] allArgs, string folderName, int formType)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine($"[red]Usage:[/] MetadataSync forms {positionalArgs[1]} <new|edit> ...");
        PrintFormsHelp();
        Environment.Exit(1);
    }

    if (positionalArgs[2].Equals("new", StringComparison.OrdinalIgnoreCase))
    {
        HandleFormsTypeNewCommand(positionalArgs, allArgs, folderName, formType);
        return;
    }

    if (positionalArgs[2].Equals("edit", StringComparison.OrdinalIgnoreCase))
    {
        HandleFormsTypeEditCommand(positionalArgs, folderName);
        return;
    }

    AnsiConsole.MarkupLine($"[red]Unknown subcommand:[/] {positionalArgs[2]}");
    AnsiConsole.WriteLine();
    PrintFormsHelp();
    Environment.Exit(1);
}

static void HandleFormsTypeEditCommand(string[] positionalArgs, string folderName)
{
    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine($"[red]Usage:[/] MetadataSync forms {positionalArgs[1]} edit <form-guid>");
        Environment.Exit(1);
    }

    var idArg = positionalArgs[3].Trim('{', '}');
    if (!Guid.TryParse(idArg, out _))
    {
        AnsiConsole.MarkupLine($"[red]Invalid GUID:[/] {positionalArgs[3]}");
        Environment.Exit(1);
    }
    var formId = Guid.Parse(idArg);

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    // Find the form XML in the snapshot
    var pattern = $"{formId.ToString().ToLowerInvariant()}.xml";

    var candidates = Directory.Exists(solutionExportDir)
        ? Directory.GetFiles(solutionExportDir, pattern, SearchOption.AllDirectories)
            .Where(f => f.Contains("FormXml", StringComparison.OrdinalIgnoreCase))
            .ToArray()
        : [];

    if (candidates.Length == 0)
    {
        // Also try with braces
        var bracePattern = $"{{{formId}}}.xml";
        candidates = Directory.Exists(solutionExportDir)
            ? Directory.GetFiles(solutionExportDir, bracePattern, SearchOption.AllDirectories)
                .Where(f => f.Contains("FormXml", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : [];
    }

    if (candidates.Length == 0)
    {
        AnsiConsole.MarkupLine($"[red]Form not found:[/] {formId}");
        AnsiConsole.MarkupLine($"[grey]Searched in: {solutionExportDir}[/]");
        Environment.Exit(1);
    }

    var sourceFile = candidates[0];

    // Validate the form is in the expected FormXml/{folderName}/ path
    var expectedFormXmlFolder = $"FormXml{Path.DirectorySeparatorChar}{folderName}{Path.DirectorySeparatorChar}";
    var expectedFormXmlFolderAlt = $"FormXml/{folderName}/";
    if (!sourceFile.Contains(expectedFormXmlFolder, StringComparison.OrdinalIgnoreCase)
        && !sourceFile.Contains(expectedFormXmlFolderAlt, StringComparison.OrdinalIgnoreCase))
    {
        // Extract actual folder name from path
        var formXmlIdx = sourceFile.IndexOf("FormXml", StringComparison.OrdinalIgnoreCase);
        var actualType = formXmlIdx >= 0
            ? Path.GetDirectoryName(sourceFile)![(formXmlIdx + "FormXml/".Length)..]
            : "unknown";
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] Form {formId} is in FormXml/{actualType}/, not FormXml/{folderName}/.");
    }

    var solutionFolder = GetSolutionFolder(solutionExportDir);
    var relativePath = Path.GetRelativePath(solutionFolder, sourceFile);
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending");
    var destPath = Path.Combine(pendingDir, relativePath);

    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
    File.Copy(sourceFile, destPath, overwrite: true);

    // Git commit baseline so edits show as diff
    var pendingRelPath = Path.GetRelativePath(solutionExportDir, destPath);
    if (GitHelper.IsGitRepo(solutionExportDir))
    {
        var parsed0 = SystemFormFileReader.Parse(destPath);
        GitHelper.CommitFiles(solutionExportDir, [pendingRelPath], $"Checkout: {parsed0.Name}");
    }

    var parsed = SystemFormFileReader.Parse(destPath);
    AnsiConsole.MarkupLine($"[green]Checked out:[/] {parsed.Name}");
    AnsiConsole.MarkupLine($"[grey]  Source: {sourceFile}[/]");
    AnsiConsole.MarkupLine($"[grey]  Edit:   {destPath}[/]");
}

static void HandleFormsTypeNewCommand(string[] positionalArgs, string[] allArgs, string folderName, int formType)
{
    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine($"[red]Usage:[/] MetadataSync forms {positionalArgs[1]} new <entity-logical-name> --name \"<form name>\" [[--copy-from <guid>]]");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[3].ToLowerInvariant();

    string? formName = ParseNamedArg(allArgs, "--name");
    if (string.IsNullOrWhiteSpace(formName))
    {
        AnsiConsole.MarkupLine($"[red]--name is required.[/] Usage: MetadataSync forms {positionalArgs[1]} new <entity> --name \"<form name>\" [[--copy-from <guid>]]");
        Environment.Exit(1);
    }

    var copyFromArg = ParseNamedArg(allArgs, "--copy-from");

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var entityFolderName = FindEntityFolderName(solutionExportDir, entityLogicalName);

    string xml;

    if (copyFromArg != null)
    {
        // Copy from existing form
        var sourceId = copyFromArg.Trim('{', '}');
        if (!Guid.TryParse(sourceId, out _))
        {
            AnsiConsole.MarkupLine($"[red]Invalid GUID for --copy-from:[/] {copyFromArg}");
            Environment.Exit(1);
        }

        var pattern = $"{sourceId.ToLowerInvariant()}.xml";
        var candidates = Directory.Exists(solutionExportDir)
            ? Directory.GetFiles(solutionExportDir, pattern, SearchOption.AllDirectories)
                .Where(f => f.Contains("FormXml", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : [];

        if (candidates.Length == 0)
        {
            var bracePattern = $"{{{sourceId}}}.xml";
            candidates = Directory.Exists(solutionExportDir)
                ? Directory.GetFiles(solutionExportDir, bracePattern, SearchOption.AllDirectories)
                    .Where(f => f.Contains("FormXml", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : [];
        }

        if (candidates.Length == 0)
        {
            AnsiConsole.MarkupLine($"[red]Source form not found:[/] {copyFromArg}");
            AnsiConsole.MarkupLine($"[grey]Searched in: {solutionExportDir}[/]");
            Environment.Exit(1);
        }

        var doc = XDocument.Load(candidates[0]);
        var root = doc.Root!;
        var formElement = root.Name.LocalName == "systemform"
            ? root
            : root.Element("systemform")!;

        // Remove formid (Dataverse assigns new ID on create)
        formElement.Element("formid")?.Remove();

        // Remove ancestor (new form is standalone)
        formElement.Element("ancestor")?.Remove();

        // Replace name in LocalizedNames
        var localizedNames = formElement.Element("LocalizedNames");
        if (localizedNames != null)
        {
            foreach (var ln in localizedNames.Elements("LocalizedName"))
                ln.SetAttributeValue("description", formName);
        }

        xml = doc.Declaration != null
            ? doc.Declaration + Environment.NewLine + doc.Root
            : doc.Root!.ToString();
    }
    else if (formType == 7)
    {
        // Quick Create scaffold
        var (primaryIdField, primaryNameField) = GetEntityPrimaryFields(solutionExportDir, entityFolderName, entityLogicalName);
        var escapedName = System.Security.SecurityElement.Escape(formName);
        var escapedPrimary = System.Security.SecurityElement.Escape(primaryNameField);

        xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<forms xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <systemform>
    <IntroducedVersion>1.0.0.0</IntroducedVersion>
    <FormPresentation>1</FormPresentation>
    <FormActivationState>1</FormActivationState>
    <form>
      <tabs>
        <tab id=""{{generated}}"" name=""tab_1"" showlabel=""false"">
          <labels>
            <label description=""{escapedName}"" languagecode=""1030"" />
          </labels>
          <columns>
            <column width=""100%"">
              <sections>
                <section id=""{{generated}}"" name=""tab_1_section_1"" columns=""1"" showlabel=""false"" showbar=""false"" IsUserDefined=""0"" labelwidth=""130"">
                  <labels>
                    <label description=""{escapedName}"" languagecode=""1030"" />
                  </labels>
                  <rows>
                    <row>
                      <cell id=""{{generated}}"" locklevel=""0"" colspan=""1"" rowspan=""1"">
                        <labels>
                          <label description=""{escapedPrimary}"" languagecode=""1030"" />
                        </labels>
                        <control id=""{primaryNameField}"" classid=""{{4273EDBD-AC1D-40D3-9FB2-095C621B552D}}"" datafieldname=""{primaryNameField}"" disabled=""false"" />
                      </cell>
                    </row>
                  </rows>
                </section>
              </sections>
            </column>
          </columns>
        </tab>
      </tabs>
      <DisplayConditions Order=""1"" FallbackForm=""true"">
        <Everyone />
      </DisplayConditions>
    </form>
    <IsCustomizable>1</IsCustomizable>
    <CanBeDeleted>1</CanBeDeleted>
    <LocalizedNames>
      <LocalizedName description=""{escapedName}"" languagecode=""1030"" />
    </LocalizedNames>
  </systemform>
</forms>";
    }
    else
    {
        // Main form scaffold (type 2)
        var (primaryIdField, primaryNameField) = GetEntityPrimaryFields(solutionExportDir, entityFolderName, entityLogicalName);

        xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<forms>
  <systemform>
    <objecttypecode>{entityLogicalName}</objecttypecode>
    <type>2</type>
    <formxml>
      <form>
        <tabs>
          <tab name=""general"" id=""{{generated}}"" IsUserDefined=""1"" locklevel=""0"" showlabel=""true"" expanded=""true"">
            <labels>
              <label description=""{System.Security.SecurityElement.Escape(formName)}"" languagecode=""1030"" />
            </labels>
            <columns>
              <column width=""100%"">
                <sections>
                  <section name=""general_section"" id=""{{generated}}"" IsUserDefined=""1"" showlabel=""true"" showbar=""false"" locklevel=""0"" columns=""2"" labelwidth=""115"">
                    <labels>
                      <label description=""General"" languagecode=""1030"" />
                    </labels>
                    <rows>
                      <row>
                        <cell id=""{{generated}}"">
                          <labels>
                            <label description=""{System.Security.SecurityElement.Escape(primaryNameField)}"" languagecode=""1030"" />
                          </labels>
                          <control id=""{primaryNameField}"" classid=""{{4273edbd-ac1d-40d3-9fb2-095c621b552d}}"" datafieldname=""{primaryNameField}"" />
                        </cell>
                      </row>
                    </rows>
                  </section>
                </sections>
              </column>
            </columns>
          </tab>
        </tabs>
      </form>
    </formxml>
    <LocalizedNames>
      <LocalizedName description=""{System.Security.SecurityElement.Escape(formName)}"" languagecode=""1030"" />
    </LocalizedNames>
  </systemform>
</forms>";
    }

    // Write to _pending/
    var safeName = formName!.ToLowerInvariant()
        .Replace(' ', '-')
        .Replace("æ", "ae").Replace("ø", "oe").Replace("å", "aa");
    safeName = System.Text.RegularExpressions.Regex.Replace(safeName, @"[^a-z0-9\-_]", "");

    var pendingDir = Path.Combine(solutionExportDir, "_pending", "Entities", entityFolderName, "FormXml", folderName);
    Directory.CreateDirectory(pendingDir);
    var destPath = Path.Combine(pendingDir, $"new_{safeName}.xml");
    File.WriteAllText(destPath, xml);

    AnsiConsole.MarkupLine($"[green]New form scaffolded:[/] {formName}");
    AnsiConsole.MarkupLine($"[grey]  Entity:  {entityLogicalName}[/]");
    if (copyFromArg != null)
        AnsiConsole.MarkupLine($"[grey]  Copied from: {copyFromArg}[/]");
    AnsiConsole.MarkupLine($"[grey]  File:    {destPath}[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Edit the form XML in the file above, then run [blue]commit[/] to push to CRM.[/]");
    AnsiConsole.MarkupLine("[grey]The form ID will be assigned by Dataverse on commit.[/]");
}

// ──────────────────────────────────────────────────────────────
// appmodule forms <entity> — configure which forms appear in an app module
// ──────────────────────────────────────────────────────────────
static void HandleAppModuleFormsCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync appmodule forms <entity-logical-name> [[--app <appmodule-name>]]");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[2].ToLowerInvariant();
    var appModuleName = ParseNamedArg(allArgs, "--app");

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    var entityFolderName = FindEntityFolderName(solutionExportDir, entityLogicalName);

    var (selectedAppModuleUniqueName, selectedAppModuleXmlPath) = ResolveAppModule(solutionExportDir, appModuleName);

    // Auto-add entity if not in AppModule
    var existingEntities = ReadAppModuleEntitySchemaNames(selectedAppModuleXmlPath);
    if (!existingEntities.Contains(entityLogicalName))
    {
        AnsiConsole.MarkupLine($"[yellow]Entity '{entityLogicalName}' is not in the AppModule. It will be added on commit.[/]");

        var entityPendingDir = Path.Combine(solutionExportDir, "_pending", "AppModuleEntities");
        Directory.CreateDirectory(entityPendingDir);

        var entityDef = new AppModuleEntityDefinition
        {
            AppModuleUniqueName = selectedAppModuleUniqueName,
            EntityLogicalName = entityLogicalName,
            IncludeAllViews = false
        };

        var entityJsonPath = Path.Combine(entityPendingDir, $"{selectedAppModuleUniqueName}_{entityLogicalName}.json");
        var entityJson = JsonSerializer.Serialize(entityDef, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(entityJsonPath, entityJson);
    }

    // Scan all local form XML files for the entity
    var formCandidates = ScanLocalFormsForEntity(solutionExportDir, entityFolderName, entityLogicalName);

    if (formCandidates.Count == 0)
    {
        AnsiConsole.MarkupLine($"[yellow]No forms found locally for entity '{entityLogicalName}'.[/]");
        AnsiConsole.MarkupLine("[grey]Run a full MetadataSync sync first to pull down form definitions.[/]");
        return;
    }

    // Read current AppModule.xml to find existing form references
    var currentFormIds = ReadAppModuleFormIds(selectedAppModuleXmlPath);

    // Show interactive multi-select
    var prompt = new MultiSelectionPrompt<(Guid Id, string Name)>()
        .Title($"Select forms for [blue]{selectedAppModuleUniqueName}[/] ({entityLogicalName}):")
        .PageSize(20)
        .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
        .UseConverter(v => $"{v.Name} ({v.Id})")
        .AddChoices(formCandidates);

    // Pre-select currently configured forms
    foreach (var f in formCandidates.Where(f => currentFormIds.Contains(f.Id)))
        prompt.Select(f);

    var selected = AnsiConsole.Prompt(prompt);
    var selectedIds = selected.Select(f => f.Id).ToList();

    // Stage JSON marker
    var pendingDir = Path.Combine(solutionExportDir, "_pending", "AppModuleForms");
    Directory.CreateDirectory(pendingDir);

    var definition = new AppModuleFormDefinition
    {
        AppModuleUniqueName = selectedAppModuleUniqueName,
        EntityLogicalName = entityLogicalName,
        FormIds = selectedIds
    };

    var jsonPath = Path.Combine(pendingDir, $"{selectedAppModuleUniqueName}_{entityLogicalName}.json");
    var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    File.WriteAllText(jsonPath, json);

    AnsiConsole.MarkupLine($"[green]Staged AppModule form configuration:[/]");
    AnsiConsole.MarkupLine($"  AppModule: {selectedAppModuleUniqueName}");
    AnsiConsole.MarkupLine($"  Entity:    {entityLogicalName}");
    AnsiConsole.MarkupLine($"  Forms:     {selectedIds.Count} selected");
    AnsiConsole.MarkupLine($"  Marker:    {jsonPath}");
    AnsiConsole.MarkupLine($"[grey]Run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// appmodule views <entity> — configure which views appear in an app module
// ──────────────────────────────────────────────────────────────
static void HandleAppModuleViewsCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync appmodule views <entity-logical-name> [[--app <appmodule-name>]]");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[2].ToLowerInvariant();
    var appModuleName = ParseNamedArg(allArgs, "--app");

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    // Find the entity folder name
    var entityFolderName = FindEntityFolderName(solutionExportDir, entityLogicalName);

    var (selectedAppModuleUniqueName, selectedAppModuleXmlPath) = ResolveAppModule(solutionExportDir, appModuleName);

    // Auto-add entity if not in AppModule
    var existingEntities = ReadAppModuleEntitySchemaNames(selectedAppModuleXmlPath);
    if (!existingEntities.Contains(entityLogicalName))
    {
        AnsiConsole.MarkupLine($"[yellow]Entity '{entityLogicalName}' is not in the AppModule. It will be added on commit.[/]");

        var entityPendingDir = Path.Combine(solutionExportDir, "_pending", "AppModuleEntities");
        Directory.CreateDirectory(entityPendingDir);

        var entityDef = new AppModuleEntityDefinition
        {
            AppModuleUniqueName = selectedAppModuleUniqueName,
            EntityLogicalName = entityLogicalName,
            IncludeAllViews = false
        };

        var entityJsonPath = Path.Combine(entityPendingDir, $"{selectedAppModuleUniqueName}_{entityLogicalName}.json");
        var entityJson = JsonSerializer.Serialize(entityDef, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(entityJsonPath, entityJson);
    }

    // Scan all local SavedQuery XML files for the entity
    var viewCandidates = ScanLocalViewsForEntity(solutionExportDir, entityFolderName, entityLogicalName);

    if (viewCandidates.Count == 0)
    {
        AnsiConsole.MarkupLine($"[yellow]No views found locally for entity '{entityLogicalName}'.[/]");
        AnsiConsole.MarkupLine("[grey]If a view is missing, ask the CRM team to add it to a solution and re-sync.[/]");
        return;
    }

    // Read current AppModule.xml to find existing view references
    var currentViewIds = ReadAppModuleViewIds(selectedAppModuleXmlPath);

    // Show interactive multi-select
    var prompt = new MultiSelectionPrompt<(Guid Id, string Name)>()
        .Title($"Select views for [blue]{selectedAppModuleUniqueName}[/] ({entityLogicalName}):")
        .PageSize(20)
        .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
        .UseConverter(v => $"{v.Name} ({v.Id})")
        .AddChoices(viewCandidates);

    // Pre-select currently configured views
    foreach (var v in viewCandidates.Where(v => currentViewIds.Contains(v.Id)))
        prompt.Select(v);

    var selected = AnsiConsole.Prompt(prompt);
    var selectedIds = selected.Select(v => v.Id).ToList();

    // Stage JSON marker
    var pendingDir = Path.Combine(solutionExportDir, "_pending", "AppModuleViews");
    Directory.CreateDirectory(pendingDir);

    var definition = new AppModuleViewDefinition
    {
        AppModuleUniqueName = selectedAppModuleUniqueName,
        EntityLogicalName = entityLogicalName,
        ViewIds = selectedIds
    };

    var jsonPath = Path.Combine(pendingDir, $"{selectedAppModuleUniqueName}_{entityLogicalName}.json");
    var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    File.WriteAllText(jsonPath, json);

    AnsiConsole.MarkupLine($"[green]Staged AppModule view configuration:[/]");
    AnsiConsole.MarkupLine($"  AppModule: {selectedAppModuleUniqueName}");
    AnsiConsole.MarkupLine($"  Entity:    {entityLogicalName}");
    AnsiConsole.MarkupLine($"  Views:     {selectedIds.Count} selected");
    AnsiConsole.MarkupLine($"  Marker:    {jsonPath}");
    AnsiConsole.MarkupLine($"[grey]Run [blue]commit[/] to push to CRM.[/]");
}

static List<(string UniqueName, string XmlPath)> DiscoverAppModules(string solutionExportDir)
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

static List<(Guid Id, string Name)> ScanLocalViewsForEntity(
    string solutionExportDir, string entityFolderName, string entityLogicalName)
{
    var views = new Dictionary<Guid, string>(); // Deduplicate by GUID

    if (!Directory.Exists(solutionExportDir)) return views.Select(kv => (kv.Key, kv.Value)).ToList();

    // Scan all solution folders + _pending
    foreach (var dir in Directory.GetDirectories(solutionExportDir))
    {
        var dirName = Path.GetFileName(dir);

        // Scan snapshot solution folders
        if (!dirName.StartsWith('.'))
        {
            ScanSavedQueriesInDir(dir, entityFolderName, entityLogicalName, views);
        }
    }

    // Also scan _pending
    var pendingDir = Path.Combine(solutionExportDir, "_pending");
    if (Directory.Exists(pendingDir))
        ScanSavedQueriesInDir(pendingDir, entityFolderName, entityLogicalName, views);

    return views.Select(kv => (kv.Key, kv.Value)).OrderBy(v => v.Value).ToList();
}

static void ScanSavedQueriesInDir(string rootDir, string entityFolderName, string entityLogicalName,
    Dictionary<Guid, string> views)
{
    // Look for SavedQueries under Entities/<entityFolderName>/SavedQueries/
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

static HashSet<Guid> ReadAppModuleViewIds(string appModuleXmlPath)
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

static HashSet<Guid> ReadAppModuleFormIds(string appModuleXmlPath)
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

static List<(Guid Id, string Name)> ScanLocalFormsForEntity(
    string solutionExportDir, string entityFolderName, string entityLogicalName)
{
    var forms = new Dictionary<Guid, string>(); // Deduplicate by GUID

    if (!Directory.Exists(solutionExportDir)) return forms.Select(kv => (kv.Key, kv.Value)).ToList();

    // Scan all solution folders + _pending
    foreach (var dir in Directory.GetDirectories(solutionExportDir))
    {
        var dirName = Path.GetFileName(dir);

        if (!dirName.StartsWith('.'))
        {
            ScanFormXmlInDir(dir, entityFolderName, forms);
        }
    }

    // Also scan _pending
    var pendingDir = Path.Combine(solutionExportDir, "_pending");
    if (Directory.Exists(pendingDir))
        ScanFormXmlInDir(pendingDir, entityFolderName, forms);

    return forms.Select(kv => (kv.Key, kv.Value)).OrderBy(v => v.Value).ToList();
}

static void ScanFormXmlInDir(string rootDir, string entityFolderName,
    Dictionary<Guid, string> forms)
{
    // Look for FormXml/main/ under Entities/<entityFolderName>/
    var formXmlDir = Path.Combine(rootDir, "Entities", entityFolderName, "FormXml", "main");
    if (!Directory.Exists(formXmlDir)) return;

    foreach (var xmlFile in Directory.GetFiles(formXmlDir, "*.xml"))
    {
        try
        {
            var parsed = SystemFormFileReader.Parse(xmlFile);
            if (parsed.FormId != Guid.Empty)
                forms.TryAdd(parsed.FormId, parsed.Name);
        }
        catch { /* skip malformed */ }
    }
}

static HashSet<string> ReadAppModuleEntitySchemaNames(string appModuleXmlPath)
{
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    try
    {
        var doc = XDocument.Load(appModuleXmlPath);
        var components = doc.Root?.Element("AppModuleComponents")?.Elements("AppModuleComponent") ?? [];
        foreach (var comp in components)
        {
            var typeAttr = comp.Attribute("type")?.Value;
            var schemaAttr = comp.Attribute("schemaName")?.Value;
            if (typeAttr == "1" && schemaAttr != null)
                names.Add(schemaAttr);
        }
    }
    catch { /* ignore */ }
    return names;
}

static List<(string Type, string SchemaName, string? Id)> ReadAppModuleComponents(string appModuleXmlPath)
{
    var result = new List<(string Type, string SchemaName, string? Id)>();
    try
    {
        var doc = XDocument.Load(appModuleXmlPath);
        var components = doc.Root?.Element("AppModuleComponents")?.Elements("AppModuleComponent") ?? [];
        foreach (var comp in components)
        {
            var typeAttr = comp.Attribute("type")?.Value ?? "?";
            var schemaAttr = comp.Attribute("schemaName")?.Value ?? "";
            var idAttr = comp.Attribute("id")?.Value;
            result.Add((typeAttr, schemaAttr, idAttr));
        }
    }
    catch { /* ignore */ }
    return result;
}

static (string UniqueName, string XmlPath) ResolveAppModule(
    string solutionExportDir, string? appModuleName)
{
    var appModules = DiscoverAppModules(solutionExportDir);
    if (appModules.Count == 0)
    {
        AnsiConsole.MarkupLine("[red]No AppModules found in solution export.[/]");
        Environment.Exit(1);
    }

    if (appModuleName != null)
    {
        var match = appModules.FirstOrDefault(a =>
            a.UniqueName.Equals(appModuleName, StringComparison.OrdinalIgnoreCase));
        if (match == default)
        {
            AnsiConsole.MarkupLine($"[red]AppModule '{appModuleName}' not found.[/]");
            AnsiConsole.MarkupLine("[grey]Available: " + string.Join(", ", appModules.Select(a => a.UniqueName)) + "[/]");
            Environment.Exit(1);
        }
        return match;
    }

    if (appModules.Count == 1)
        return appModules[0];

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Which AppModule?")
            .AddChoices(appModules.Select(a => a.UniqueName)));
    return appModules.First(a => a.UniqueName == choice);
}

static string? ParseNamedArg(string[] allArgs, string name)
{
    for (var i = 0; i < allArgs.Length; i++)
    {
        if (allArgs[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < allArgs.Length)
            return allArgs[i + 1];
    }
    return null;
}

static bool HasFlag(string[] allArgs, string flag)
{
    return allArgs.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
}

static void PrintHelp()
{
    AnsiConsole.Write(
        new FigletText("XRM Metadata Sync")
            .Color(Color.Blue));
    AnsiConsole.MarkupLine("[grey]Sync Dataverse metadata into XrmMockup format for XrmEmulator[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Commands:[/]");
    AnsiConsole.MarkupLine("  [bold](no command)[/]                                       Full interactive sync wizard");
    AnsiConsole.MarkupLine("  [bold]views[/] <guid>                                       Checkout a view for editing");
    AnsiConsole.MarkupLine("  [bold]views new[/] <entity> --name \"<name>\"                  Scaffold a new view");
    AnsiConsole.MarkupLine("  [bold]forms main new[/] <entity> --name \"<n>\" [[--copy-from <g>]]          Scaffold a new main form");
    AnsiConsole.MarkupLine("  [bold]forms main edit[/] <guid>                                         Checkout main form for editing");
    AnsiConsole.MarkupLine("  [bold]forms quickcreate new[/] <entity> --name \"<n>\" [[--copy-from <g>]]   Scaffold a Quick Create form");
    AnsiConsole.MarkupLine("  [bold]forms quickcreate edit[/] <guid>                                  Checkout Quick Create form for editing");
    AnsiConsole.MarkupLine("  [bold]sitemap[/] <appmodule-name>                           Checkout a sitemap for editing");
    AnsiConsole.MarkupLine("  [bold]entity[/] <logical-name>                              Checkout entity metadata for editing");
    AnsiConsole.MarkupLine("  [bold]entity new[/] <schema> --display-name \"<name>\"       Create a new custom entity");
    AnsiConsole.MarkupLine("  [bold]entity attribute add[/] <entity> <name> --type <t>     Add a new field (lookup, string, int, ...)");
    AnsiConsole.MarkupLine("  [bold]icon new[/] <webresource> <svg> [[--entity <e>]]         Stage a new icon upload");
    AnsiConsole.MarkupLine("  [bold]icon set[/] <entity> <webresource>                     Set entity icon to existing resource");
    AnsiConsole.MarkupLine("  [bold]appmodule views[/] <entity> [[--app <name>]]             Configure AppModule view selection");
    AnsiConsole.MarkupLine("  [bold]appmodule forms[/] <entity> [[--app <name>]]             Configure AppModule form selection");
    AnsiConsole.MarkupLine("  [bold]appmodule entity add[/] <entity> [[--app <n>]]           Add entity to AppModule");
    AnsiConsole.MarkupLine("  [bold]appmodule list[/] [[--app <name>]]                       List AppModule components");
    AnsiConsole.MarkupLine("  [bold]webresource new[/] <name> <file> [[--type js]]           Stage a new web resource upload");
    AnsiConsole.MarkupLine("  [bold]webresource checkout[/] <name>                         Checkout existing web resource for editing");
    AnsiConsole.MarkupLine("  [bold]commandbar[/] <app> [bold]add[/] <entity>                      Stage a new command bar button");
    AnsiConsole.MarkupLine("  [bold]commandbar[/] <app> [bold]edit[/] <name>                       Edit/customize an existing command bar button");
    AnsiConsole.MarkupLine("  [bold]ribbonworkbench hide[/] <entity> <button-id>            Hide a ribbon button via HideCustomAction");
    AnsiConsole.MarkupLine("  [bold]deprecate[/] <entity> <attribute>                       Deprecate a field (prefix display name with ZZ)");
    AnsiConsole.MarkupLine("  [bold]optionset add-value[/] <name> <label> [--value <int>]  Add a value to a global option set");
    AnsiConsole.MarkupLine("  [bold]commit[/]                                             Push pending changes to CRM");
    AnsiConsole.MarkupLine("  [bold]git-init[/]                                           Initialize git tracking in SolutionExport/");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Options:[/]");
    AnsiConsole.MarkupLine("  --help, -h       Show this help");
    AnsiConsole.MarkupLine("  --no-cache        Skip auth token cache");
    AnsiConsole.MarkupLine("  --debug           Enable debug logging (for commit)");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Workflow:[/]");
    AnsiConsole.MarkupLine("  1. Run full sync (no command) to pull metadata from CRM");
    AnsiConsole.MarkupLine("  2. Checkout components (views/forms/sitemap/entity) to _pending/");
    AnsiConsole.MarkupLine("  3. Edit files in _pending/");
    AnsiConsole.MarkupLine("  4. Run [bold]commit[/] to push changes to CRM");
}

// ──────────────────────────────────────────────────────────────
// appmodule entity add <entity> — add entity to AppModule
// ──────────────────────────────────────────────────────────────
static void HandleAppModuleEntityCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3 || !positionalArgs[2].Equals("add", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync appmodule entity add <entity-logical-name> [[--app <appmodule-name>]] [[--all-views]]");
        Environment.Exit(1);
    }

    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync appmodule entity add <entity-logical-name> [[--app <appmodule-name>]] [[--all-views]]");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[3].ToLowerInvariant();
    var appModuleName = ParseNamedArg(allArgs, "--app");
    var allViews = HasFlag(allArgs, "--all-views");

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    var (selectedAppModuleUniqueName, selectedAppModuleXmlPath) = ResolveAppModule(solutionExportDir, appModuleName);

    // Check if entity already present
    var existingEntities = ReadAppModuleEntitySchemaNames(selectedAppModuleXmlPath);
    if (existingEntities.Contains(entityLogicalName))
    {
        AnsiConsole.MarkupLine($"[yellow]Entity '{entityLogicalName}' is already in AppModule '{selectedAppModuleUniqueName}'.[/]");
        return;
    }

    // Stage entity JSON
    var pendingDir = Path.Combine(solutionExportDir, "_pending", "AppModuleEntities");
    Directory.CreateDirectory(pendingDir);

    var definition = new AppModuleEntityDefinition
    {
        AppModuleUniqueName = selectedAppModuleUniqueName,
        EntityLogicalName = entityLogicalName,
        IncludeAllViews = allViews
    };

    var jsonPath = Path.Combine(pendingDir, $"{selectedAppModuleUniqueName}_{entityLogicalName}.json");
    var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    File.WriteAllText(jsonPath, json);

    AnsiConsole.MarkupLine($"[green]Staged AppModule entity:[/]");
    AnsiConsole.MarkupLine($"  AppModule: {selectedAppModuleUniqueName}");
    AnsiConsole.MarkupLine($"  Entity:    {entityLogicalName}");

    // If --all-views, also stage views
    if (allViews)
    {
        var entityFolderName = FindEntityFolderName(solutionExportDir, entityLogicalName);
        var viewCandidates = ScanLocalViewsForEntity(solutionExportDir, entityFolderName, entityLogicalName);

        if (viewCandidates.Count > 0)
        {
            var viewsPendingDir = Path.Combine(solutionExportDir, "_pending", "AppModuleViews");
            Directory.CreateDirectory(viewsPendingDir);

            var viewDef = new AppModuleViewDefinition
            {
                AppModuleUniqueName = selectedAppModuleUniqueName,
                EntityLogicalName = entityLogicalName,
                ViewIds = viewCandidates.Select(v => v.Id).ToList()
            };

            var viewJsonPath = Path.Combine(viewsPendingDir, $"{selectedAppModuleUniqueName}_{entityLogicalName}.json");
            var viewJson = JsonSerializer.Serialize(viewDef, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            File.WriteAllText(viewJsonPath, viewJson);

            AnsiConsole.MarkupLine($"  Views:     {viewCandidates.Count} (all local views staged)");
        }
        else
        {
            AnsiConsole.MarkupLine($"  Views:     [yellow]no local views found[/]");
        }
    }

    AnsiConsole.MarkupLine($"[grey]Run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// webresource new <name> <file> — stage a web resource upload
// ──────────────────────────────────────────────────────────────
static void HandleWebResourceCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 2)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync webresource new <webresource-name> <file-path> [[--type js]]");
        Environment.Exit(1);
    }

    if (positionalArgs[1].Equals("new", StringComparison.OrdinalIgnoreCase))
    {
        HandleWebResourceNewCommand(positionalArgs, allArgs);
    }
    else if (positionalArgs[1].Equals("checkout", StringComparison.OrdinalIgnoreCase))
    {
        HandleWebResourceCheckoutCommand(positionalArgs);
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]Unknown webresource subcommand:[/] {positionalArgs[1]}");
        AnsiConsole.MarkupLine("[grey]Available: new, checkout[/]");
        Environment.Exit(1);
    }
}

static void HandleWebResourceNewCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync webresource new <webresource-name> <file-path> [[--type js]]");
        Environment.Exit(1);
    }

    var webResourceName = positionalArgs[2];
    var resourceFilePath = positionalArgs[3];

    if (!File.Exists(resourceFilePath))
    {
        AnsiConsole.MarkupLine($"[red]File not found:[/] {resourceFilePath}");
        Environment.Exit(1);
    }

    var typeArg = ParseNamedArg(allArgs, "--type") ?? "js";
    var typeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["html"] = 1, ["css"] = 4, ["js"] = 3, ["xml"] = 2, ["svg"] = 11
    };
    if (!typeMap.TryGetValue(typeArg, out var webResourceType))
    {
        AnsiConsole.MarkupLine($"[red]Unknown web resource type:[/] {typeArg}");
        AnsiConsole.MarkupLine("[grey]Available: html (1), xml (2), js (3), css (4), svg (11)[/]");
        Environment.Exit(1);
    }

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var pendingWebResDir = Path.Combine(baseDir, "SolutionExport", "_pending", "WebResources");
    Directory.CreateDirectory(pendingWebResDir);

    var safeName = webResourceName.Replace("/", "-").Replace("\\", "-");
    var extension = Path.GetExtension(resourceFilePath);

    // Copy resource file
    var destPath = Path.Combine(pendingWebResDir, $"{safeName}{extension}");
    File.Copy(resourceFilePath, destPath, overwrite: true);

    // Derive display name
    var displayName = Path.GetFileNameWithoutExtension(webResourceName.Split('/').Last());
    displayName = char.ToUpper(displayName[0]) + displayName[1..];

    var definition = new WebResourceUploadDefinition
    {
        WebResourceName = webResourceName,
        DisplayName = displayName,
        ResourceFile = $"{safeName}{extension}",
        WebResourceType = webResourceType
    };

    var jsonPath = Path.Combine(pendingWebResDir, $"{safeName}.json");
    var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(jsonPath, json);

    AnsiConsole.MarkupLine($"[green]Staged web resource upload:[/]");
    AnsiConsole.MarkupLine($"  File:    {destPath}");
    AnsiConsole.MarkupLine($"  Marker:  {jsonPath}");
    AnsiConsole.MarkupLine($"  Type:    {typeArg} ({webResourceType})");
    AnsiConsole.MarkupLine($"[grey]Run [blue]commit[/] to upload to CRM.[/]");
}

static void HandleWebResourceCheckoutCommand(string[] positionalArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync webresource checkout <webresource-name>");
        Environment.Exit(1);
    }

    var webResourceName = positionalArgs[2];
    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);

    // Find the web resource in the solution export
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var webResDirectories = Directory.GetDirectories(solutionExportDir, "WebResources", SearchOption.AllDirectories)
        .Where(d => !d.Contains("_pending") && !d.Contains("_committed"))
        .ToList();

    string? foundContentFile = null;
    string? foundDataXml = null;

    foreach (var dir in webResDirectories)
    {
        // Content file has no extension in solution export
        var contentPath = Path.Combine(dir, webResourceName);
        var dataXmlPath = Path.Combine(dir, $"{webResourceName}.data.xml");

        if (File.Exists(contentPath) && File.Exists(dataXmlPath))
        {
            foundContentFile = contentPath;
            foundDataXml = dataXmlPath;
            break;
        }
    }

    if (foundContentFile == null || foundDataXml == null)
    {
        AnsiConsole.MarkupLine($"[red]Web resource not found:[/] {webResourceName}");
        AnsiConsole.MarkupLine("[grey]Searched in SolutionExport/*/WebResources/[/]");
        Environment.Exit(1);
    }

    // Parse the .data.xml to get metadata
    var dataDoc = System.Xml.Linq.XDocument.Load(foundDataXml);
    var wrRoot = dataDoc.Root!;
    var wrName = wrRoot.Element("Name")?.Value ?? webResourceName;
    var wrDisplayName = wrRoot.Element("DisplayName")?.Value ?? webResourceName;
    var wrType = int.TryParse(wrRoot.Element("WebResourceType")?.Value, out var t) ? t : 3;

    // Determine file extension from type
    var extMap = new Dictionary<int, string> { [1] = ".html", [2] = ".xml", [3] = ".js", [4] = ".css", [11] = ".svg" };
    var ext = extMap.GetValueOrDefault(wrType, ".js");

    // Copy to _pending
    var pendingWebResDir = Path.Combine(baseDir, "SolutionExport", "_pending", "WebResources");
    Directory.CreateDirectory(pendingWebResDir);

    var safeName = wrName.Replace("/", "-").Replace("\\", "-");
    var destPath = Path.Combine(pendingWebResDir, $"{safeName}{ext}");
    File.Copy(foundContentFile, destPath, overwrite: true);

    // Create the JSON marker
    var definition = new WebResourceUploadDefinition
    {
        WebResourceName = wrName,
        DisplayName = wrDisplayName,
        ResourceFile = $"{safeName}{ext}",
        WebResourceType = wrType
    };

    var jsonPath = Path.Combine(pendingWebResDir, $"{safeName}.json");
    var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(jsonPath, json);

    AnsiConsole.MarkupLine($"[green]Checked out web resource:[/] {wrName}");
    AnsiConsole.MarkupLine($"  File:    {destPath}");
    AnsiConsole.MarkupLine($"  Marker:  {jsonPath}");
    AnsiConsole.MarkupLine($"  Type:    {ext.TrimStart('.')} ({wrType})");
    AnsiConsole.MarkupLine($"[yellow]Edit the file above, then run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// commandbar <app> add|edit — stage a command bar button
// ──────────────────────────────────────────────────────────────
static void HandleCommandBarCommand(string[] positionalArgs, string[] allArgs)
{
    // commandbar <app> add <entity>
    // commandbar <app> edit <name>
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/]");
        AnsiConsole.MarkupLine("  MetadataSync commandbar <app> add <entity>");
        AnsiConsole.MarkupLine("  MetadataSync commandbar <app> edit <name>");
        Environment.Exit(1);
    }

    var subcommand = positionalArgs[2];

    if (subcommand.Equals("add", StringComparison.OrdinalIgnoreCase))
    {
        HandleCommandBarAddCommand(positionalArgs, allArgs);
    }
    else if (subcommand.Equals("edit", StringComparison.OrdinalIgnoreCase))
    {
        HandleCommandBarEditCommand(positionalArgs, allArgs);
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]Unknown commandbar subcommand:[/] {subcommand}");
        AnsiConsole.MarkupLine("[grey]Available: add, edit[/]");
        Environment.Exit(1);
    }
}

static string GetPublisherPrefix(string solutionExportDir)
{
    var solutionFolder = GetSolutionFolder(solutionExportDir);
    var solutionXmlPath = Path.Combine(solutionFolder, "Other", "Solution.xml");
    if (!File.Exists(solutionXmlPath))
        throw new InvalidOperationException($"Solution.xml not found at {solutionXmlPath}");

    var solDoc = System.Xml.Linq.XDocument.Parse(File.ReadAllText(solutionXmlPath));
    return solDoc.Descendants("CustomizationPrefix").FirstOrDefault()?.Value
        ?? throw new InvalidOperationException("Cannot find CustomizationPrefix in Solution.xml");
}

static void HandleCommandBarAddCommand(string[] positionalArgs, string[] allArgs)
{
    // commandbar <app> add <entity>
    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync commandbar <app> add <entity>");
        Environment.Exit(1);
    }

    var appModuleName = positionalArgs[1];
    var entityLogicalName = positionalArgs[3].ToLowerInvariant();

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    var (selectedAppModuleUniqueName, _) = ResolveAppModule(solutionExportDir, appModuleName);
    var prefix = GetPublisherPrefix(solutionExportDir);

    var uniqueName = $"{prefix}__{entityLogicalName}_newbutton";

    // Check if this uniquename already exists in the export
    var (existing, _) = AppActionFileReader.FindByName(solutionExportDir, uniqueName);
    if (existing != null)
    {
        AnsiConsole.MarkupLine($"[red]An appaction with uniquename '{uniqueName}' already exists in the solution export.[/]");
        AnsiConsole.MarkupLine($"[yellow]Use [blue]commandbar {appModuleName} edit {uniqueName}[/] to modify it instead.[/]");
        Environment.Exit(1);
    }

    var xml = $"""
        <appaction uniquename="{uniqueName}">
          <appmoduleid>
            <uniquename>{selectedAppModuleUniqueName}</uniquename>
          </appmoduleid>
          <name>TODO: display name for the button</name>
          <buttonlabeltext default="TODO: button label text" />
          <context>1</context>
          <contextentity>
            <logicalname>{entityLogicalName}</logicalname>
          </contextentity>
          <contextvalue>{entityLogicalName}</contextvalue>
          <location>2</location>
          <type>0</type>
          <hidden>0</hidden>
          <onclickeventtype>2</onclickeventtype>
          <onclickeventjavascriptwebresourceid>
            <name>TODO: e.g. cr_/js/mylib.js</name>
          </onclickeventjavascriptwebresourceid>
          <onclickeventjavascriptfunctionname>TODO: e.g. MyLib.onButtonClick</onclickeventjavascriptfunctionname>
          <onclickeventjavascriptparameters>PrimaryControl</onclickeventjavascriptparameters>
          <fonticon>Add</fonticon>
          <sequence>10</sequence>
        </appaction>
        """;

    var pendingDir = Path.Combine(solutionExportDir, "_pending", "appactions");
    Directory.CreateDirectory(pendingDir);

    var destPath = Path.Combine(pendingDir, $"{uniqueName}.xml");
    File.WriteAllText(destPath, xml);

    AnsiConsole.MarkupLine($"[green]Staged new command bar button:[/]");
    AnsiConsole.MarkupLine($"  Entity:     {entityLogicalName}");
    AnsiConsole.MarkupLine($"  AppModule:  {selectedAppModuleUniqueName}");
    AnsiConsole.MarkupLine($"  UniqueName: {uniqueName}");
    AnsiConsole.MarkupLine($"  File:       {destPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Edit the XML, then run [blue]commit[/] to push to CRM.[/]");
}

static void HandleCommandBarEditCommand(string[] positionalArgs, string[] allArgs)
{
    // commandbar <app> edit <name>
    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync commandbar <app> edit <name>");
        AnsiConsole.MarkupLine("[grey]<name> is the OOTB constant (e.g. Mscrm.SubGrid.account.NewRecord) or a custom appaction uniquename.[/]");
        Environment.Exit(1);
    }

    var appModuleName = positionalArgs[1];
    var buttonName = positionalArgs[3];

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    var (selectedAppModuleUniqueName, _) = ResolveAppModule(solutionExportDir, appModuleName);
    var prefix = GetPublisherPrefix(solutionExportDir);

    // Try to find existing appaction XML in solution export
    var (existingDef, existingXmlPath) = AppActionFileReader.FindByName(solutionExportDir, buttonName);

    string xml;
    string uniqueName;
    string source;

    if (existingDef == null || existingXmlPath == null)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] Command bar button '{buttonName}' not found in solution export.");
        AnsiConsole.MarkupLine("[grey]Available appactions:[/]");

        // List available appactions to help the user find the right name
        var solutionFolder = Directory.GetDirectories(solutionExportDir)
            .FirstOrDefault(d => { var n = Path.GetFileName(d); return !n.StartsWith('.') && !n.StartsWith('_'); });
        if (solutionFolder != null)
        {
            var appActionsDir = Path.Combine(solutionFolder, "appactions");
            if (Directory.Exists(appActionsDir))
            {
                foreach (var xmlFile in Directory.GetFiles(appActionsDir, "appaction.xml", SearchOption.AllDirectories))
                {
                    try
                    {
                        var parsed = AppActionFileReader.Parse(xmlFile);
                        AnsiConsole.MarkupLine($"  [blue]{parsed.UniqueName}[/]  (name: {parsed.Name})");
                    }
                    catch { }
                }
            }
        }

        AnsiConsole.MarkupLine("[grey]Use [blue]commandbar <app> add <entity>[/] to create a new button instead.[/]");
        Environment.Exit(1);
        return; // unreachable but helps compiler
    }

    // Found in export — copy the XML as-is (agent edits it directly)
    xml = File.ReadAllText(existingXmlPath);
    uniqueName = existingDef.UniqueName;
    source = $"{existingDef.Name ?? uniqueName} (from export)";

    var pendingDir = Path.Combine(solutionExportDir, "_pending", "appactions");
    Directory.CreateDirectory(pendingDir);

    var destPath = Path.Combine(pendingDir, $"{uniqueName}.xml");
    File.WriteAllText(destPath, xml);

    AnsiConsole.MarkupLine($"[green]Staged command bar edit:[/]");
    AnsiConsole.MarkupLine($"  Source:     {source}");
    AnsiConsole.MarkupLine($"  UniqueName: {uniqueName}");
    AnsiConsole.MarkupLine($"  File:       {destPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Edit the XML, then run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// ribbonworkbench hide <entity> <button-id> — stage a ribbon hide
// ──────────────────────────────────────────────────────────────
static void HandleRibbonWorkbenchCommand(string[] positionalArgs, string[] allArgs)
{
    // ribbonworkbench hide <entity> <button-id>
    if (positionalArgs.Length < 2)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/]");
        AnsiConsole.MarkupLine("  MetadataSync [bold]ribbonworkbench hide[/] <entity> <button-id>");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[grey]Stages a HideCustomAction for the given ribbon button.[/]");
        AnsiConsole.MarkupLine("[grey]Use the Ribbon/ export folder to discover button IDs.[/]");
        Environment.Exit(1);
    }

    var subCommand = positionalArgs[1].ToLowerInvariant();
    if (subCommand != "hide")
    {
        AnsiConsole.MarkupLine($"[red]Unknown ribbonworkbench subcommand:[/] {subCommand}");
        AnsiConsole.MarkupLine("[grey]Available: hide[/]");
        Environment.Exit(1);
    }

    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync ribbonworkbench hide <entity> <button-id>");
        AnsiConsole.MarkupLine("[grey]Example: ribbonworkbench hide account Mscrm.SubGrid.account.AddNewStandard[/]");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[2].ToLowerInvariant();
    var buttonId = positionalArgs[3];

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    // Validate: check Ribbon/ export for the button ID if available
    var ribbonFile = Path.Combine(baseDir, "Ribbon", $"{entityLogicalName}.xml");
    if (File.Exists(ribbonFile))
    {
        var ribbonXml = File.ReadAllText(ribbonFile);
        if (!ribbonXml.Contains($"Id=\"{buttonId}\"", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Button ID [blue]{buttonId}[/] not found in Ribbon/{entityLogicalName}.xml");
            if (!AnsiConsole.Confirm("Stage anyway?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                return;
            }
        }
    }

    // Check if already hidden in RibbonDiff.xml
    try
    {
        var solutionFolder = GetSolutionFolder(solutionExportDir);
        var entityFolderName = FindEntityFolderName(solutionExportDir, entityLogicalName);
        var ribbonDiffPath = Path.Combine(solutionFolder, "Entities", entityFolderName, "RibbonDiff.xml");
        if (File.Exists(ribbonDiffPath))
        {
            var diffXml = File.ReadAllText(ribbonDiffPath);
            if (diffXml.Contains($"Location=\"{buttonId}\"", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[yellow]{buttonId}[/] is already hidden in RibbonDiff.xml. Skipping.");
                return;
            }
        }
    }
    catch { /* Solution folder not found — skip check */ }

    // Stage the action
    var action = new RibbonWorkbenchAction
    {
        Action = "hide",
        EntityLogicalName = entityLogicalName,
        ButtonId = buttonId
    };

    var pendingDir = Path.Combine(solutionExportDir, "_pending", "RibbonWorkbench");
    Directory.CreateDirectory(pendingDir);

    var safeButtonId = buttonId.Replace(".", "_");
    var destPath = Path.Combine(pendingDir, $"{entityLogicalName}_hide_{safeButtonId}.json");
    File.WriteAllText(destPath, JsonSerializer.Serialize(action, new JsonSerializerOptions { WriteIndented = true }));

    AnsiConsole.MarkupLine($"[green]Staged ribbon hide:[/]");
    AnsiConsole.MarkupLine($"  Entity:   {entityLogicalName}");
    AnsiConsole.MarkupLine($"  Button:   {buttonId}");
    AnsiConsole.MarkupLine($"  File:     {destPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// entity new <schema-name> --display-name "<name>" [--plural "<name>"]
// ──────────────────────────────────────────────────────────────
static void HandleEntityNewCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3 || HasFlag(allArgs, "--help") || HasFlag(allArgs, "-h"))
    {
        AnsiConsole.MarkupLine("[bold]MetadataSync entity new[/] — scaffold a new custom entity");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  entity new <schema-name> --display-name \"<name>\" [--plural \"<name>\"] [--description \"<desc>\"]");
        AnsiConsole.MarkupLine("    [grey]Schema name should include publisher prefix (e.g. kf_PartnerFormResponse)[/]");
        AnsiConsole.MarkupLine("    [grey]Example: entity new kf_PartnerFormResponse --display-name \"Partner Form Svar\"[/]");
        Environment.Exit(positionalArgs.Length < 3 ? 1 : 0);
        return;
    }

    var schemaName = positionalArgs[2];
    var displayName = ParseNamedArg(allArgs, "--display-name") ?? schemaName;
    var plural = ParseNamedArg(allArgs, "--plural") ?? displayName;
    var description = ParseNamedArg(allArgs, "--description");

    // Derive logical name from schema name
    var logicalName = schemaName.ToLowerInvariant();

    var metadataPath = FindConnectionMetadata();
    var metadata = ReadConnectionMetadata(metadataPath);
    var baseDir = GetBaseDir(metadataPath);
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending", "Entities");
    Directory.CreateDirectory(pendingDir);

    var destPath = Path.Combine(pendingDir, $"{logicalName}.entity.json");

    var definition = new NewEntityDefinition
    {
        EntityLogicalName = logicalName,
        EntitySchemaName = schemaName,
        DisplayName = displayName,
        DisplayNamePlural = plural,
        PrimaryAttributeSchemaName = $"{schemaName.Split('_')[0]}_Name",
        PrimaryAttributeDisplayName = "Navn",
        SolutionUniqueName = metadata.Solution.UniqueName,
        Attributes = [],
    };

    File.WriteAllText(destPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    }));

    AnsiConsole.MarkupLine($"[green]Entity definition staged:[/]");
    AnsiConsole.MarkupLine($"  Schema:       {schemaName}");
    AnsiConsole.MarkupLine($"  Logical:      {logicalName}");
    AnsiConsole.MarkupLine($"  Display:      {displayName}");
    AnsiConsole.MarkupLine($"  Plural:       {plural}");
    AnsiConsole.MarkupLine($"  Primary Attr: {definition.PrimaryAttributeSchemaName} (\"{definition.PrimaryAttributeDisplayName}\")");
    AnsiConsole.MarkupLine($"  Solution:     {definition.SolutionUniqueName}");
    AnsiConsole.MarkupLine($"  File:         {destPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Edit the JSON file to add attributes, then run [blue]commit[/].[/]");
}

// ──────────────────────────────────────────────────────────────
// entity attribute add <entity> <name> --type <type> [--target <entity>] [--display-name <name>] [--relationship <schema>]
// ──────────────────────────────────────────────────────────────
static void HandleEntityAttributeAddCommand(string[] positionalArgs, string[] allArgs)
{
    // entity attribute add <entity> <attribute-name> --type <type>
    if (positionalArgs.Length < 5)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync entity attribute add <entity> <attribute-name> --type <type> [[--target <entity>]] [[--display-name <name>]]");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[yellow]Types:[/] lookup, string, memo, int, decimal, boolean, datetime, image");
        AnsiConsole.MarkupLine("[grey]For lookups, --target is required.[/]");
        AnsiConsole.MarkupLine("[grey]Example: entity attribute add lead cr_partner --type lookup --target account --display-name \"Partner\"[/]");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[3].ToLowerInvariant();
    var attributeName = positionalArgs[4].ToLowerInvariant();
    var attributeType = ParseNamedArg(allArgs, "--type");
    var targetEntity = ParseNamedArg(allArgs, "--target");
    var displayName = ParseNamedArg(allArgs, "--display-name");
    var relationshipSchemaName = ParseNamedArg(allArgs, "--relationship");
    var maxLengthStr = ParseNamedArg(allArgs, "--max-length");
    var requiredLevel = ParseNamedArg(allArgs, "--required") ?? "none";

    if (string.IsNullOrEmpty(attributeType))
    {
        AnsiConsole.MarkupLine("[red]--type is required.[/] Options: lookup, string, memo, int, decimal, boolean, datetime, image");
        Environment.Exit(1);
    }

    if (attributeType.Equals("lookup", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(targetEntity))
    {
        AnsiConsole.MarkupLine("[red]--target is required for lookup attributes.[/]");
        Environment.Exit(1);
    }

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var prefix = GetPublisherPrefix(solutionExportDir);

    // Validate attribute name has the publisher prefix
    if (!attributeName.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine($"[red]Attribute name must start with publisher prefix '{prefix}_'.[/]");
        AnsiConsole.MarkupLine($"[grey]Example: {prefix}_{attributeName}[/]");
        Environment.Exit(1);
    }

    // Default display name: strip prefix and capitalize
    if (string.IsNullOrEmpty(displayName))
    {
        var nameWithoutPrefix = attributeName[(prefix.Length + 1)..];
        displayName = char.ToUpper(nameWithoutPrefix[0]) + nameWithoutPrefix[1..];
    }

    // Derive SchemaName: prefix + "_" + PascalCase display name (convention: cr_Partner, cr_Department)
    var schemaName = $"{prefix}_{displayName.Replace(" ", "")}";

    // Read solution unique name
    var solutionFolder = GetSolutionFolder(solutionExportDir);
    var solutionXmlPath = Path.Combine(solutionFolder, "Other", "Solution.xml");
    var solDoc = System.Xml.Linq.XDocument.Parse(File.ReadAllText(solutionXmlPath));
    var solutionUniqueName = solDoc.Descendants("UniqueName").FirstOrDefault()?.Value
        ?? throw new InvalidOperationException("Cannot find solution UniqueName in Solution.xml");

    // Build relationship schema name for lookups
    if (attributeType.Equals("lookup", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(relationshipSchemaName))
    {
        // Convention: <prefix>_<entity>_<DisplayName>_<targetEntity>
        var cleanDisplayName = displayName.Replace(" ", "");
        relationshipSchemaName = $"{prefix}_{entityLogicalName}_{cleanDisplayName}_{targetEntity}";
    }

    var definition = new NewAttributeDefinition
    {
        EntityLogicalName = entityLogicalName,
        AttributeLogicalName = attributeName,
        AttributeSchemaName = schemaName,
        DisplayName = displayName,
        AttributeType = attributeType.ToLowerInvariant(),
        TargetEntityLogicalName = targetEntity,
        RelationshipSchemaName = relationshipSchemaName,
        MaxLength = maxLengthStr != null ? int.Parse(maxLengthStr) : null,
        RequiredLevel = requiredLevel,
        SolutionUniqueName = solutionUniqueName
    };

    var pendingDir = Path.Combine(solutionExportDir, "_pending", "Attributes");
    Directory.CreateDirectory(pendingDir);

    var destPath = Path.Combine(pendingDir, $"{entityLogicalName}_{attributeName}.attribute.json");
    File.WriteAllText(destPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));

    AnsiConsole.MarkupLine($"[green]Staged new attribute:[/]");
    AnsiConsole.MarkupLine($"  Entity:      {entityLogicalName}");
    AnsiConsole.MarkupLine($"  Attribute:   {attributeName}");
    AnsiConsole.MarkupLine($"  SchemaName:  {schemaName}");
    AnsiConsole.MarkupLine($"  Type:        {attributeType}");
    AnsiConsole.MarkupLine($"  DisplayName: {displayName}");
    if (!string.IsNullOrEmpty(targetEntity))
        AnsiConsole.MarkupLine($"  Target:      {targetEntity}");
    if (!string.IsNullOrEmpty(relationshipSchemaName))
        AnsiConsole.MarkupLine($"  Relationship:{relationshipSchemaName}");
    AnsiConsole.MarkupLine($"  Solution:    {solutionUniqueName}");
    AnsiConsole.MarkupLine($"  File:        {destPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// query <table> [--select col1,col2] [--filter field=value] [--top N]
// ──────────────────────────────────────────────────────────────
static async Task HandleQueryCommand(string[] positionalArgs, string[] allArgs, IConfiguration configuration, bool noCache)
{
    if (positionalArgs.Length < 2)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync query <table> [--select col1,col2] [--filter field=value] [--top N] [--fetchxml \"<fetch>...\"]");
        AnsiConsole.MarkupLine("[grey]Examples:[/]");
        AnsiConsole.MarkupLine("[grey]  query kf_partnerrelation --select kf_name,kf_account,kf_contact --top 10[/]");
        AnsiConsole.MarkupLine("[grey]  query contact --filter firstname=Poul --select firstname,lastname,emailaddress1[/]");
        AnsiConsole.MarkupLine("[grey]  query kf_partnerrelation --fetchxml \"<fetch top='10'><entity name='kf_partnerrelation'><all-attributes/></entity></fetch>\"[/]");
        Environment.Exit(1);
    }

    var table = positionalArgs[1].ToLowerInvariant();
    var selectArg = ParseNamedArg(allArgs, "--select");
    var filterArgs = ParseAllNamedArgs(allArgs, "--filter");
    var topArg = ParseNamedArg(allArgs, "--top");
    var fetchXml = ParseNamedArg(allArgs, "--fetchxml");
    var impersonateArg = ParseNamedArg(allArgs, "--impersonate");

    var metadataPath = FindConnectionMetadata();
    var metadata = ReadConnectionMetadata(metadataPath);

    AnsiConsole.MarkupLine("[grey]Connecting to Dataverse...[/]");
    var connectionSettings = await ReconnectFromMetadata(metadata, configuration, noCache);
    using var client = await ConnectionFactory.CreateAsync(connectionSettings);
    AnsiConsole.MarkupLine("[green]Connected.[/]");

    // Impersonation: resolve user and set CallerId
    if (!string.IsNullOrEmpty(impersonateArg))
    {
        Guid callerId;
        if (Guid.TryParse(impersonateArg, out callerId))
        {
            // Direct GUID
        }
        else
        {
            // Resolve by name (applicationuser fullname or systemuser fullname)
            var userQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression("systemuser")
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet("fullname"),
                TopCount = 1,
                Criteria = new Microsoft.Xrm.Sdk.Query.FilterExpression
                {
                    Conditions =
                    {
                        new Microsoft.Xrm.Sdk.Query.ConditionExpression("fullname", Microsoft.Xrm.Sdk.Query.ConditionOperator.Like, $"%{impersonateArg}%")
                    }
                }
            };
            var userResults = client.RetrieveMultiple(userQuery);
            if (userResults.Entities.Count == 0)
            {
                AnsiConsole.MarkupLine($"[red]User not found:[/] {impersonateArg}");
                Environment.Exit(1);
            }
            callerId = userResults.Entities[0].Id;
            var resolvedName = userResults.Entities[0].GetAttributeValue<string>("fullname");
            AnsiConsole.MarkupLine($"[grey]Resolved user: {resolvedName} ({callerId})[/]");
        }
        client.CallerId = callerId;
        AnsiConsole.MarkupLine($"[yellow]Impersonating:[/] {callerId}");
    }

    Microsoft.Xrm.Sdk.EntityCollection results;

    if (!string.IsNullOrEmpty(fetchXml))
    {
        // FetchXML mode
        var fetchReq = new Microsoft.Xrm.Sdk.Query.FetchExpression(fetchXml);
        results = client.RetrieveMultiple(fetchReq);
    }
    else
    {
        // QueryExpression mode
        var columns = string.IsNullOrEmpty(selectArg)
            ? new Microsoft.Xrm.Sdk.Query.ColumnSet(true)
            : new Microsoft.Xrm.Sdk.Query.ColumnSet(selectArg.Split(',').Select(c => c.Trim()).ToArray());

        var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(table)
        {
            ColumnSet = columns,
            TopCount = topArg != null ? int.Parse(topArg) : 50
        };

        if (filterArgs.Count > 0)
        {
            foreach (var filter in filterArgs)
            {
                var parts = filter.Split('=', 2);
                if (parts.Length == 2)
                {
                    query.Criteria.Conditions.Add(
                        new Microsoft.Xrm.Sdk.Query.ConditionExpression(parts[0].Trim(), Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, parts[1].Trim()));
                }
            }
        }

        results = client.RetrieveMultiple(query);
    }

    AnsiConsole.MarkupLine($"[green]Returned {results.Entities.Count} record(s)[/]");
    AnsiConsole.WriteLine();

    if (results.Entities.Count == 0)
        return;

    // Collect all column names across all results
    var allColumns = results.Entities
        .SelectMany(e => e.Attributes.Keys)
        .Distinct()
        .OrderBy(c => c)
        .ToList();

    // Output as JSON for easy parsing
    var output = new List<Dictionary<string, object?>>();
    foreach (var entity in results.Entities)
    {
        var row = new Dictionary<string, object?> { ["id"] = entity.Id.ToString() };
        foreach (var col in allColumns)
        {
            if (!entity.Contains(col)) continue;
            var val = entity[col];
            row[col] = val switch
            {
                EntityReference er => $"{er.LogicalName}:{er.Id} ({er.Name})",
                OptionSetValue osv => osv.Value,
                Money m => m.Value,
                AliasedValue av => av.Value?.ToString(),
                OptionSetValueCollection osc => string.Join(",", osc.Select(o => o.Value)),
                _ => val?.ToString()
            };
        }
        output.Add(row);
    }

    var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
    AnsiConsole.WriteLine(json);
}

/// <summary>Parse all occurrences of a named argument (e.g. multiple --filter).</summary>
static List<string> ParseAllNamedArgs(string[] args, string name)
{
    var values = new List<string>();
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            values.Add(args[i + 1]);
    }
    return values;
}

// ──────────────────────────────────────────────────────────────
// security-role update|add — manage security role privileges
// ──────────────────────────────────────────────────────────────
static void HandleSecurityRoleCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 2)
    {
        PrintSecurityRoleUsage();
        Environment.Exit(1);
    }

    var subCommand = positionalArgs[1].ToLowerInvariant();

    switch (subCommand)
    {
        case "add":
            HandleSecurityRoleAddCommand(positionalArgs);
            break;
        case "update":
            HandleSecurityRoleUpdateCommand(positionalArgs);
            break;
        default:
            PrintSecurityRoleUsage();
            Environment.Exit(1);
            break;
    }
}

static void PrintSecurityRoleUsage()
{
    AnsiConsole.MarkupLine("[bold]MetadataSync security-role[/] — manage security role privileges");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("  [yellow]add[/] <role-name> <entity> <access> [depth]");
    AnsiConsole.MarkupLine("    Add a privilege to a security role. Merges with existing pending file if present.");
    AnsiConsole.MarkupLine("    [grey]Access: Create, Read, Write, Delete, Append, AppendTo, Assign, Share[/]");
    AnsiConsole.MarkupLine("    [grey]Depth:  Basic, Local, Deep, Global (default: Global)[/]");
    AnsiConsole.MarkupLine("    [grey]Example: security-role add \"Partner Service\" lead Create Global[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("  [yellow]update[/] <role-name>");
    AnsiConsole.MarkupLine("    Checkout security role for editing. Creates a pending file to edit manually.");
    AnsiConsole.MarkupLine("    [grey]Example: security-role update \"Partner Service\"[/]");
}

static void HandleSecurityRoleAddCommand(string[] positionalArgs)
{
    if (positionalArgs.Length < 5)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] security-role add <role-name> <entity> <access> [depth]");
        AnsiConsole.MarkupLine("[grey]Example: security-role add \"Partner Service\" lead Create Global[/]");
        Environment.Exit(1);
    }

    var roleName = positionalArgs[2];
    var entity = positionalArgs[3].ToLowerInvariant();
    var access = positionalArgs[4];
    var depth = positionalArgs.Length > 5 ? positionalArgs[5] : "Global";

    // Validate access type
    var validAccess = new[] { "create", "read", "write", "delete", "append", "appendto", "assign", "share" };
    if (!validAccess.Contains(access.ToLowerInvariant()))
    {
        AnsiConsole.MarkupLine($"[red]Invalid access type:[/] '{access}'. Valid: {string.Join(", ", validAccess.Select(a => char.ToUpper(a[0]) + a[1..]))}");
        Environment.Exit(1);
    }

    // Validate depth
    var validDepth = new[] { "basic", "local", "deep", "global" };
    if (!validDepth.Contains(depth.ToLowerInvariant()))
    {
        AnsiConsole.MarkupLine($"[red]Invalid depth:[/] '{depth}'. Valid: Basic, Local, Deep, Global");
        Environment.Exit(1);
    }

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var pendingDir = Path.Combine(solutionExportDir, "_pending", "SecurityRoles");
    Directory.CreateDirectory(pendingDir);

    var safeName = roleName.Replace(" ", "_").Replace("/", "_");
    var destPath = Path.Combine(pendingDir, $"{safeName}.securityrole.json");

    // Load existing pending file if present (merge mode)
    var privileges = new List<PrivilegeEntry>();
    if (File.Exists(destPath))
    {
        try
        {
            var existing = JsonSerializer.Deserialize<SecurityRoleUpdateDefinition>(
                File.ReadAllText(destPath),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (existing != null)
                privileges = existing.Privileges.ToList();
        }
        catch { /* ignore parse errors, start fresh */ }
    }

    // Check for duplicate
    var newEntry = new PrivilegeEntry { Entity = entity, Access = access, Depth = depth };
    var duplicate = privileges.FirstOrDefault(p =>
        p.Entity.Equals(entity, StringComparison.OrdinalIgnoreCase) &&
        p.Access.Equals(access, StringComparison.OrdinalIgnoreCase));

    if (duplicate != null)
    {
        // Update depth if different
        if (!duplicate.Depth.Equals(depth, StringComparison.OrdinalIgnoreCase))
        {
            privileges.Remove(duplicate);
            privileges.Add(newEntry);
            AnsiConsole.MarkupLine($"[yellow]Updated existing privilege depth:[/] {access} on {entity} → {depth}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]Privilege already exists:[/] {access} on {entity} ({depth})");
            return;
        }
    }
    else
    {
        privileges.Add(newEntry);
    }

    var definition = new SecurityRoleUpdateDefinition
    {
        RoleName = roleName,
        Privileges = privileges
    };

    File.WriteAllText(destPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    }));

    AnsiConsole.MarkupLine($"[green]Privilege added to pending file:[/]");
    AnsiConsole.MarkupLine($"  Role:      {roleName}");
    AnsiConsole.MarkupLine($"  Entity:    {entity}");
    AnsiConsole.MarkupLine($"  Access:    {access}");
    AnsiConsole.MarkupLine($"  Depth:     {depth}");
    AnsiConsole.MarkupLine($"  File:      {destPath}");
    AnsiConsole.MarkupLine($"  Total:     {privileges.Count} privilege(s) pending");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Run [blue]commit[/] to apply, or add more privileges first.[/]");
}

static void HandleSecurityRoleUpdateCommand(string[] positionalArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync security-role update <role-name>");
        AnsiConsole.MarkupLine("[grey]Example: security-role update \"Partner Service\"[/]");
        AnsiConsole.MarkupLine("[grey]Creates a pending file with the role's current privileges. Edit to add/modify, then commit.[/]");
        Environment.Exit(1);
    }

    var roleName = positionalArgs[2];

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);

    // Try to load current privileges from exported SecurityRoles XML
    var currentPrivileges = LoadPrivilegesFromExport(baseDir, roleName);

    var definition = new SecurityRoleUpdateDefinition
    {
        RoleName = roleName,
        Privileges = currentPrivileges
    };

    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var pendingDir = Path.Combine(solutionExportDir, "_pending", "SecurityRoles");
    Directory.CreateDirectory(pendingDir);

    var safeName = roleName.Replace(" ", "_").Replace("/", "_");
    var destPath = Path.Combine(pendingDir, $"{safeName}.securityrole.json");
    File.WriteAllText(destPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    }));

    AnsiConsole.MarkupLine($"[green]Security role pending file created:[/]");
    AnsiConsole.MarkupLine($"  Role:       {roleName}");
    AnsiConsole.MarkupLine($"  Privileges: {currentPrivileges.Count}");
    AnsiConsole.MarkupLine($"  File:       {destPath}");
    AnsiConsole.WriteLine();
    if (currentPrivileges.Count > 0)
        AnsiConsole.MarkupLine("[yellow]The file contains the role's current privileges. Add or modify entries, then run [blue]commit[/].[/]");
    else
        AnsiConsole.MarkupLine("[yellow]No existing privileges found in export. Add privilege entries, then run [blue]commit[/].[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]IMPORTANT: Apply least privilege — only grant what the service actually needs.[/]");
}

/// <summary>
/// Load existing privileges from the SecurityRoles XML export.
/// Maps CRM privilege names (prvReadkf_partnerrelation) back to entity + access.
/// </summary>
static List<PrivilegeEntry> LoadPrivilegesFromExport(string baseDir, string roleName)
{
    var entries = new List<PrivilegeEntry>();
    var rolesDir = Path.Combine(baseDir, "SecurityRoles");
    var xmlPath = Path.Combine(rolesDir, $"{roleName}.xml");

    if (!File.Exists(xmlPath))
        return entries;

    try
    {
        var doc = System.Xml.Linq.XDocument.Load(xmlPath);
        var ns = doc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;
        var privilegesEl = doc.Root?.Element(ns + "Privileges");
        if (privilegesEl == null)
            return entries;

        // Parse privilege entries from XML (KeyValuePair structure from XrmMockup serialization)
        foreach (var kvp in privilegesEl.Elements())
        {
            // The XrmMockup format nests privileges as entity name → access → depth
            // For now, return empty if the XML privileges section is empty
        }
    }
    catch
    {
        // Ignore parse errors, return empty
    }

    return entries;
}

// ──────────────────────────────────────────────────────────────
// optionset add-value <name> <label> [--value <int>]
// ──────────────────────────────────────────────────────────────
static void HandleOptionSetCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 2)
    {
        PrintOptionSetUsage();
        Environment.Exit(1);
    }

    var subCommand = positionalArgs[1].ToLowerInvariant();

    switch (subCommand)
    {
        case "add-value":
            HandleOptionSetAddValueCommand(positionalArgs, allArgs);
            break;
        default:
            PrintOptionSetUsage();
            Environment.Exit(1);
            break;
    }
}

static void PrintOptionSetUsage()
{
    AnsiConsole.MarkupLine("[bold]MetadataSync optionset[/] — manage global option set values");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("  [yellow]add-value[/] <optionset-name> <label> [--value <int>]");
    AnsiConsole.MarkupLine("    Add a new value to a global option set. Merges with existing pending file if present.");
    AnsiConsole.MarkupLine("    [grey]If --value is omitted, CRM auto-assigns the next available integer.[/]");
    AnsiConsole.MarkupLine("    [grey]Example: optionset add-value kf_fieldinputtype \"Rådgiver\" --value 100000009[/]");
}

static void HandleOptionSetAddValueCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] optionset add-value <optionset-name> <label> [--value <int>]");
        AnsiConsole.MarkupLine("[grey]Example: optionset add-value kf_fieldinputtype \"Rådgiver\" --value 100000009[/]");
        Environment.Exit(1);
    }

    var optionSetName = positionalArgs[2].ToLowerInvariant();
    var label = positionalArgs[3];

    // Parse --value flag from allArgs (positionalArgs strips flags)
    int? value = null;
    var valueArg = ParseNamedArg(allArgs, "--value");
    if (valueArg != null && int.TryParse(valueArg, out var parsed))
        value = parsed;

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var pendingDir = Path.Combine(solutionExportDir, "_pending", "GlobalOptionSets");
    Directory.CreateDirectory(pendingDir);

    var destPath = Path.Combine(pendingDir, $"{optionSetName}.optionset.json");

    // Load existing pending file if present (merge mode)
    var values = new List<OptionSetValueEntry>();
    if (File.Exists(destPath))
    {
        try
        {
            var existing = JsonSerializer.Deserialize<OptionSetValueDefinition>(
                File.ReadAllText(destPath),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });
            if (existing != null)
                values = existing.Values.ToList();
        }
        catch { /* ignore parse errors, start fresh */ }
    }

    // Check for duplicate
    var newEntry = new OptionSetValueEntry { Label = label, Value = value };
    var duplicate = values.FirstOrDefault(v =>
        v.Label.Equals(label, StringComparison.OrdinalIgnoreCase)
        || (value.HasValue && v.Value == value));

    if (duplicate != null)
    {
        AnsiConsole.MarkupLine($"[yellow]Value already exists:[/] '{duplicate.Label}' = {duplicate.Value}");
        return;
    }

    values.Add(newEntry);

    var definition = new OptionSetValueDefinition
    {
        OptionSetName = optionSetName,
        Values = values
    };

    File.WriteAllText(destPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    }));

    AnsiConsole.MarkupLine($"[green]Option set value added to pending file:[/]");
    AnsiConsole.MarkupLine($"  Option Set: {optionSetName}");
    AnsiConsole.MarkupLine($"  Label:      {label}");
    AnsiConsole.MarkupLine($"  Value:      {(value.HasValue ? value.Value.ToString() : "(auto-assign)")}");
    AnsiConsole.MarkupLine($"  File:       {destPath}");
    AnsiConsole.MarkupLine($"  Total:      {values.Count} value(s) pending");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Run [blue]commit[/] to apply, or add more values first.[/]");
}

// ──────────────────────────────────────────────────────────────
// import <table> [file]  — scaffold or stage a data import file
// ──────────────────────────────────────────────────────────────
static void HandlePcfCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3 || HasFlag(allArgs, "--help") || HasFlag(allArgs, "-h"))
    {
        AnsiConsole.MarkupLine("[bold]MetadataSync pcf[/] — stage PCF control for deployment");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  pcf push <project-path> [--prefix <publisher-prefix>]");
        AnsiConsole.MarkupLine("    Stage a PCF control project for deployment via commit.");
        AnsiConsole.MarkupLine("    The project must have been initialized with 'pac pcf push' at least once.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Example:[/]");
        AnsiConsole.MarkupLine("  pcf push src/pcf/EnreachQueueControl --prefix kf");
        Environment.Exit(positionalArgs.Length < 3 ? 1 : 0);
        return;
    }

    if (!positionalArgs[1].Equals("push", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine($"[red]Unknown pcf subcommand:[/] {positionalArgs[1]}");
        Environment.Exit(1);
        return;
    }

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending");
    Directory.CreateDirectory(pendingDir);

    var projectPath = positionalArgs[2];
    if (!Path.IsPathRooted(projectPath))
        projectPath = Path.GetFullPath(projectPath);

    if (!Directory.Exists(projectPath))
    {
        AnsiConsole.MarkupLine($"[red]PCF project directory not found:[/] {projectPath}");
        Environment.Exit(1);
        return;
    }

    // Find the manifest to get the control name
    var manifestFiles = Directory.GetFiles(projectPath, "ControlManifest.Input.xml", SearchOption.AllDirectories);
    if (manifestFiles.Length == 0)
    {
        AnsiConsole.MarkupLine($"[red]No ControlManifest.Input.xml found in:[/] {projectPath}");
        Environment.Exit(1);
        return;
    }

    var manifest = XDocument.Load(manifestFiles[0]);
    var controlEl = manifest.Root!.Element("control")!;
    var ns = controlEl.Attribute("namespace")!.Value;
    var constructor = controlEl.Attribute("constructor")!.Value;
    var controlName = $"{ns}.{constructor}";

    var prefix = "kf";
    for (int i = 0; i < allArgs.Length - 1; i++)
    {
        if (allArgs[i].Equals("--prefix", StringComparison.OrdinalIgnoreCase))
        {
            prefix = allArgs[i + 1];
            break;
        }
    }

    // Verify cdsproj exists
    var cdsProj = Path.Combine(projectPath, $"obj/PowerAppsToolsTemp_{prefix}/PowerAppsToolsTemp_{prefix}.cdsproj");
    if (!File.Exists(cdsProj))
    {
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] cdsproj not found at: {cdsProj}");
        AnsiConsole.MarkupLine($"[yellow]Run 'pac pcf push --publisher-prefix {prefix}' once to scaffold it.[/]");
    }

    var def = new PcfControlDefinition
    {
        Name = controlName,
        ProjectPath = projectPath,
        PublisherPrefix = prefix,
    };

    var json = JsonSerializer.Serialize(def, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    });

    var fileName = $"{constructor}.pcf.json";
    var filePath = Path.Combine(pendingDir, fileName);
    File.WriteAllText(filePath, json);

    AnsiConsole.MarkupLine($"[green]Staged:[/] {controlName}");
    AnsiConsole.MarkupLine($"[grey]  File: {Path.GetRelativePath(baseDir, filePath)}[/]");
    AnsiConsole.MarkupLine($"[grey]  Project: {projectPath}[/]");
    AnsiConsole.MarkupLine($"[grey]  Prefix: {prefix}[/]");
}

static void HandleImportCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 2)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/]");
        AnsiConsole.MarkupLine("  import <table>         Scaffold an empty import template");
        AnsiConsole.MarkupLine("  import <table> <file>  Stage an existing import file");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[grey]Example: import kf_partnerformline[/]");
        AnsiConsole.MarkupLine("[grey]Example: import kf_partnerformline data/templates.import.json[/]");
        Environment.Exit(1);
    }

    var tableName = positionalArgs[1].ToLowerInvariant();
    var sourceFile = positionalArgs.Length >= 3 ? positionalArgs[2] : null;

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var pendingDir = Path.Combine(solutionExportDir, "_pending", "Import");
    Directory.CreateDirectory(pendingDir);

    var destPath = Path.Combine(pendingDir, $"{tableName}.import.json");

    if (sourceFile != null)
    {
        // File mode: stage an existing file
        if (!File.Exists(sourceFile))
        {
            AnsiConsole.MarkupLine($"[red]File not found:[/] {sourceFile}");
            Environment.Exit(1);
        }

        // Validate JSON structure
        try
        {
            var json = File.ReadAllText(sourceFile);
            var parsed = JsonSerializer.Deserialize<DataImportDefinition>(json,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });
            if (parsed?.Table == null || parsed.Rows == null || parsed.MatchOn == null)
                throw new InvalidOperationException("Missing required fields: table, matchOn, rows");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Invalid import file:[/] {ex.Message}");
            Environment.Exit(1);
        }

        File.Copy(sourceFile, destPath, overwrite: true);
        AnsiConsole.MarkupLine($"[green]Import file staged:[/] {destPath}");
    }
    else
    {
        // Scaffold mode: create template from entity metadata
        var fields = DiscoverEntityFields(baseDir, solutionExportDir, tableName);

        var template = new
        {
            table = tableName,
            matchOn = new[] { fields.FirstOrDefault() ?? "kf_name" },
            fieldTypes = BuildFieldTypeHints(fields, baseDir, solutionExportDir, tableName),
            rows = new[] { fields.ToDictionary(f => f, f => (object?)null) }
        };

        File.WriteAllText(destPath, JsonSerializer.Serialize(template, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        }));

        AnsiConsole.MarkupLine($"[green]Import template created:[/]");
        AnsiConsole.MarkupLine($"  Table:  {tableName}");
        AnsiConsole.MarkupLine($"  Fields: {fields.Count}");
        AnsiConsole.MarkupLine($"  File:   {destPath}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Edit the file to set matchOn fields and add row data, then run [blue]commit[/].[/]");
    }
}

/// <summary>
/// Discover entity fields from Model/entities markdown or Entity.xml.
/// Returns custom field logical names (kf_* prefix fields).
/// </summary>
static List<string> DiscoverEntityFields(string baseDir, string solutionExportDir, string tableName)
{
    // Try Model/entities/<table>.md first (has clean field list)
    var mdPath = Path.Combine(baseDir, "Model", "entities", $"{tableName}.md");
    if (File.Exists(mdPath))
    {
        var lines = File.ReadAllLines(mdPath);
        var fields = new List<string>();
        var inTable = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("| ") && line.Contains(" | ") && !line.Contains("---"))
            {
                if (line.Contains("Logical Name"))
                {
                    inTable = true;
                    continue;
                }
                if (inTable)
                {
                    var cols = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (cols.Length > 0)
                    {
                        var fieldName = cols[0].Trim();
                        // Include custom fields, skip virtual nav-property fields (end in "name" for lookups, yominame)
                        if (fieldName.StartsWith("kf_") && !fieldName.EndsWith("yominame")
                            && !(fieldName.EndsWith("name") && cols.Length >= 3 && cols[2].Trim().Contains("Virtual")))
                            fields.Add(fieldName);
                    }
                }
            }
            else if (inTable && !line.StartsWith("|"))
            {
                break;
            }
        }
        if (fields.Count > 0)
            return fields;
    }

    // Fallback: return a generic template
    return ["kf_name"];
}

/// <summary>
/// Build fieldTypes hints for known integer fields (not OptionSets).
/// </summary>
static Dictionary<string, string> BuildFieldTypeHints(List<string> fields, string baseDir, string solutionExportDir, string tableName)
{
    var hints = new Dictionary<string, string>();
    var mdPath = Path.Combine(baseDir, "Model", "entities", $"{tableName}.md");
    if (!File.Exists(mdPath))
        return hints;

    var lines = File.ReadAllLines(mdPath);
    var inTable = false;
    foreach (var line in lines)
    {
        if (line.StartsWith("| ") && line.Contains(" | ") && !line.Contains("---"))
        {
            if (line.Contains("Logical Name"))
            {
                inTable = true;
                continue;
            }
            if (inTable)
            {
                var cols = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length >= 3)
                {
                    var fieldName = cols[0].Trim();
                    var fieldType = cols[2].Trim();
                    if (fields.Contains(fieldName))
                    {
                        if (fieldType.StartsWith("Integer"))
                            hints[fieldName] = "int";
                        else if (fieldType.StartsWith("Memo"))
                            hints[fieldName] = "string";
                        else if (fieldType.Contains("Lookup"))
                            hints[fieldName] = "lookup";
                        else if (fieldType.StartsWith("MultiSelect"))
                            hints[fieldName] = "multiselect";
                    }
                }
            }
        }
        else if (inTable && !line.StartsWith("|"))
        {
            break;
        }
    }
    return hints;
}

// ──────────────────────────────────────────────────────────────
// relationship update <schema-name> --delete <behavior> [--assign <behavior>] ...
// ──────────────────────────────────────────────────────────────
static void HandleRelationshipCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3 || !positionalArgs[1].Equals("update", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync relationship update <schema-name> --delete <behavior>");
        AnsiConsole.MarkupLine("[grey]Behaviors: Cascade, RemoveLink, Restrict, NoCascade[/]");
        AnsiConsole.MarkupLine("[grey]Options: --delete, --assign, --share, --unshare, --reparent, --merge[/]");
        AnsiConsole.MarkupLine("[grey]Example: relationship update kf_partnerformline_PartnerForm_kf_partnerforms --delete Cascade[/]");
        Environment.Exit(1);
    }

    var schemaName = positionalArgs[2];
    var deleteBehavior = ParseNamedArg(allArgs, "--delete");
    var assignBehavior = ParseNamedArg(allArgs, "--assign");
    var shareBehavior = ParseNamedArg(allArgs, "--share");
    var unshareBehavior = ParseNamedArg(allArgs, "--unshare");
    var reparentBehavior = ParseNamedArg(allArgs, "--reparent");
    var mergeBehavior = ParseNamedArg(allArgs, "--merge");

    if (deleteBehavior == null && assignBehavior == null && shareBehavior == null
        && unshareBehavior == null && reparentBehavior == null && mergeBehavior == null)
    {
        AnsiConsole.MarkupLine("[red]At least one cascade behavior must be specified (e.g. --delete Cascade).[/]");
        Environment.Exit(1);
    }

    var definition = new RelationshipUpdateDefinition
    {
        SchemaName = schemaName,
        DeleteBehavior = deleteBehavior,
        AssignBehavior = assignBehavior,
        ShareBehavior = shareBehavior,
        UnshareBehavior = unshareBehavior,
        ReparentBehavior = reparentBehavior,
        MergeBehavior = mergeBehavior
    };

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var pendingDir = Path.Combine(solutionExportDir, "_pending");
    Directory.CreateDirectory(pendingDir);

    var destPath = Path.Combine(pendingDir, $"{schemaName}.relationship.json");
    File.WriteAllText(destPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    }));

    AnsiConsole.MarkupLine($"[green]Relationship update staged:[/]");
    AnsiConsole.MarkupLine($"  Relationship: {schemaName}");
    if (deleteBehavior != null) AnsiConsole.MarkupLine($"  Delete:       {deleteBehavior}");
    if (assignBehavior != null) AnsiConsole.MarkupLine($"  Assign:       {assignBehavior}");
    if (shareBehavior != null) AnsiConsole.MarkupLine($"  Share:        {shareBehavior}");
    if (unshareBehavior != null) AnsiConsole.MarkupLine($"  Unshare:      {unshareBehavior}");
    if (reparentBehavior != null) AnsiConsole.MarkupLine($"  Reparent:     {reparentBehavior}");
    if (mergeBehavior != null) AnsiConsole.MarkupLine($"  Merge:        {mergeBehavior}");
    AnsiConsole.MarkupLine($"  File:         {destPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// plugin register <dll-path> — scaffold plugin registration pending file
// ──────────────────────────────────────────────────────────────
static void HandlePluginCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length >= 2 && positionalArgs[1].Equals("remove", StringComparison.OrdinalIgnoreCase))
    {
        HandlePluginRemoveCommand(positionalArgs, allArgs);
        return;
    }

    if (positionalArgs.Length < 2 || (
        !positionalArgs[1].Equals("register", StringComparison.OrdinalIgnoreCase) &&
        !positionalArgs[1].Equals("update", StringComparison.OrdinalIgnoreCase)))
    {
        AnsiConsole.MarkupLine("[red]Usage:[/]");
        AnsiConsole.MarkupLine("  plugin register <dll-path>     Create a new plugin registration pending file");
        AnsiConsole.MarkupLine("  plugin update <dll-path>       Re-sync a previously registered plugin (keeps types/steps)");
        AnsiConsole.MarkupLine("  plugin remove <assembly-name>  Remove a plugin assembly and its steps from CRM");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[grey]Example: plugin register src/MyPlugin/bin/Release/net462/MyPlugin.dll[/]");
        Environment.Exit(1);
    }

    var subCommand = positionalArgs[1].ToLowerInvariant();

    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine($"[red]Usage:[/] plugin {subCommand} <dll-path>");
        Environment.Exit(1);
    }

    var dllPath = positionalArgs[2];
    if (!File.Exists(dllPath))
    {
        var fullPath = Path.GetFullPath(dllPath);
        if (!File.Exists(fullPath))
        {
            AnsiConsole.MarkupLine($"[red]DLL not found:[/] {dllPath}");
            Environment.Exit(1);
        }
        dllPath = fullPath;
    }

    var assemblyName = Path.GetFileNameWithoutExtension(dllPath);

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    // Read solution unique name
    var solutionFolder = GetSolutionFolder(solutionExportDir);
    var solutionXmlPath = Path.Combine(solutionFolder, "Other", "Solution.xml");
    var solDoc = System.Xml.Linq.XDocument.Parse(File.ReadAllText(solutionXmlPath));
    var solutionUniqueName = solDoc.Descendants("UniqueName").FirstOrDefault()?.Value
        ?? throw new InvalidOperationException("Cannot find solution UniqueName in Solution.xml");

    // Make assembly path relative to baseDir
    var relativeDllPath = Path.GetRelativePath(baseDir, Path.GetFullPath(dllPath));

    PluginRegistrationDefinition definition;

    if (subCommand == "update")
    {
        // Look for previous committed or pending definition to reuse types/steps
        var committedPath = Path.Combine(solutionExportDir, "_committed", "PluginAssemblies", $"{assemblyName}.plugin.json");
        var pendingPath = Path.Combine(solutionExportDir, "_pending", "PluginAssemblies", $"{assemblyName}.plugin.json");

        string? previousPath = File.Exists(committedPath) ? committedPath
            : File.Exists(pendingPath) ? pendingPath
            : null;

        if (previousPath != null)
        {
            var previous = PluginRegistrationFileReader.Parse(previousPath);
            definition = previous with { AssemblyPath = relativeDllPath };
            AnsiConsole.MarkupLine($"[green]Reusing types/steps from previous registration ({previous.Types.Count} type(s)).[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]No previous registration found for '{assemblyName}'. Creating fresh skeleton.[/]");
            definition = new PluginRegistrationDefinition
            {
                AssemblyName = assemblyName,
                AssemblyPath = relativeDllPath,
                IsolationMode = 2,
                SourceType = 0,
                SolutionUniqueName = solutionUniqueName,
                Types = []
            };
        }
    }
    else
    {
        // Fresh registration — empty types for the agent to fill in
        definition = new PluginRegistrationDefinition
        {
            AssemblyName = assemblyName,
            AssemblyPath = relativeDllPath,
            IsolationMode = 2,
            SourceType = 0,
            SolutionUniqueName = solutionUniqueName,
            Types = []
        };
    }

    var pendingDir = Path.Combine(solutionExportDir, "_pending", "PluginAssemblies");
    Directory.CreateDirectory(pendingDir);

    var destPath = Path.Combine(pendingDir, $"{assemblyName}.plugin.json");
    File.WriteAllText(destPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));

    AnsiConsole.MarkupLine($"[green]Plugin {subCommand} pending file created:[/]");
    AnsiConsole.MarkupLine($"  Assembly: {assemblyName}");
    AnsiConsole.MarkupLine($"  DLL:      {relativeDllPath}");
    AnsiConsole.MarkupLine($"  Solution: {solutionUniqueName}");
    AnsiConsole.MarkupLine($"  File:     {destPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Edit the file to add plugin types and steps, then run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// plugin remove <assembly-name> — stage deletion of plugin assembly + steps
// ──────────────────────────────────────────────────────────────
static void HandlePluginRemoveCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync plugin remove <assembly-name>");
        AnsiConsole.MarkupLine("[grey]Example: plugin remove PartnerRelationRootAccount[/]");
        Environment.Exit(1);
    }

    var assemblyName = positionalArgs[2];

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    // Find plugin assembly in solution export
    var solutionFolder = GetSolutionFolder(solutionExportDir);
    var pluginAssembliesDir = Path.Combine(solutionFolder, "PluginAssemblies");
    var stepsDir = Path.Combine(solutionFolder, "SdkMessageProcessingSteps");

    // Find the assembly folder
    string? assemblyFolder = null;
    Guid assemblyId = Guid.Empty;
    if (Directory.Exists(pluginAssembliesDir))
    {
        foreach (var dir in Directory.GetDirectories(pluginAssembliesDir))
        {
            var folderName = Path.GetFileName(dir);
            if (folderName.StartsWith(assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                assemblyFolder = dir;
                // Extract GUID from folder name: "AssemblyName-XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"
                var nameLen = assemblyName.Length;
                if (folderName.Length > nameLen + 1 && folderName[nameLen] == '-')
                {
                    var guidStr = folderName[(nameLen + 1)..];
                    if (Guid.TryParse(guidStr, out var parsed))
                        assemblyId = parsed;
                }
                break;
            }
        }
    }

    // Find plugin type ID from the assembly data.xml
    var pluginTypeNames = new List<string>();
    var pluginTypeIds = new List<Guid>();
    if (assemblyFolder != null)
    {
        var dataXml = Directory.GetFiles(assemblyFolder, "*.data.xml").FirstOrDefault();
        if (dataXml != null)
        {
            var doc = System.Xml.Linq.XDocument.Load(dataXml);
            foreach (var pt in doc.Descendants("PluginType"))
            {
                var name = pt.Attribute("Name")?.Value;
                var idStr = pt.Attribute("PluginTypeId")?.Value;
                if (name != null) pluginTypeNames.Add(name);
                if (idStr != null && Guid.TryParse(idStr, out var ptId))
                    pluginTypeIds.Add(ptId);
            }
        }
    }

    // Find steps that reference this assembly's plugin types
    var stepFiles = new List<(Guid StepId, string Name, string FilePath)>();
    if (Directory.Exists(stepsDir))
    {
        foreach (var stepFile in Directory.GetFiles(stepsDir, "*.xml"))
        {
            var stepDoc = System.Xml.Linq.XDocument.Load(stepFile);
            var root = stepDoc.Root;
            if (root == null) continue;

            var pluginTypeName = root.Element("PluginTypeName")?.Value ?? "";
            // Check if this step belongs to our assembly
            if (pluginTypeNames.Any(pt => pluginTypeName.StartsWith(pt, StringComparison.OrdinalIgnoreCase))
                || pluginTypeName.Contains(assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                var stepIdStr = root.Attribute("SdkMessageProcessingStepId")?.Value?.Trim('{', '}');
                var stepName = root.Attribute("Name")?.Value ?? "Unknown step";
                if (stepIdStr != null && Guid.TryParse(stepIdStr, out var stepId))
                    stepFiles.Add((stepId, stepName, stepFile));
            }
        }
    }

    // Create pending delete files
    var pendingDir = Path.Combine(solutionExportDir, "_pending", "Deletes");
    Directory.CreateDirectory(pendingDir);

    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    var createdFiles = new List<string>();

    // Steps first (must be deleted before assembly)
    foreach (var (stepId, stepName, _) in stepFiles)
    {
        var def = new DeleteDefinition
        {
            EntityType = "sdkmessageprocessingstep",
            ComponentId = stepId,
            DisplayName = stepName
        };
        var fileName = $"1_step_{stepId:N}.delete.json";
        var path = Path.Combine(pendingDir, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(def, jsonOptions));
        createdFiles.Add(path);
    }

    // Then the assembly
    if (assemblyId != Guid.Empty)
    {
        var def = new DeleteDefinition
        {
            EntityType = "pluginassembly",
            ComponentId = assemblyId,
            DisplayName = $"Assembly: {assemblyName}"
        };
        var path = Path.Combine(pendingDir, $"2_assembly_{assemblyName}.delete.json");
        File.WriteAllText(path, JsonSerializer.Serialize(def, jsonOptions));
        createdFiles.Add(path);
    }

    AnsiConsole.MarkupLine($"[green]Plugin removal staged:[/]");
    AnsiConsole.MarkupLine($"  Assembly: {assemblyName} ({assemblyId})");
    AnsiConsole.MarkupLine($"  Steps:   {stepFiles.Count}");
    foreach (var (_, name, _) in stepFiles)
        AnsiConsole.MarkupLine($"    - {name}");
    AnsiConsole.MarkupLine($"  Files:   {createdFiles.Count} delete file(s) created");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Run [blue]commit[/] to remove from CRM.[/]");

    if (assemblyId == Guid.Empty)
        AnsiConsole.MarkupLine("[red]WARNING: Could not find assembly in solution export. You may need to set the ComponentId manually.[/]");
}

// ──────────────────────────────────────────────────────────────
// deprecate <entity> <attribute> — mark a field as deprecated
// ──────────────────────────────────────────────────────────────
static void HandleDeprecateCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 3)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync deprecate <entity> <attribute>");
        AnsiConsole.MarkupLine("[grey]Example: deprecate lead cr_department[/]");
        AnsiConsole.MarkupLine("[grey]Prefixes the field display name with \"ZZ\" so it sorts last and is clearly deprecated.[/]");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[1].ToLowerInvariant();
    var attributeLogicalName = positionalArgs[2].ToLowerInvariant();

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    // Look up current display name from Model/entities/<entity>.md
    var modelPath = Path.Combine(baseDir, "Model", "entities", $"{entityLogicalName}.md");
    if (!File.Exists(modelPath))
    {
        AnsiConsole.MarkupLine($"[red]Model file not found:[/] {modelPath}");
        AnsiConsole.MarkupLine("[grey]Run a metadata sync first to generate Model/entities/.[/]");
        Environment.Exit(1);
    }

    string? originalDisplayName = null;
    foreach (var line in File.ReadLines(modelPath))
    {
        // Table format: | logical_name | Display Name | Type | ...
        if (line.StartsWith("|") && line.Contains(attributeLogicalName))
        {
            var cols = line.Split('|', StringSplitOptions.None);
            if (cols.Length >= 3)
            {
                var logicalCol = cols[1].Trim();
                if (logicalCol.Equals(attributeLogicalName, StringComparison.OrdinalIgnoreCase))
                {
                    originalDisplayName = cols[2].Trim();
                    break;
                }
            }
        }
    }

    if (originalDisplayName == null)
    {
        AnsiConsole.MarkupLine($"[red]Attribute '{attributeLogicalName}' not found on entity '{entityLogicalName}'.[/]");
        Environment.Exit(1);
    }

    if (originalDisplayName.StartsWith("ZZ", StringComparison.OrdinalIgnoreCase)
        || originalDisplayName.StartsWith("(Deprecated)", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine($"[yellow]Attribute '{attributeLogicalName}' already appears deprecated:[/] {originalDisplayName}");
        return;
    }

    var newDisplayName = $"ZZ {originalDisplayName}";

    var definition = new DeprecateDefinition
    {
        EntityLogicalName = entityLogicalName,
        AttributeLogicalName = attributeLogicalName,
        OriginalDisplayName = originalDisplayName,
        NewDisplayName = newDisplayName
    };

    var pendingDir = Path.Combine(solutionExportDir, "_pending", "Deprecates");
    Directory.CreateDirectory(pendingDir);

    var destPath = Path.Combine(pendingDir, $"{entityLogicalName}_{attributeLogicalName}.deprecate.json");
    File.WriteAllText(destPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));

    AnsiConsole.MarkupLine($"[green]Staged deprecation:[/]");
    AnsiConsole.MarkupLine($"  Entity:    {entityLogicalName}");
    AnsiConsole.MarkupLine($"  Attribute: {attributeLogicalName}");
    AnsiConsole.MarkupLine($"  Rename:    \"{originalDisplayName}\" → \"{newDisplayName}\"");
    AnsiConsole.MarkupLine($"  File:      {destPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// appmodule list [--app <name>] — list AppModule components
// ──────────────────────────────────────────────────────────────
static void HandleAppModuleListCommand(string[] positionalArgs, string[] allArgs)
{
    var appModuleName = ParseNamedArg(allArgs, "--app");

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    var (selectedAppModuleUniqueName, selectedAppModuleXmlPath) = ResolveAppModule(solutionExportDir, appModuleName);

    var components = ReadAppModuleComponents(selectedAppModuleXmlPath);

    if (components.Count == 0)
    {
        AnsiConsole.MarkupLine($"[yellow]No components found in AppModule '{selectedAppModuleUniqueName}'.[/]");
        return;
    }

    // Map type numbers to friendly names
    static string TypeName(string type) => type switch
    {
        "1" => "Entity",
        "26" => "View",
        "60" => "Form",
        "62" => "SiteMap",
        _ => $"Type {type}"
    };

    // For type=26 views and type=60 forms, try to resolve names from local XML files
    var viewNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var formNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var entityFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var solDir in Directory.GetDirectories(solutionExportDir))
    {
        var dirName = Path.GetFileName(solDir);
        if (dirName.StartsWith('.') || dirName.StartsWith('_')) continue;

        var entitiesDir = Path.Combine(solDir, "Entities");
        if (!Directory.Exists(entitiesDir)) continue;

        foreach (var entityDir in Directory.GetDirectories(entitiesDir))
        {
            var savedQueriesDir = Path.Combine(entityDir, "SavedQueries");
            if (Directory.Exists(savedQueriesDir))
            {
                foreach (var xmlFile in Directory.GetFiles(savedQueriesDir, "*.xml"))
                {
                    try
                    {
                        var parsed = SavedQueryFileReader.Parse(xmlFile);
                        var idStr = parsed.SavedQueryId.ToString("B").ToUpperInvariant();
                        viewNames.TryAdd(idStr, parsed.Name);
                    }
                    catch { /* skip */ }
                }
            }

            var formXmlDir = Path.Combine(entityDir, "FormXml", "main");
            if (Directory.Exists(formXmlDir))
            {
                foreach (var xmlFile in Directory.GetFiles(formXmlDir, "*.xml"))
                {
                    try
                    {
                        var parsed = SystemFormFileReader.Parse(xmlFile);
                        if (parsed.FormId != Guid.Empty)
                        {
                            var idStr = parsed.FormId.ToString("B").ToUpperInvariant();
                            formNames.TryAdd(idStr, parsed.Name);
                        }
                    }
                    catch { /* skip */ }
                }
            }
        }
    }

    var table = new Table().Border(TableBorder.Rounded)
        .AddColumn("Type")
        .AddColumn("Component");

    foreach (var comp in components)
    {
        var typeName = TypeName(comp.Type);
        var displayName = comp.SchemaName;

        if (comp.Type == "26" && comp.Id != null)
        {
            var normalizedId = comp.Id.Trim('{', '}');
            var bracketId = "{" + normalizedId.ToUpperInvariant() + "}";
            if (viewNames.TryGetValue(bracketId, out var name))
                displayName = $"{name} ({normalizedId})";
            else
                displayName = normalizedId;
        }
        else if (comp.Type == "60" && comp.Id != null)
        {
            var normalizedId = comp.Id.Trim('{', '}');
            var bracketId = "{" + normalizedId.ToUpperInvariant() + "}";
            if (formNames.TryGetValue(bracketId, out var name))
                displayName = $"{name} ({normalizedId})";
            else
                displayName = normalizedId;
        }

        table.AddRow(typeName, Markup.Escape(displayName));
    }

    AnsiConsole.MarkupLine($"[bold]AppModule:[/] {selectedAppModuleUniqueName}");
    AnsiConsole.Write(table);
}

// ──────────────────────────────────────────────────────────────
// sitemap <appmodule-name> — checkout a sitemap for editing
// ──────────────────────────────────────────────────────────────
static async Task HandleSiteMapCommand(string[] positionalArgs, IConfiguration configuration, bool noCache)
{
    if (positionalArgs.Length < 2)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync sitemap <appmodule-name>");
        Environment.Exit(1);
    }

    var appModuleName = positionalArgs[1];

    var metadataPath = FindConnectionMetadata();
    var metadata = ReadConnectionMetadata(metadataPath);
    var baseDir = GetBaseDir(metadataPath);

    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var solutionFolder = GetSolutionFolder(solutionExportDir);

    // Find AppModuleSiteMaps/<name>/AppModuleSiteMap.xml (case-insensitive)
    var siteMapDir = Path.Combine(solutionFolder, "AppModuleSiteMaps");
    if (!Directory.Exists(siteMapDir))
    {
        AnsiConsole.MarkupLine($"[red]No AppModuleSiteMaps directory found in solution export.[/]");
        Environment.Exit(1);
    }

    var matchingDir = Directory.GetDirectories(siteMapDir)
        .FirstOrDefault(d => Path.GetFileName(d).Equals(appModuleName, StringComparison.OrdinalIgnoreCase));

    if (matchingDir == null)
    {
        var available = Directory.GetDirectories(siteMapDir).Select(Path.GetFileName);
        AnsiConsole.MarkupLine($"[red]AppModule not found:[/] {appModuleName}");
        AnsiConsole.MarkupLine($"[grey]Available: {string.Join(", ", available)}[/]");
        Environment.Exit(1);
    }

    var sourceFile = Path.Combine(matchingDir, "AppModuleSiteMap.xml");
    if (!File.Exists(sourceFile))
    {
        AnsiConsole.MarkupLine($"[red]AppModuleSiteMap.xml not found in:[/] {matchingDir}");
        Environment.Exit(1);
    }

    var folderName = Path.GetFileName(matchingDir);
    var relativePath = Path.Combine("AppModuleSiteMaps", folderName, "AppModuleSiteMap.xml");
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending");
    var destPath = Path.Combine(pendingDir, relativePath);

    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
    File.Copy(sourceFile, destPath, overwrite: true);

    var parsed = SiteMapFileReader.Parse(destPath, folderName);

    // Print agent guidance
    Console.WriteLine();
    Console.WriteLine($"═══ CHECKED OUT: AppModuleSiteMap — {parsed.Name} ═══");
    Console.WriteLine($"File: {destPath}");
    Console.WriteLine();
    Console.WriteLine("EDITABLE ELEMENTS:");
    Console.WriteLine("  • <SubArea> — Add/remove/reorder entity entries (Entity=\"logicalname\")");
    Console.WriteLine("  • <Group>  — Add/remove groups, change <Title> text");
    Console.WriteLine("  • <Area>   — Add/remove areas, change <Title> text");
    Console.WriteLine();
    Console.WriteLine("EXAMPLE — Add entity to a group:");
    Console.WriteLine("  <SubArea Id=\"subarea_new\" Entity=\"opportunity\" />");
    Console.WriteLine();
    Console.WriteLine("COMMIT: Run `MetadataSync commit` when ready to push changes.");
}

// ──────────────────────────────────────────────────────────────
// entity <logical-name> — checkout an entity file for editing
// ──────────────────────────────────────────────────────────────
static async Task HandleEntityCommand(string[] positionalArgs, IConfiguration configuration, bool noCache)
{
    if (positionalArgs.Length < 2)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync entity <entity-logical-name>");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[1].ToLowerInvariant();

    var metadataPath = FindConnectionMetadata();
    var metadata = ReadConnectionMetadata(metadataPath);
    var baseDir = GetBaseDir(metadataPath);

    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");
    var solutionFolder = GetSolutionFolder(solutionExportDir);

    // Find Entities/<Name>/Entity.xml (case-insensitive)
    var entitiesDir = Path.Combine(solutionFolder, "Entities");
    if (!Directory.Exists(entitiesDir))
    {
        AnsiConsole.MarkupLine($"[red]No Entities directory found in solution export.[/]");
        Environment.Exit(1);
    }

    var matchingDir = Directory.GetDirectories(entitiesDir)
        .FirstOrDefault(d => Path.GetFileName(d).Equals(entityLogicalName, StringComparison.OrdinalIgnoreCase));

    if (matchingDir == null)
    {
        AnsiConsole.MarkupLine($"[red]Entity not found:[/] {entityLogicalName}");
        AnsiConsole.MarkupLine("[grey]Available entities:[/]");
        foreach (var dir in Directory.GetDirectories(entitiesDir).Take(20))
            AnsiConsole.MarkupLine($"[grey]  {Path.GetFileName(dir)}[/]");
        Environment.Exit(1);
    }

    var sourceFile = Path.Combine(matchingDir, "Entity.xml");
    if (!File.Exists(sourceFile))
    {
        AnsiConsole.MarkupLine($"[red]Entity.xml not found in:[/] {matchingDir}");
        Environment.Exit(1);
    }

    var folderName = Path.GetFileName(matchingDir);
    var relativePath = Path.Combine("Entities", folderName, "Entity.xml");
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending");
    var destPath = Path.Combine(pendingDir, relativePath);

    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
    File.Copy(sourceFile, destPath, overwrite: true);

    // Git commit baseline so edits show as diff (only this file, not other pending changes)
    var solutionExportGitDir = Path.Combine(baseDir, "SolutionExport");
    var entityRelPath = Path.GetRelativePath(solutionExportGitDir, destPath);
    if (GitHelper.IsGitRepo(solutionExportGitDir))
    {
        GitHelper.CommitFiles(solutionExportGitDir, [entityRelPath], $"Checkout: Entity {folderName}");
    }

    var parsed = EntityFileReader.Parse(destPath);
    var customAttrs = parsed.Attributes.Where(a => a.IsCustomField).ToList();

    // Print agent guidance
    Console.WriteLine();
    Console.WriteLine($"═══ CHECKED OUT: Entity — {parsed.DisplayName} ({customAttrs.Count} custom fields) ═══");
    Console.WriteLine($"File: {destPath}");
    Console.WriteLine();
    Console.WriteLine("EDITABLE FIELDS (per <attribute> where IsCustomField=1):");
    Console.WriteLine("  • <displaynames>/<displayname description=\"...\"> — Display name");
    Console.WriteLine("  • <Descriptions>/<Description description=\"...\"> — Field description");
    Console.WriteLine("  • <RequiredLevel> — none | required");
    Console.WriteLine("  • <MaxLength> — String max length (nvarchar only)");
    Console.WriteLine();

    if (customAttrs.Count > 0)
    {
        Console.WriteLine("CUSTOM ATTRIBUTES:");
        var maxNameLen = customAttrs.Max(a => a.LogicalName.Length);
        var maxTypeLen = customAttrs.Max(a => a.Type.Length);
        foreach (var attr in customAttrs)
        {
            Console.WriteLine($"  {attr.LogicalName.PadRight(maxNameLen)}  {attr.Type.PadRight(maxTypeLen)}  \"{attr.DisplayName}\"");
        }
        Console.WriteLine();
    }

    Console.WriteLine("READ-ONLY: System attributes (IsCustomField=0), <Type>, PhysicalName, LogicalName");
    Console.WriteLine();
    Console.WriteLine("COMMIT: Run `MetadataSync commit` when ready to push changes.");
}

// ──────────────────────────────────────────────────────────────
// icon new / icon set — stage icon changes into _pending/
// ──────────────────────────────────────────────────────────────
static void HandleIconCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 2)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/]");
        AnsiConsole.MarkupLine("  MetadataSync icon new <webresource-name> <svg-file-path> [[--entity <logical-name>]]");
        AnsiConsole.MarkupLine("  MetadataSync icon set <entity-logical-name> <webresource-name>");
        Environment.Exit(1);
    }

    var subCommand = positionalArgs[1];

    if (subCommand.Equals("new", StringComparison.OrdinalIgnoreCase))
    {
        HandleIconNewCommand(positionalArgs, allArgs);
    }
    else if (subCommand.Equals("set", StringComparison.OrdinalIgnoreCase))
    {
        HandleIconSetCommand(positionalArgs);
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]Unknown icon subcommand:[/] {subCommand}");
        AnsiConsole.MarkupLine("[grey]Available: new, set[/]");
        Environment.Exit(1);
    }
}

static void HandleIconNewCommand(string[] positionalArgs, string[] allArgs)
{
    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync icon new <webresource-name> <svg-file-path> [[--entity <logical-name>]]");
        Environment.Exit(1);
    }

    var webResourceName = positionalArgs[2];
    var svgFilePath = positionalArgs[3];

    if (!File.Exists(svgFilePath))
    {
        AnsiConsole.MarkupLine($"[red]SVG file not found:[/] {svgFilePath}");
        Environment.Exit(1);
    }

    // Parse optional --entity flag
    string? entityLogicalName = null;
    for (var i = 0; i < allArgs.Length; i++)
    {
        if (allArgs[i].Equals("--entity", StringComparison.OrdinalIgnoreCase) && i + 1 < allArgs.Length)
        {
            entityLogicalName = allArgs[i + 1];
            break;
        }
    }

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var pendingIconsDir = Path.Combine(baseDir, "SolutionExport", "_pending", "Icons");
    Directory.CreateDirectory(pendingIconsDir);

    // Replace slashes with dashes for safe file names
    var safeName = webResourceName.Replace("/", "-").Replace("\\", "-");

    // Copy SVG file
    var svgDestPath = Path.Combine(pendingIconsDir, $"{safeName}.svg");
    File.Copy(svgFilePath, svgDestPath, overwrite: true);

    // Derive display name from the web resource name
    var displayName = Path.GetFileNameWithoutExtension(webResourceName.Split('/').Last());
    displayName = char.ToUpper(displayName[0]) + displayName[1..] + " Icon";

    // Write JSON marker
    var definition = new IconUploadDefinition
    {
        WebResourceName = webResourceName,
        DisplayName = displayName,
        SvgFile = $"{safeName}.svg",
        EntityLogicalName = entityLogicalName
    };

    var jsonPath = Path.Combine(pendingIconsDir, $"{safeName}.json");
    var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(jsonPath, json);

    AnsiConsole.MarkupLine($"[green]Staged icon upload:[/]");
    AnsiConsole.MarkupLine($"  SVG:    {svgDestPath}");
    AnsiConsole.MarkupLine($"  Marker: {jsonPath}");
    if (entityLogicalName != null)
        AnsiConsole.MarkupLine($"  Entity: {entityLogicalName} → IconVectorName = {webResourceName}");
    AnsiConsole.MarkupLine($"[grey]Run [blue]commit[/] to upload to CRM.[/]");
}

static void HandleIconSetCommand(string[] positionalArgs)
{
    if (positionalArgs.Length < 4)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] MetadataSync icon set <entity-logical-name> <webresource-name>");
        Environment.Exit(1);
    }

    var entityLogicalName = positionalArgs[2];
    var webResourceName = positionalArgs[3];

    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var pendingIconsDir = Path.Combine(baseDir, "SolutionExport", "_pending", "Icons");
    Directory.CreateDirectory(pendingIconsDir);

    var definition = new IconSetDefinition
    {
        EntityLogicalName = entityLogicalName,
        IconVectorName = webResourceName
    };

    var jsonPath = Path.Combine(pendingIconsDir, $"{entityLogicalName}.icon.json");
    var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(jsonPath, json);

    AnsiConsole.MarkupLine($"[green]Staged icon assignment:[/]");
    AnsiConsole.MarkupLine($"  Entity: {entityLogicalName} → IconVectorName = {webResourceName}");
    AnsiConsole.MarkupLine($"  Marker: {jsonPath}");
    AnsiConsole.MarkupLine($"[grey]Run [blue]commit[/] to push to CRM.[/]");
}

// ──────────────────────────────────────────────────────────────
// commit — push pending changes to CRM with human approval
// ──────────────────────────────────────────────────────────────
// ──────────────────────────────────────────────────────────────
// pending — list all staged changes in _pending/
// ──────────────────────────────────────────────────────────────
static void HandlePendingCommand()
{
    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending");

    if (!Directory.Exists(pendingDir))
    {
        AnsiConsole.MarkupLine("[yellow]No pending changes.[/]");
        return;
    }

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
        .Where(f => f.Contains("Deletes", StringComparison.OrdinalIgnoreCase))
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

    var items = new List<(string Type, string Label, string File)>();

    foreach (var f in pendingViewFiles)
    {
        var parsed = SavedQueryFileReader.Parse(f);
        var label = parsed.SavedQueryId == Guid.Empty
            ? $"{parsed.Name} (new)"
            : $"{parsed.Name} ({parsed.SavedQueryId})";
        items.Add(("View", label, Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingSiteMapFiles)
    {
        var folderName = Path.GetFileName(Path.GetDirectoryName(f))!;
        var parsed = SiteMapFileReader.Parse(f, folderName);
        items.Add(("SiteMap", $"{parsed.Name} ({parsed.UniqueName})", Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingEntityFiles)
    {
        var parsed = EntityFileReader.Parse(f);
        var customCount = parsed.Attributes.Count(a => a.IsCustomField);
        items.Add(("Entity", $"{parsed.DisplayName} ({customCount} custom fields)", Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingIconFiles)
    {
        if (f.EndsWith(".icon.json", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = JsonSerializer.Deserialize<IconSetDefinition>(File.ReadAllText(f))!;
            items.Add(("Icon Set", $"{parsed.EntityLogicalName} → {parsed.IconVectorName}", Path.GetRelativePath(pendingDir, f)));
        }
        else
        {
            var parsed = JsonSerializer.Deserialize<IconUploadDefinition>(File.ReadAllText(f))!;
            items.Add(("Icon Upload", parsed.WebResourceName, Path.GetRelativePath(pendingDir, f)));
        }
    }

    foreach (var f in pendingAppModuleEntityFiles)
    {
        var parsed = JsonSerializer.Deserialize<AppModuleEntityDefinition>(File.ReadAllText(f),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        items.Add(("AppModule Entity", $"{parsed.AppModuleUniqueName} / {parsed.EntityLogicalName}", Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingAppModuleViewFiles)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(f));
        var root = doc.RootElement;
        var appName = root.GetProperty("appModuleUniqueName").GetString() ?? "?";
        var entityName = root.GetProperty("entityLogicalName").GetString() ?? "?";
        var viewCount = root.GetProperty("viewIds").GetArrayLength();
        items.Add(("AppModule Views", $"{appName} / {entityName} ({viewCount} views)", Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingFormFiles)
    {
        var parsed = SystemFormFileReader.Parse(f);
        var label = parsed.FormId == Guid.Empty
            ? $"{parsed.Name} (new)"
            : $"{parsed.Name} ({parsed.FormId})";
        items.Add(("Form", label, Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingAppModuleFormFiles)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(f));
        var root = doc.RootElement;
        var appName = root.GetProperty("appModuleUniqueName").GetString() ?? "?";
        var entityName = root.GetProperty("entityLogicalName").GetString() ?? "?";
        var formCount = root.GetProperty("formIds").GetArrayLength();
        items.Add(("AppModule Forms", $"{appName} / {entityName} ({formCount} forms)", Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingBusinessRuleFiles)
    {
        var parsed = BusinessRuleFileReader.Parse(f);
        var label = parsed.WorkflowId == Guid.Empty
            ? $"{parsed.Name} (new)"
            : $"{parsed.Name} ({parsed.WorkflowId})";
        items.Add(("Business Rule", label, Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingDeleteFiles)
    {
        var parsed = JsonSerializer.Deserialize<DeleteDefinition>(File.ReadAllText(f),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        items.Add(("Delete", $"{parsed.EntityType}: {parsed.DisplayName} ({parsed.ComponentId})", Path.GetRelativePath(pendingDir, f)));
    }

    var pendingNewAttributeFiles = Directory.GetFiles(pendingDir, "*.attribute.json", SearchOption.AllDirectories)
        .ToList();

    foreach (var f in pendingNewAttributeFiles)
    {
        var parsed = JsonSerializer.Deserialize<NewAttributeDefinition>(File.ReadAllText(f),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        var typeLabel = parsed.AttributeType == "lookup"
            ? $"Lookup → {parsed.TargetEntityLogicalName}"
            : parsed.AttributeType;
        var actionLabel = string.Equals(parsed.Action, "update", StringComparison.OrdinalIgnoreCase)
            ? "Update Attribute"
            : "New Attribute";
        items.Add((actionLabel, $"{parsed.EntityLogicalName}.{parsed.AttributeLogicalName} ({typeLabel}, \"{parsed.DisplayName}\")", Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingWebResourceFiles)
    {
        var parsed = JsonSerializer.Deserialize<WebResourceUploadDefinition>(File.ReadAllText(f))!;
        items.Add(("WebResource", parsed.WebResourceName, Path.GetRelativePath(pendingDir, f)));
    }

    foreach (var f in pendingCommandBarFiles)
    {
        var parsed = AppActionFileReader.Parse(f);
        items.Add(("CommandBar", $"{parsed.Label ?? parsed.Name ?? parsed.UniqueName} ({parsed.EntityLogicalName}, {parsed.UniqueName})", Path.GetRelativePath(pendingDir, f)));
    }

    var pendingPluginFiles = Directory.GetFiles(pendingDir, "*.plugin.json", SearchOption.AllDirectories)
        .Where(f => f.Contains("PluginAssemblies", StringComparison.OrdinalIgnoreCase))
        .ToList();

    foreach (var f in pendingPluginFiles)
    {
        var parsed = PluginRegistrationFileReader.Parse(f);
        var stepCount = parsed.Types.SelectMany(t => t.Steps).Count();
        items.Add(("Plugin", $"{parsed.AssemblyName} ({parsed.Types.Count} type(s), {stepCount} step(s))", Path.GetRelativePath(pendingDir, f)));
    }

    var pendingRelationshipFiles = Directory.GetFiles(pendingDir, "*.relationship.json", SearchOption.AllDirectories)
        .ToList();

    foreach (var f in pendingRelationshipFiles)
    {
        var parsed = JsonSerializer.Deserialize<RelationshipUpdateDefinition>(File.ReadAllText(f),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        var changes = new List<string>();
        if (parsed.DeleteBehavior != null) changes.Add($"Delete={parsed.DeleteBehavior}");
        if (parsed.AssignBehavior != null) changes.Add($"Assign={parsed.AssignBehavior}");
        items.Add(("Relationship", $"{parsed.SchemaName} ({string.Join(", ", changes)})", Path.GetRelativePath(pendingDir, f)));
    }

    var pendingImportFiles = Directory.GetFiles(pendingDir, "*.import.json", SearchOption.AllDirectories)
        .Where(f => f.Contains("Import", StringComparison.OrdinalIgnoreCase))
        .ToList();

    foreach (var f in pendingImportFiles)
    {
        var parsed = JsonSerializer.Deserialize<DataImportDefinition>(File.ReadAllText(f),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true })!;
        items.Add(("Import", $"{parsed.Table} ({parsed.Rows.Count} rows, match: {string.Join("+", parsed.MatchOn)})", Path.GetRelativePath(pendingDir, f)));
    }

    var pendingPcfFiles = Directory.GetFiles(pendingDir, "*.pcf.json", SearchOption.AllDirectories)
        .ToList();

    foreach (var f in pendingPcfFiles)
    {
        var parsed = JsonSerializer.Deserialize<PcfControlDefinition>(File.ReadAllText(f),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        items.Add(("PCF Control", parsed.Name, Path.GetRelativePath(pendingDir, f)));
    }

    var pendingSecurityRoleFiles = Directory.GetFiles(pendingDir, "*.securityrole.json", SearchOption.AllDirectories)
        .ToList();

    foreach (var f in pendingSecurityRoleFiles)
    {
        var parsed = JsonSerializer.Deserialize<SecurityRoleUpdateDefinition>(File.ReadAllText(f),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true })!;
        items.Add(("Security Role", $"{parsed.RoleName} ({parsed.Privileges.Count} privileges)", Path.GetRelativePath(pendingDir, f)));
    }

    if (items.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No pending changes.[/]");
        return;
    }

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("[bold]Type[/]")
        .AddColumn("[bold]Description[/]")
        .AddColumn("[grey]File[/]");

    foreach (var (type, label, file) in items)
        table.AddRow(Markup.Escape(type), Markup.Escape(label), $"[grey]{Markup.Escape(file)}[/]");

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule($"[bold blue]Pending Changes ({items.Count})[/]").LeftJustified());
    AnsiConsole.WriteLine();
    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();
}

static async Task HandleCommitCommand(IConfiguration configuration, bool noCache, bool debug)
{
    var metadataPath = FindConnectionMetadata();
    var metadata = ReadConnectionMetadata(metadataPath);
    var baseDir = GetBaseDir(metadataPath);
    var pendingDir = Path.Combine(baseDir, "SolutionExport", "_pending");

    StreamWriter? debugLog = null;
    if (debug)
    {
        var logsDir = Path.Combine(baseDir, ".metadatasync", "logs");
        Directory.CreateDirectory(logsDir);
        var logPath = Path.Combine(logsDir, $"commit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
        debugLog = new StreamWriter(logPath, append: false) { AutoFlush = true };
        debugLog.WriteLine($"[{DateTime.UtcNow:O}] MetadataSync commit --debug");
        debugLog.WriteLine($"  baseDir: {baseDir}");
        debugLog.WriteLine($"  pendingDir: {pendingDir}");
        debugLog.WriteLine($"  environment: {metadata.Environment.Url}");
        debugLog.WriteLine($"  solution: {metadata.Solution.UniqueName}");
        AnsiConsole.MarkupLine($"[grey]Debug log: {logPath}[/]");
    }
    void Log(string message) { debugLog?.WriteLine($"[{DateTime.UtcNow:O}] {message}"); }

    try
    {
    // Discover pending items using CommitPipeline
    var commitItems = CommitPipeline.DiscoverPendingItems(pendingDir);
    if (commitItems.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No pending changes found.[/]");
        return;
    }

    // Present selection
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold blue]Pending Changes[/]").LeftJustified());
    AnsiConsole.WriteLine();

    if (!AnsiConsole.Profile.Capabilities.Interactive)
    {
        AnsiConsole.MarkupLine("[yellow]Non-interactive terminal detected.[/]");
        AnsiConsole.MarkupLine("[yellow]The commit command requires human approval to select which changes to push.[/]");
        AnsiConsole.MarkupLine("[yellow]Please ask the user to run the commit command manually in a terminal:[/]");
        AnsiConsole.MarkupLine("[blue]  dotnet run --project src/XrmEmulator.MetadataSync -- commit[/]");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[grey]Pending items:[/]");
        foreach (var item in commitItems)
        {
            AnsiConsole.MarkupLine($"  [grey]• {Markup.Escape(item.DisplayName)}[/]");
            if (item.Type == CommitItemType.SecurityRoleUpdate && item.ParsedData is SecurityRoleUpdateDefinition roleDef)
            {
                foreach (var priv in roleDef.Privileges)
                    AnsiConsole.MarkupLine($"    [grey]  {priv.Access} on {priv.Entity} ({priv.Depth})[/]");
            }
        }
        Environment.Exit(1);
    }

    var prompt = new MultiSelectionPrompt<CommitItem>()
        .Title("Select changes to push to CRM:")
        .PageSize(20)
        .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
        .UseConverter(c => c.DisplayName);

    // Add items — for security roles, show privilege details in the converter
    foreach (var item in commitItems)
        prompt.AddChoice(item);

    // Show expanded details below the selection for security roles and option sets
    foreach (var item in commitItems.Where(i => i.Type == CommitItemType.OptionSetValue))
    {
        if (item.ParsedData is OptionSetValueDefinition osDef && osDef.Values.Count > 0)
        {
            AnsiConsole.MarkupLine($"  [blue]{Markup.Escape(item.DisplayName)}[/]");
            foreach (var val in osDef.Values)
                AnsiConsole.MarkupLine($"    [grey]• \"{val.Label}\" = {(val.Value.HasValue ? val.Value.Value.ToString() : "auto")}[/]");
            AnsiConsole.WriteLine();
        }
    }
    foreach (var item in commitItems.Where(i => i.Type == CommitItemType.SecurityRoleUpdate))
    {
        if (item.ParsedData is SecurityRoleUpdateDefinition roleDef && roleDef.Privileges.Count > 0)
        {
            AnsiConsole.MarkupLine($"  [blue]{Markup.Escape(item.DisplayName)}[/]");
            foreach (var priv in roleDef.Privileges)
                AnsiConsole.MarkupLine($"    [grey]• {priv.Access} on {priv.Entity} ({priv.Depth})[/]");
            AnsiConsole.WriteLine();
        }
    }

    var selected = AnsiConsole.Prompt(prompt);

    if (selected.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No items selected. Commit cancelled.[/]");
        return;
    }

    // Confirm — show details for each selected item
    AnsiConsole.WriteLine();
    var table = new Table().Border(TableBorder.Rounded)
        .AddColumn("Type")
        .AddColumn("Details");
    foreach (var item in selected)
    {
        var details = FormatCommitItemDetails(item);
        table.AddRow(item.Type.ToString(), details);
    }
    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();

    if (!AnsiConsole.Confirm($"Push {selected.Count} change(s) to CRM?"))
    {
        AnsiConsole.MarkupLine("[yellow]Commit cancelled.[/]");
        return;
    }

    // Connect using cached tokens
    Log("Connecting to CRM...");
    var connectionSettings = await ReconnectFromMetadata(metadata, configuration, noCache);
    using var client = await ConnectionFactory.CreateAsync(connectionSettings);
    Log("Connected successfully.");

    // Execute commit via pipeline
    CommitResult result = null!;
    AnsiConsole.Progress()
        .AutoClear(false)
        .HideCompleted(false)
        .Columns(
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new SpinnerColumn())
        .Start(ctx =>
        {
            var commitTask = ctx.AddTask("[green]Committing changes[/]", maxValue: 1);
            Spectre.Console.ProgressTask? ribbonTask = null;
            Spectre.Console.ProgressTask? publishTask = null;
            Spectre.Console.ProgressTask? exportTask = null;

            result = CommitPipeline.ExecuteCommit(client, metadata, baseDir, selected, Log,
                onPhaseChanged: phase =>
                {
                    if (phase.Contains("Importing ribbon"))
                    {
                        ribbonTask ??= ctx.AddTask("[green]Importing ribbon changes[/]", maxValue: 1);
                    }
                    else if (phase.Contains("Publishing"))
                    {
                        commitTask.Increment(1);
                        ribbonTask?.Increment(1);
                        publishTask = ctx.AddTask("[green]Publishing customizations[/]", maxValue: 1);
                    }
                    else if (phase.Contains("Re-exporting"))
                    {
                        publishTask?.Increment(1);
                        exportTask = ctx.AddTask("[green]Re-exporting solution[/]", maxValue: 1);
                    }
                },
                confirm: message => AnsiConsole.Confirm($"[yellow]{message}[/]"));

            commitTask.Increment(1);
            ribbonTask?.Increment(1);
            publishTask?.Increment(1);
            exportTask?.Increment(1);
        });

    // Report results
    if (result.FailedItem != null)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[red]Failed:[/] {Markup.Escape(result.FailedItem.DisplayName)}");
        var errorMessage = ExtractErrorDetail(result.FailedException!);
        foreach (var line in errorMessage.Split('\n'))
            AnsiConsole.MarkupLine($"[red]  {Markup.Escape(line)}[/]");
        if (result.Committed.Count > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{result.Committed.Count} item(s) committed successfully before the error.[/]");
            AnsiConsole.MarkupLine("[yellow]Re-run commit to retry the failed item and remaining items.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No items were committed. Fix the issue and re-run commit.[/]");
            return;
        }
    }

    // Verification
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold blue]Verification[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var verifyExportDir = Path.Combine(baseDir, "SolutionExport");
    var verifySolutionFolder = GetSolutionFolder(verifyExportDir);
    var committedDir = Path.Combine(baseDir, "SolutionExport", "_committed");

    foreach (var item in result.Committed)
    {
        var relativePath = Path.GetRelativePath(pendingDir, item.FilePath).Replace('\\', '/');
        var committedPath = Path.Combine(committedDir, relativePath);
        var snapshotPath = Path.Combine(verifySolutionFolder, relativePath);

        var isNewView = item.Type == CommitItemType.SavedQuery
            && ((SavedQueryDefinition)item.ParsedData).SavedQueryId == Guid.Empty;
        var isNewForm = item.Type == CommitItemType.SystemForm
            && ((SystemFormDefinition)item.ParsedData).FormId == Guid.Empty;
        var isNewBusinessRule = item.Type == CommitItemType.BusinessRule
            && ((BusinessRuleDefinition)item.ParsedData).WorkflowId == Guid.Empty;
        if (item.Type == CommitItemType.IconUpload || item.Type == CommitItemType.IconSet
            || item.Type == CommitItemType.AppModuleEntity || item.Type == CommitItemType.AppModuleView
            || item.Type == CommitItemType.AppModuleForm || item.Type == CommitItemType.BusinessRule
            || item.Type == CommitItemType.Delete
            || item.Type == CommitItemType.WebResourceUpload
            || item.Type == CommitItemType.CommandBar
            || item.Type == CommitItemType.Deprecate
            || item.Type == CommitItemType.NewAttribute
            || item.Type == CommitItemType.RibbonWorkbench
            || item.Type == CommitItemType.RelationshipUpdate
            || item.Type == CommitItemType.SecurityRoleUpdate
            || item.Type == CommitItemType.DataImport
            || isNewView || isNewForm || isNewBusinessRule)
        {
            AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(item.DisplayName)} \u2014 pushed & archived");
            continue;
        }

        // Plugin: verify assembly + steps exist in re-exported solution
        if (item.Type == CommitItemType.PluginRegistration)
        {
            var pluginDef = (PluginRegistrationDefinition)item.ParsedData;
            var pluginAssemblyDirs = Directory.Exists(Path.Combine(verifySolutionFolder, "PluginAssemblies"))
                ? Directory.GetDirectories(Path.Combine(verifySolutionFolder, "PluginAssemblies"))
                : Array.Empty<string>();
            var assemblyNameNoDots = pluginDef.AssemblyName.Replace(".", "");
            var assemblyFound = pluginAssemblyDirs.Any(d =>
            {
                var folderName = Path.GetFileName(d);
                return folderName.StartsWith(pluginDef.AssemblyName, StringComparison.OrdinalIgnoreCase)
                    || folderName.StartsWith(assemblyNameNoDots, StringComparison.OrdinalIgnoreCase);
            });

            var stepsDir = Path.Combine(verifySolutionFolder, "SdkMessageProcessingSteps");
            var expectedStepCount = pluginDef.Types.SelectMany(t => t.Steps).Count();
            var actualStepCount = 0;
            if (Directory.Exists(stepsDir))
            {
                foreach (var stepFile in Directory.GetFiles(stepsDir, "*.xml"))
                {
                    var stepXml = File.ReadAllText(stepFile);
                    if (stepXml.Contains(pluginDef.AssemblyName, StringComparison.OrdinalIgnoreCase)
                        || stepXml.Contains(assemblyNameNoDots, StringComparison.OrdinalIgnoreCase))
                        actualStepCount++;
                }
            }

            if (assemblyFound && actualStepCount >= expectedStepCount)
            {
                AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(item.DisplayName)} \u2014 verified (assembly + {actualStepCount} step(s) in re-export)");
            }
            else if (assemblyFound)
            {
                AnsiConsole.MarkupLine($"[yellow]\u26a0[/] {Markup.Escape(item.DisplayName)} \u2014 assembly found but only {actualStepCount}/{expectedStepCount} step(s) in re-export");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]\u2717[/] {Markup.Escape(item.DisplayName)} \u2014 assembly NOT found in re-exported solution");
            }
            continue;
        }

        if (File.Exists(committedPath) && File.Exists(snapshotPath))
        {
            var committedContent = File.ReadAllText(committedPath).Trim();
            var snapshotContent = File.ReadAllText(snapshotPath).Trim();

            if (committedContent == snapshotContent)
            {
                AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(item.DisplayName)} \u2014 verified (snapshot matches)");
            }
            else if (item.Type == CommitItemType.SavedQuery)
            {
                var committedParsed = SavedQueryFileReader.Parse(committedPath);
                var snapshotParsed = SavedQueryFileReader.Parse(snapshotPath);

                if (committedParsed.FetchXml?.Trim() == snapshotParsed.FetchXml?.Trim()
                    && committedParsed.LayoutXml?.Trim() == snapshotParsed.LayoutXml?.Trim()
                    && committedParsed.Name == snapshotParsed.Name)
                {
                    AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(item.DisplayName)} \u2014 verified (cosmetic XML differences ignored)");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]\u26a0[/] {Markup.Escape(item.DisplayName)} \u2014 content mismatch after round-trip (already archived)");
                }
            }
            else if (item.Type == CommitItemType.SystemForm)
            {
                var committedParsed = SystemFormFileReader.Parse(committedPath);
                var snapshotParsed = SystemFormFileReader.Parse(snapshotPath);

                if (committedParsed.FormXml.Trim() == snapshotParsed.FormXml.Trim()
                    && committedParsed.Name == snapshotParsed.Name)
                {
                    AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(item.DisplayName)} \u2014 verified (cosmetic XML differences ignored)");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]\u26a0[/] {Markup.Escape(item.DisplayName)} \u2014 content mismatch after round-trip (already archived)");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(item.DisplayName)} \u2014 pushed & archived");
            }
        }
        else if (!File.Exists(snapshotPath))
        {
            AnsiConsole.MarkupLine($"[yellow]\u26a0[/] {Markup.Escape(item.DisplayName)} \u2014 not found in re-exported snapshot (already archived)");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(item.DisplayName)} \u2014 pushed & archived");
        }
    }

    AnsiConsole.WriteLine();
    if (result.FailedItem != null)
    {
        AnsiConsole.MarkupLine($"[yellow]Commit partially complete.[/] {result.Committed.Count} succeeded, remaining in _pending/.");
    }
    else
    {
        Log("Commit complete.");
        AnsiConsole.MarkupLine("[green]Commit complete.[/]");
    }
    }
    catch (Exception ex) when (debugLog != null)
    {
        debugLog.WriteLine($"[{DateTime.UtcNow:O}] EXCEPTION: {ex}");
        throw;
    }
    finally
    {
        debugLog?.Dispose();
    }
}

// ──────────────────────────────────────────────────────────────
// default — full interactive sync (existing behavior)
// ──────────────────────────────────────────────────────────────
static async Task HandleSyncCommand(IConfiguration configuration, bool noCache)
{
    // Check if we're inside an already-synced folder — offer quick re-export
    string? existingMetadataPath = null;
    try { existingMetadataPath = FindConnectionMetadata(); } catch { /* not found, full wizard */ }

    if (existingMetadataPath != null)
    {
        var existingMetadata = ReadConnectionMetadata(existingMetadataPath);
        var existingBaseDir = GetBaseDir(existingMetadataPath);

        AnsiConsole.MarkupLine($"[blue]Existing sync detected:[/] {existingMetadata.Solution.UniqueName} @ {existingMetadata.Environment.Url}");
        AnsiConsole.MarkupLine($"[grey]Last synced: {existingMetadata.SyncedAt:u}[/]");
        AnsiConsole.MarkupLine($"[grey]Output: {existingBaseDir}[/]");
        AnsiConsole.WriteLine();

        if (AnsiConsole.Confirm($"Re-export [green]{existingMetadata.Solution.UniqueName}[/]?"))
        {
            var connectionSettings = await ReconnectFromMetadata(existingMetadata, configuration, noCache);
            using var client = await ConnectionFactory.CreateAsync(connectionSettings);

            // Discover entity names from existing Model/entities/ folder
            var entitiesDir = Path.Combine(existingBaseDir, "Model", "entities");
            var entityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(entitiesDir))
            {
                foreach (var file in Directory.GetFiles(entitiesDir, "*.md"))
                    entityNames.Add(Path.GetFileNameWithoutExtension(file));
            }

            // Re-export = full sync with everything enabled, using saved settings
            var reExportOptions = new SyncOptions
            {
                SolutionId = existingMetadata.Solution.Id,
                SolutionUniqueName = existingMetadata.Solution.UniqueName,
                SelectedEntities = entityNames,
                OutputDirectory = existingBaseDir,
                IncludePlugins = true,
                IncludeWorkflows = true,
                IncludeSecurityRoles = true,
                IncludeOptionSets = true,
                IncludeOrganizationData = true,
                IncludeSolutionExport = true,
                IncludeRibbonExport = true
            };

            ExecuteSync(client, reExportOptions);
            WriteConnectionMetadata(connectionSettings, existingMetadata.Solution.Id, existingMetadata.Solution.UniqueName, existingBaseDir);
            PrintSyncSummary(reExportOptions);
            return;
        }

        AnsiConsole.MarkupLine("[grey]Starting full sync wizard...[/]");
        AnsiConsole.WriteLine();
    }

    // Full sync wizard
    // 2. Run ConnectionWizard to get connection settings
    var connectionSettingsFull = await ConnectionWizard.RunAsync(configuration, noCache);

    // 3. Create ServiceClient via ConnectionFactory
    using var clientFull = await ConnectionFactory.CreateAsync(connectionSettingsFull);

    // 4. Run SolutionPicker to select solution
    var (solutionId, solutionUniqueName) = SolutionPicker.Run(clientFull);

    // 5. Run EntityPicker for entity selection
    var selectedEntities = EntityPicker.Run(clientFull, solutionId);

    // 6. Run MetadataScopePicker for scope + output directory
    var syncOptions = MetadataScopePicker.Run(solutionId, solutionUniqueName, selectedEntities);

    // 7. Confirm
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold blue]Sync Summary[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("Setting")
        .AddColumn("Value");

    table.AddRow("Entities", $"{syncOptions.SelectedEntities.Count} selected");
    table.AddRow("Plugins", syncOptions.IncludePlugins ? "Yes" : "No");
    table.AddRow("Workflows", syncOptions.IncludeWorkflows ? "Yes" : "No");
    table.AddRow("Security Roles", syncOptions.IncludeSecurityRoles ? "Yes" : "No");
    table.AddRow("Global Option Sets", syncOptions.IncludeOptionSets ? "Yes" : "No");
    table.AddRow("Currencies & Organization", syncOptions.IncludeOrganizationData ? "Yes" : "No");
    table.AddRow("Solution Export & Unpack", syncOptions.IncludeSolutionExport ? "Yes" : "No");
    table.AddRow("Output Directory", syncOptions.OutputDirectory);

    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();

    if (!AnsiConsole.Confirm("Proceed with metadata sync?"))
    {
        AnsiConsole.MarkupLine("[yellow]Sync cancelled.[/]");
        return;
    }

    ExecuteSync(clientFull, syncOptions);
    WriteConnectionMetadata(connectionSettingsFull, solutionId, solutionUniqueName, syncOptions.OutputDirectory);
    PrintSyncSummary(syncOptions, offerGitInit: true);
}

// ──────────────────────────────────────────────────────────────
// ExecuteSync — shared execution logic for full sync and re-export
// ──────────────────────────────────────────────────────────────
static void ExecuteSync(IOrganizationService client, SyncOptions syncOptions)
{
    Dictionary<string, EntityMetadata>? entityMetadata = null;
    Dictionary<string, Dictionary<int, int>>? defaultStateStatus = null;
    List<MetaPlugin>? plugins = null;
    OptionSetMetadataBase[]? optionSets = null;
    Entity? organization = null;
    Entity? rootBusinessUnit = null;
    List<Entity>? currencies = null;
    List<Entity>? workflows = null;
    List<SecurityRole>? securityRoles = null;

    AnsiConsole.WriteLine();

    AnsiConsole.Progress()
        .AutoClear(false)
        .HideCompleted(false)
        .Columns(
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new SpinnerColumn())
        .Start(ctx =>
        {
            // Entity Metadata (always required)
            var entityTask = ctx.AddTask("[green]Entity Metadata[/]", maxValue: 100);
            entityMetadata = EntityMetadataReader.Read(client, syncOptions.SelectedEntities);
            defaultStateStatus = EntityMetadataReader.BuildDefaultStateStatus(entityMetadata);
            entityTask.Value = 100;

            // Plugins
            if (syncOptions.IncludePlugins)
            {
                var pluginTask = ctx.AddTask("[green]Plugins[/]", maxValue: 100);
                plugins = PluginReader.Read(client, syncOptions.SelectedEntities);
                pluginTask.Value = 100;
            }

            // Workflows
            if (syncOptions.IncludeWorkflows)
            {
                var workflowTask = ctx.AddTask("[green]Workflows[/]", maxValue: 100);
                workflows = WorkflowReader.Read(client, syncOptions.SelectedEntities);
                workflowTask.Value = 100;
            }

            // Security Roles
            if (syncOptions.IncludeSecurityRoles)
            {
                var roleTask = ctx.AddTask("[green]Security Roles[/]", maxValue: 100);
                securityRoles = SecurityRoleReader.Read(client);
                roleTask.Value = 100;
            }

            // Global Option Sets
            if (syncOptions.IncludeOptionSets)
            {
                var optionSetTask = ctx.AddTask("[green]Global Option Sets[/]", maxValue: 100);
                optionSets = OptionSetReader.Read(client);
                optionSetTask.Value = 100;
            }

            // Currencies & Organization
            if (syncOptions.IncludeOrganizationData)
            {
                var orgTask = ctx.AddTask("[green]Currencies & Organization[/]", maxValue: 100);
                var orgData = OrganizationReader.Read(client);
                organization = orgData.Organization;
                rootBusinessUnit = orgData.RootBusinessUnit;
                currencies = orgData.Currencies;
                orgTask.Value = 100;
            }

            // Serialization
            var serializeTask = ctx.AddTask("[green]Serializing output[/]", maxValue: 100);
            MetadataSerializer.Serialize(
                syncOptions,
                entityMetadata,
                defaultStateStatus,
                plugins,
                optionSets,
                organization,
                rootBusinessUnit,
                currencies,
                workflows,
                securityRoles);
            serializeTask.Value = 100;

            // Solution Export & Unpack
            if (syncOptions.IncludeSolutionExport)
            {
                var exportTask = ctx.AddTask("[green]Solution Export & Unpack[/]", maxValue: 100);
                SolutionExporter.Export(client, syncOptions.SolutionUniqueName, syncOptions.OutputDirectory);
                exportTask.Value = 100;
            }

            // Ribbon Export — retrieve full merged ribbon XML for each entity
            if (syncOptions.IncludeRibbonExport && syncOptions.IncludeSolutionExport)
            {
                var ribbonTask = ctx.AddTask("[green]Ribbon Export[/]", maxValue: 100);
                var solutionExportDir = Path.Combine(syncOptions.OutputDirectory, "SolutionExport");
                RibbonExporter.Export(client, solutionExportDir, syncOptions.SolutionUniqueName, syncOptions.OutputDirectory);
                ribbonTask.Value = 100;
            }
        });
}

static void PrintSyncSummary(SyncOptions syncOptions, bool offerGitInit = false)
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold green]Sync Complete[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var outputPath = Path.GetFullPath(syncOptions.OutputDirectory);
    AnsiConsole.MarkupLine($"[green]Metadata written to:[/] {outputPath}");

    // Count what was synced from the output files
    var entitiesDir = Path.Combine(syncOptions.OutputDirectory, "Model", "entities");
    if (Directory.Exists(entitiesDir))
        AnsiConsole.MarkupLine($"  Entities: {Directory.GetFiles(entitiesDir, "*.md").Length}");

    if (syncOptions.IncludeSolutionExport)
    {
        AnsiConsole.MarkupLine($"  Solution unpacked to: {Path.GetFullPath(Path.Combine(syncOptions.OutputDirectory, "SolutionExport", syncOptions.SolutionUniqueName))}");

        var ribbonDir = Path.Combine(syncOptions.OutputDirectory, "Ribbon");
        if (syncOptions.IncludeRibbonExport && Directory.Exists(ribbonDir))
        {
            var ribbonCount = Directory.GetFiles(ribbonDir, "*.xml").Length;
            if (ribbonCount > 0)
                AnsiConsole.MarkupLine($"  Ribbon exports: {ribbonCount} entities → Ribbon/");
        }

        var solutionExportDir = Path.Combine(syncOptions.OutputDirectory, "SolutionExport");

        // If git is already enabled, commit the new snapshot
        if (GitHelper.IsGitRepo(solutionExportDir))
        {
            try
            {
                var committed = GitHelper.CommitAll(solutionExportDir, $"Sync: {syncOptions.SolutionUniqueName}");
                if (committed)
                    AnsiConsole.MarkupLine("[grey]Git: committed sync snapshot in SolutionExport/[/]");
            }
            catch (Exception gitEx)
            {
                AnsiConsole.MarkupLine($"[yellow]Git warning:[/] {Markup.Escape(gitEx.Message)}");
            }
        }
        // Otherwise, offer to enable git tracking (only on first full sync)
        else if (offerGitInit && GitHelper.IsGitAvailable())
        {
            AnsiConsole.WriteLine();
            if (AnsiConsole.Confirm("Enable git tracking for SolutionExport?", defaultValue: false))
            {
                try
                {
                    GitHelper.Init(solutionExportDir);
                    AnsiConsole.MarkupLine("[green]Git tracking initialized in SolutionExport/[/]");
                }
                catch (Exception gitEx)
                {
                    AnsiConsole.MarkupLine($"[yellow]Git init warning:[/] {Markup.Escape(gitEx.Message)}");
                }
            }
        }
    }
}

// ──────────────────────────────────────────────────────────────
// Helpers
// ──────────────────────────────────────────────────────────────
static void WriteConnectionMetadata(
    ConnectionSettings connectionSettings,
    Guid solutionId,
    string solutionUniqueName,
    string outputDirectory)
{
    // Retrieve the friendly name from SolutionPicker's output (we use uniqueName as fallback)
    var metadata = new ConnectionMetadata
    {
        Environment = new EnvironmentMetadata { Url = connectionSettings.Url },
        Solution = new SolutionMetadata
        {
            Id = solutionId,
            UniqueName = solutionUniqueName,
            FriendlyName = solutionUniqueName // Best available; SolutionPicker doesn't return friendly name
        },
        AuthMode = connectionSettings.AuthMode.ToString(),
        ClientId = connectionSettings.ClientId,
        SyncedAt = DateTimeOffset.UtcNow
    };

    var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    var stateDir = Path.Combine(outputDirectory, ".metadatasync");
    Directory.CreateDirectory(stateDir);
    var path = Path.Combine(stateDir, "connection_metadata.json");
    File.WriteAllText(path, json);

    // Auto-migrate: delete legacy file if present
    var legacyPath = Path.Combine(outputDirectory, "connection_metadata.json");
    if (File.Exists(legacyPath))
    {
        File.Delete(legacyPath);
        AnsiConsole.MarkupLine($"[grey]Migrated connection_metadata.json to .metadatasync/[/]");
    }

    AnsiConsole.MarkupLine($"[grey]Connection metadata written to: {path}[/]");
}

static string FindConnectionMetadata()
{
    // Search upward from current directory for connection_metadata.json
    // Check .metadatasync/ first (new location), then legacy bare path
    var dir = Directory.GetCurrentDirectory();
    while (dir != null)
    {
        // New location: .metadatasync/connection_metadata.json
        var newCandidate = Path.Combine(dir, ".metadatasync", "connection_metadata.json");
        if (File.Exists(newCandidate))
            return newCandidate;

        // Legacy location: connection_metadata.json
        var candidate = Path.Combine(dir, "connection_metadata.json");
        if (File.Exists(candidate))
            return candidate;

        // Also search in subdirectories one level deep
        foreach (var subDir in Directory.GetDirectories(dir))
        {
            newCandidate = Path.Combine(subDir, ".metadatasync", "connection_metadata.json");
            if (File.Exists(newCandidate))
                return newCandidate;

            candidate = Path.Combine(subDir, "connection_metadata.json");
            if (File.Exists(candidate))
                return candidate;
        }

        dir = Path.GetDirectoryName(dir);
    }

    throw new InvalidOperationException(
        "connection_metadata.json not found. Run MetadataSync (full sync) first to create it.");
}

static ConnectionMetadata ReadConnectionMetadata(string path)
{
    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<ConnectionMetadata>(json, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }) ?? throw new InvalidOperationException($"Failed to deserialize {path}");
}

static async Task<ConnectionSettings> ReconnectFromMetadata(
    ConnectionMetadata metadata,
    IConfiguration configuration,
    bool noCache)
{
    if (Enum.TryParse<AuthMode>(metadata.AuthMode, ignoreCase: true, out var authMode))
    {
        return new ConnectionSettings
        {
            Url = metadata.Environment.Url,
            AuthMode = authMode,
            ClientId = metadata.ClientId,
            NoCache = noCache
        };
    }

    // Fallback to interactive wizard
    AnsiConsole.MarkupLine("[yellow]Could not determine auth mode from metadata. Running connection wizard...[/]");
    return await ConnectionWizard.RunAsync(configuration, noCache);
}

static string GetSolutionFolder(string solutionExportDir)
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
/// Format detailed description of a commit item for the confirmation table.
/// Shows expanded info for items that benefit from detail (e.g., security role privileges).
/// </summary>
static string FormatCommitItemDetails(CommitItem item)
{
    if (item.Type == CommitItemType.SecurityRoleUpdate && item.ParsedData is SecurityRoleUpdateDefinition roleDef)
    {
        var lines = new List<string> { Markup.Escape(item.DisplayName) };
        foreach (var priv in roleDef.Privileges)
            lines.Add($"  [grey]• {Markup.Escape(priv.Access)} on {Markup.Escape(priv.Entity)} ({Markup.Escape(priv.Depth)})[/]");
        return string.Join("\n", lines);
    }

    if (item.Type == CommitItemType.OptionSetValue && item.ParsedData is OptionSetValueDefinition osDef)
    {
        var lines = new List<string> { Markup.Escape(item.DisplayName) };
        foreach (var val in osDef.Values)
            lines.Add($"  [grey]• \"{Markup.Escape(val.Label)}\" = {(val.Value.HasValue ? val.Value.Value.ToString() : "auto")}[/]");
        return string.Join("\n", lines);
    }

    if (item.Type == CommitItemType.NewEntity && item.ParsedData is NewEntityDefinition entityDef)
    {
        var lines = new List<string> { Markup.Escape(item.DisplayName) };
        if (entityDef.Attributes is { Count: > 0 })
            foreach (var attr in entityDef.Attributes)
                lines.Add($"  [grey]• {Markup.Escape(attr.AttributeSchemaName)} ({Markup.Escape(attr.AttributeType)}): \"{Markup.Escape(attr.DisplayName)}\"[/]");
        return string.Join("\n", lines);
    }

    if (item.Type == CommitItemType.NewAttribute && item.ParsedData is NewAttributeDefinition attrDef)
    {
        return $"{Markup.Escape(item.DisplayName)}\n  [grey]• Type: {Markup.Escape(attrDef.AttributeType)}, Entity: {Markup.Escape(attrDef.EntityLogicalName)}[/]";
    }

    return Markup.Escape(item.DisplayName);
}

static string GetBaseDir(string metadataPath)
{
    var parent = Path.GetDirectoryName(metadataPath)!;
    return Path.GetFileName(parent) == ".metadatasync"
        ? Path.GetDirectoryName(parent)!
        : parent;
}

// ──────────────────────────────────────────────────────────────
// git-init — initialize git tracking in SolutionExport/
// ──────────────────────────────────────────────────────────────
static void HandleGitInitCommand()
{
    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var solutionExportDir = Path.Combine(baseDir, "SolutionExport");

    if (!GitHelper.IsGitAvailable())
    {
        AnsiConsole.MarkupLine("[red]git is not available on PATH.[/]");
        Environment.Exit(1);
    }

    if (!Directory.Exists(solutionExportDir))
    {
        AnsiConsole.MarkupLine("[red]SolutionExport/ directory not found.[/] Run a full sync first.");
        Environment.Exit(1);
    }

    if (GitHelper.IsGitRepo(solutionExportDir))
    {
        if (GitHelper.HasCommits(solutionExportDir))
        {
            AnsiConsole.MarkupLine("[yellow]SolutionExport/ is already a git repository.[/]");
            return;
        }

        // Partially initialized (git init succeeded but commit failed) — retry the commit
        AnsiConsole.MarkupLine("[yellow]SolutionExport/ has .git/ but no commits — retrying initial commit...[/]");
        GitHelper.CompleteInit(solutionExportDir);
        AnsiConsole.MarkupLine("[green]Git tracking initialized in SolutionExport/[/]");
        AnsiConsole.MarkupLine("[grey]Future commits and re-exports will be tracked automatically.[/]");
        return;
    }

    GitHelper.Init(solutionExportDir);
    AnsiConsole.MarkupLine("[green]Git tracking initialized in SolutionExport/[/]");
    AnsiConsole.MarkupLine("[grey]Future commits and re-exports will be tracked automatically.[/]");
}

// ──────────────────────────────────────────────────────────────
// hook guard-readonly — block writes to SolutionExport/
// ──────────────────────────────────────────────────────────────
static async Task HandleHookGuardReadonly()
{
    var json = await Console.In.ReadToEndAsync();
    using var doc = JsonDocument.Parse(json);
    var filePath = doc.RootElement.GetProperty("tool_input").GetProperty("file_path").GetString() ?? "";

    if (filePath.Contains("SolutionExport/")
        && !filePath.Contains("_pending/"))
    {
        await Console.Error.WriteLineAsync(
            "BLOCKED: SolutionExport/ is read-only. Use MetadataSync checkout commands (views/sitemap/entity) to check out files to _pending/.");
        Environment.Exit(2);
    }
}

// ──────────────────────────────────────────────────────────────
// hook guard-pending — block direct writes to _pending/
// ──────────────────────────────────────────────────────────────
static async Task HandleHookGuardPending()
{
    var json = await Console.In.ReadToEndAsync();
    using var doc = JsonDocument.Parse(json);
    var filePath = doc.RootElement.GetProperty("tool_input").GetProperty("file_path").GetString() ?? "";

    if (filePath.Contains("/_pending/") || filePath.EndsWith("_pending/"))
    {
        await Console.Error.WriteLineAsync(
            "BLOCKED: Cannot create files in _pending/. Use MetadataSync commands (views/sitemap/entity) to check out files.");
        Environment.Exit(2);
    }
}

// ──────────────────────────────────────────────────────────────
// agent init — publish hooks binary + write .claude/settings.json
// ──────────────────────────────────────────────────────────────
static void HandleAgentInit()
{
    // Find git root by walking up from cwd
    var dir = Directory.GetCurrentDirectory();
    string? gitRoot = null;
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir, ".git")))
        {
            gitRoot = dir;
            break;
        }
        dir = Path.GetDirectoryName(dir);
    }

    if (gitRoot == null)
    {
        Console.Error.WriteLine("Could not find git root (no .git/ directory found).");
        Environment.Exit(1);
    }

    // Detect local dev: look for the MetadataSync csproj in this repo
    var csprojPath = Directory.GetFiles(gitRoot, "XrmEmulator.MetadataSync.csproj", SearchOption.AllDirectories)
        .FirstOrDefault();
    var isLocalDev = csprojPath != null;

    string commandPrefix;

    if (isLocalDev)
    {
        // Publish to a local bin/hooks directory next to the csproj
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var publishDir = Path.Combine(projectDir, "bin", "hooks");

        Console.WriteLine($"Publishing MetadataSync to {publishDir} ...");
        var publish = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{csprojPath}\" -o \"{publishDir}\" --nologo -v quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        publish.WaitForExit();

        if (publish.ExitCode != 0)
        {
            var stderr = publish.StandardError.ReadToEnd();
            Console.Error.WriteLine($"dotnet publish failed (exit {publish.ExitCode}):\n{stderr}");
            Environment.Exit(1);
        }

        // Published exe uses the assembly name, not the ToolCommandName
        var assemblyName = Path.GetFileNameWithoutExtension(csprojPath);
        var exeName = OperatingSystem.IsWindows() ? $"{assemblyName}.exe" : assemblyName;
        var exePath = Path.Combine(publishDir, exeName);

        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"Published executable not found at {exePath}");
            Environment.Exit(1);
        }

        commandPrefix = exePath;
        Console.WriteLine($"Published to {exePath}");
    }
    else
    {
        // Distributed mode: run directly from NuGet
        commandPrefix = "dotnet dnx --yes XrmEmulator.MetadataSync --";
        Console.WriteLine("Using distributed mode (dotnet dnx)");
    }

    var claudeDir = Path.Combine(gitRoot, ".claude");
    Directory.CreateDirectory(claudeDir);
    var settingsPath = Path.Combine(claudeDir, "settings.json");

    // Build the hooks config
    var hooksJson = $$"""
    {
      "hooks": {
        "PreToolUse": [
          {
            "matcher": "Write|Edit",
            "hooks": [{ "type": "command", "command": "{{commandPrefix}} hook guard-readonly" }]
          },
          {
            "matcher": "Write",
            "hooks": [{ "type": "command", "command": "{{commandPrefix}} hook guard-pending" }]
          }
        ]
      }
    }
    """;

    using var hooksDoc = JsonDocument.Parse(hooksJson);

    // Merge with existing settings — preserve all keys except hooks
    var merged = new Dictionary<string, JsonElement>();

    if (File.Exists(settingsPath))
    {
        var existing = File.ReadAllText(settingsPath);
        using var existingDoc = JsonDocument.Parse(existing);
        foreach (var prop in existingDoc.RootElement.EnumerateObject())
        {
            if (prop.Name != "hooks")
                merged[prop.Name] = prop.Value.Clone();
        }
    }

    merged["hooks"] = hooksDoc.RootElement.GetProperty("hooks").Clone();

    var options = new JsonSerializerOptions { WriteIndented = true };
    var output = JsonSerializer.Serialize(merged, options);
    File.WriteAllText(settingsPath, output);

    Console.WriteLine();
    Console.WriteLine($"Hooks configured in {settingsPath}");
    Console.WriteLine($"  PreToolUse (Write|Edit): hook guard-readonly — blocks edits to SolutionExport/");
    Console.WriteLine($"  PreToolUse (Write):      hook guard-pending  — blocks direct file creation in _pending/");
}

// ──────────────────────────────────────────────────────────────
// mcp init — configure Graph auth + devtunnel + .mcp.json
// ──────────────────────────────────────────────────────────────
static async Task HandleMcpInit()
{
    // Find git root by walking up from cwd
    var dir = Directory.GetCurrentDirectory();
    string? gitRoot = null;
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir, ".git")))
        {
            gitRoot = dir;
            break;
        }
        dir = Path.GetDirectoryName(dir);
    }

    if (gitRoot == null)
    {
        Console.Error.WriteLine("Could not find git root (no .git/ directory found).");
        Environment.Exit(1);
    }

    // Find base dir (where .metadatasync/ lives)
    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var metadata = ReadConnectionMetadata(metadataPath);

    Console.WriteLine("MetadataSync MCP Init");
    Console.WriteLine($"  Environment: {metadata.Environment.Url}");
    Console.WriteLine($"  Solution: {metadata.Solution.UniqueName}");
    Console.WriteLine();

    // Prompt for Graph app registration details
    Console.Write("Graph App Client ID: ");
    var clientId = Console.ReadLine()?.Trim()
        ?? throw new InvalidOperationException("Client ID is required.");

    Console.Write("Graph Tenant ID: ");
    var tenantId = Console.ReadLine()?.Trim()
        ?? throw new InvalidOperationException("Tenant ID is required.");

    Console.Write("Approver email (who receives approval cards): ");
    var approverEmail = Console.ReadLine()?.Trim()
        ?? throw new InvalidOperationException("Approver email is required.");

    // Generate HMAC signing key
    var hmacSigningKey = HmacHelper.GenerateSigningKey();

    // Interactive OAuth2 auth code flow (public client)
    Console.WriteLine();
    Console.WriteLine("Starting OAuth2 authentication...");
    var (_, refreshToken) = await GraphAuthHelper.AcquireTokensInteractiveAsync(clientId, tenantId);
    Console.WriteLine("Authentication successful!");

    // Create devtunnel (optional — skip if devtunnel CLI not available)
    string? devtunnelId = null;
    if (DevtunnelManager.IsLoggedIn())
    {
        Console.WriteLine();
        Console.WriteLine("Creating devtunnel...");
        devtunnelId = DevtunnelManager.CreateTunnel();
        DevtunnelManager.AddPort(devtunnelId, 0); // Port will be assigned at serve time
        Console.WriteLine($"  Tunnel ID: {devtunnelId}");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("devtunnel CLI not available or not logged in. Skipping tunnel setup.");
        Console.WriteLine("  Run 'devtunnel user login' to enable tunnel support.");
    }

    // Save config
    var config = new McpConfig
    {
        GraphClientId = clientId,
        GraphTenantId = tenantId,
        ApproverEmail = approverEmail,
        HmacSigningKey = hmacSigningKey,
        RefreshToken = refreshToken,
        DevtunnelId = devtunnelId
    };

    var configPath = McpConfig.GetConfigPath(baseDir);
    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
    var configJson = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(configPath, configJson);
    Console.WriteLine($"  Config saved to {configPath}");

    // Ensure .gitignore has mcp-config.json
    var gitignorePath = Path.Combine(baseDir, ".metadatasync", ".gitignore");
    if (!File.Exists(gitignorePath) || !File.ReadAllText(gitignorePath).Contains("mcp-config.json"))
    {
        File.AppendAllText(gitignorePath, "\nmcp-config.json\napprovals/\n");
    }

    // Detect local dev (same pattern as HandleAgentInit)
    var csprojPath = Directory.GetFiles(gitRoot, "XrmEmulator.MetadataSync.csproj", SearchOption.AllDirectories)
        .FirstOrDefault();
    var isLocalDev = csprojPath != null;

    string commandPrefix;
    if (isLocalDev)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var publishDir = Path.Combine(projectDir, "bin", "hooks");

        Console.WriteLine($"Publishing MetadataSync to {publishDir} ...");
        var publish = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{csprojPath}\" -o \"{publishDir}\" --nologo -v quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        publish.WaitForExit();

        if (publish.ExitCode != 0)
        {
            var stderr = publish.StandardError.ReadToEnd();
            Console.Error.WriteLine($"dotnet publish failed (exit {publish.ExitCode}):\n{stderr}");
            Environment.Exit(1);
        }

        var assemblyName = Path.GetFileNameWithoutExtension(csprojPath);
        var exeName = OperatingSystem.IsWindows() ? $"{assemblyName}.exe" : assemblyName;
        var exePath = Path.Combine(publishDir, exeName);
        commandPrefix = exePath;
    }
    else
    {
        commandPrefix = "dotnet dnx --yes XrmEmulator.MetadataSync --";
    }

    // Update .mcp.json at git root
    var mcpJsonPath = Path.Combine(gitRoot, ".mcp.json");
    var mcpDoc = new Dictionary<string, object>();
    if (File.Exists(mcpJsonPath))
    {
        var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
            File.ReadAllText(mcpJsonPath));
        if (existing != null)
        {
            foreach (var kvp in existing)
                mcpDoc[kvp.Key] = kvp.Value;
        }
    }

    // Build mcpServers entry
    if (!mcpDoc.ContainsKey("mcpServers"))
        mcpDoc["mcpServers"] = new Dictionary<string, object>();

    var servers = mcpDoc["mcpServers"];
    if (servers is System.Text.Json.JsonElement je)
    {
        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText())!;
        dict["metadatasync"] = new
        {
            command = commandPrefix.Contains(' ') ? commandPrefix.Split(' ')[0] : commandPrefix,
            args = commandPrefix.Contains(' ')
                ? commandPrefix.Split(' ').Skip(1).Append("mcp").Append("serve").ToArray()
                : new[] { "mcp", "serve" }
        };
        mcpDoc["mcpServers"] = dict;
    }

    var mcpJson = System.Text.Json.JsonSerializer.Serialize(mcpDoc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(mcpJsonPath, mcpJson);

    Console.WriteLine();
    Console.WriteLine("MCP Init complete!");
    Console.WriteLine($"  .mcp.json updated: {mcpJsonPath}");
    Console.WriteLine($"  Graph client: {clientId}");
    Console.WriteLine($"  Approver: {approverEmail}");
    if (devtunnelId != null)
        Console.WriteLine($"  Devtunnel: {devtunnelId}");
    Console.WriteLine();
    Console.WriteLine("Run 'mcp serve' to start the MCP server.");
}

// ──────────────────────────────────────────────────────────────
// mcp serve — long-running MCP server with approval flow
// ──────────────────────────────────────────────────────────────
static async Task HandleMcpServe()
{
    var metadataPath = FindConnectionMetadata();
    var baseDir = GetBaseDir(metadataPath);
    var configPath = McpConfig.GetConfigPath(baseDir);

    if (!File.Exists(configPath))
    {
        Console.Error.WriteLine($"MCP config not found at {configPath}. Run 'mcp init' first.");
        Environment.Exit(1);
    }

    var configJson = File.ReadAllText(configPath);
    var config = System.Text.Json.JsonSerializer.Deserialize<McpConfig>(configJson)
        ?? throw new InvalidOperationException("Failed to deserialize MCP config");

    var server = new McpServer(config, baseDir);
    using var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await server.RunAsync(cts.Token);
}

static string ExtractErrorDetail(Exception ex)
{
    // Dataverse SOAP fault (typed SDK messages)
    if (ex is FaultException<OrganizationServiceFault> fault)
    {
        var msg = fault.Detail.Message;
        if (fault.Detail.InnerFault != null)
            msg += $"\n  Inner fault: {fault.Detail.InnerFault.Message}";
        return msg;
    }

    // InvalidOperationException (our own wrappers, e.g. Delete handler)
    if (ex is InvalidOperationException)
        return ex.Message;

    // For Dataverse WebAPI errors (HttpOperationException etc.),
    // try to extract the response body via reflection since
    // Microsoft.Rest types aren't directly referenced.
    var detail = TryExtractHttpResponseContent(ex)
        ?? TryExtractHttpResponseContent(ex.InnerException);
    if (detail != null)
        return $"{ex.Message}\n  Server response: {detail}";

    // Fallback: unwrap to innermost
    var inner = ex;
    while (inner.InnerException != null)
        inner = inner.InnerException;
    return inner == ex ? ex.Message : $"{ex.Message}\n  Detail: {inner.Message}";
}

static string? TryExtractHttpResponseContent(Exception? ex)
{
    if (ex == null) return null;
    // HttpOperationException has a Response property with a Content string
    var responseProp = ex.GetType().GetProperty("Response");
    if (responseProp == null) return null;
    var response = responseProp.GetValue(ex);
    if (response == null) return null;
    var contentProp = response.GetType().GetProperty("Content");
    var content = contentProp?.GetValue(response) as string;
    return string.IsNullOrWhiteSpace(content) ? null : content;
}

// Needed for user secrets configuration builder
public partial class Program { }
