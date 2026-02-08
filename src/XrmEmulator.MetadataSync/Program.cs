using DG.Tools.XrmMockup;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Spectre.Console;
using XrmEmulator.MetadataSync.Connection;
using XrmEmulator.MetadataSync.Interactive;
using XrmEmulator.MetadataSync.Models;
using XrmEmulator.MetadataSync.Readers;
using XrmEmulator.MetadataSync.Serialization;

AnsiConsole.Write(
    new FigletText("XRM Metadata Sync")
        .Color(Color.Blue));

AnsiConsole.MarkupLine("[grey]Sync Dataverse metadata into XrmMockup format for XrmEmulator[/]");
AnsiConsole.WriteLine();

try
{
    // 1. Parse configuration from user secrets + CLI args
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets<Program>(optional: true)
        .AddCommandLine(args)
        .Build();

    // 2. Run ConnectionWizard to get connection settings
    var connectionSettings = await ConnectionWizard.RunAsync(configuration);

    // 3. Create ServiceClient via ConnectionFactory
    using var client = await ConnectionFactory.CreateAsync(connectionSettings);

    // 4. Run SolutionPicker to select solution
    var solutionId = SolutionPicker.Run(client);

    // 5. Run EntityPicker for entity selection
    var selectedEntities = EntityPicker.Run(client, solutionId);

    // 6. Run MetadataScopePicker for scope + output directory
    var syncOptions = MetadataScopePicker.Run(solutionId, selectedEntities);

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
    table.AddRow("Output Directory", syncOptions.OutputDirectory);

    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();

    if (!AnsiConsole.Confirm("Proceed with metadata sync?"))
    {
        AnsiConsole.MarkupLine("[yellow]Sync cancelled.[/]");
        return;
    }

    // 8. Execute sync with progress bars
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
        });

    // 9. Summary
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold green]Sync Complete[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var outputPath = Path.GetFullPath(syncOptions.OutputDirectory);
    AnsiConsole.MarkupLine($"[green]Metadata written to:[/] {outputPath}");

    if (entityMetadata != null)
        AnsiConsole.MarkupLine($"  Entities: {entityMetadata.Count}");
    if (plugins != null)
        AnsiConsole.MarkupLine($"  Plugins: {plugins.Count}");
    if (workflows != null)
        AnsiConsole.MarkupLine($"  Workflows: {workflows.Count}");
    if (securityRoles != null)
        AnsiConsole.MarkupLine($"  Security Roles: {securityRoles.Count}");
    if (optionSets != null)
        AnsiConsole.MarkupLine($"  Global Option Sets: {optionSets.Length}");
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    Environment.Exit(1);
}

// Needed for user secrets configuration builder
public partial class Program { }
