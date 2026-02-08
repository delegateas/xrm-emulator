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

    public static SyncOptions Run(Guid solutionId, HashSet<string> selectedEntities)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold blue]Metadata Scope[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var choices = new[]
        {
            EntityMetadataOption,
            PluginsOption,
            WorkflowsOption,
            SecurityRolesOption,
            GlobalOptionSetsOption,
            CurrenciesOrgOption
        };

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
            SelectedEntities = selectedEntities,
            OutputDirectory = outputDirectory,
            IncludePlugins = selectedSet.Contains(PluginsOption),
            IncludeWorkflows = selectedSet.Contains(WorkflowsOption),
            IncludeSecurityRoles = selectedSet.Contains(SecurityRolesOption),
            IncludeOptionSets = selectedSet.Contains(GlobalOptionSetsOption),
            IncludeOrganizationData = selectedSet.Contains(CurrenciesOrgOption)
        };
    }
}
