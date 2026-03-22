using Spectre.Console;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Interactive;

public static class MetadataScopePicker
{
    private const string EntityMetadataOption = "Entity Metadata (always required)";
    private const string PluginsOption = "Plugins";
    private const string WorkflowsOption = "Workflows";
    private const string SecurityRolesOption = "Security Roles";
    private const string GlobalOptionSetsOption = "Global Option Sets";
    private const string CurrenciesOrgOption = "Currencies & Organization";
    private const string SolutionExportOption = "Solution Export & Unpack";
    private const string RibbonExportOption = "Entity Ribbon XML";

    public static SyncOptions Run(Guid solutionId, string solutionUniqueName, HashSet<string> selectedEntities)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold blue]Metadata Scope[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var isDefaultSolution = solutionUniqueName.Equals("Default", StringComparison.OrdinalIgnoreCase);

        var choices = new List<string>
        {
            EntityMetadataOption,
            PluginsOption,
            WorkflowsOption,
            SecurityRolesOption,
            GlobalOptionSetsOption,
            CurrenciesOrgOption
        };

        if (!isDefaultSolution)
        {
            choices.Add(SolutionExportOption);
            choices.Add(RibbonExportOption);
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Solution Export is not available for the Default solution.[/]");
        }

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select [green]metadata scope[/] to sync:")
            .PageSize(10)
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
            .AddChoices(choices);

        foreach (var choice in choices)
            prompt.Select(choice);

        var selected = AnsiConsole.Prompt(prompt);
        var selectedSet = new HashSet<string>(selected);

        var outputDirectory = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter [green]output directory[/]:")
                .DefaultValue("./Metadata")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(outputDirectory))
            outputDirectory = "./Metadata";

        return new SyncOptions
        {
            SolutionId = solutionId,
            SolutionUniqueName = solutionUniqueName,
            SelectedEntities = selectedEntities,
            OutputDirectory = outputDirectory,
            IncludePlugins = selectedSet.Contains(PluginsOption),
            IncludeWorkflows = selectedSet.Contains(WorkflowsOption),
            IncludeSecurityRoles = selectedSet.Contains(SecurityRolesOption),
            IncludeOptionSets = selectedSet.Contains(GlobalOptionSetsOption),
            IncludeOrganizationData = selectedSet.Contains(CurrenciesOrgOption),
            IncludeSolutionExport = selectedSet.Contains(SolutionExportOption),
            IncludeRibbonExport = selectedSet.Contains(RibbonExportOption)
        };
    }
}
