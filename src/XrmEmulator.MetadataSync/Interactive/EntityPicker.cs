using Microsoft.Xrm.Sdk;
using Spectre.Console;
using XrmEmulator.MetadataSync.Readers;

namespace XrmEmulator.MetadataSync.Interactive;

public static class EntityPicker
{
    private static readonly HashSet<string> DefaultEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "systemuser",
        "team",
        "businessunit",
        "organization",
        "transactioncurrency",
        "role",
        "contact",
        "account"
    };

    public static HashSet<string> Run(IOrganizationService service, Guid solutionId)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold blue]Entity Selection[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[grey]Retrieving solution entities...[/]");
        var solutionEntities = SolutionComponentReader.GetEntityLogicalNames(service, solutionId);

        // Combine solution entities with defaults
        var allEntities = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in solutionEntities)
            allEntities.Add(entity);
        foreach (var entity in DefaultEntities)
            allEntities.Add(entity);

        if (allEntities.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No entities found. Using default XrmMockup entities only.[/]");
            return new HashSet<string>(DefaultEntities, StringComparer.OrdinalIgnoreCase);
        }

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select [green]entities[/] to include in metadata sync:")
            .PageSize(20)
            .MoreChoicesText("[grey]Move up and down to reveal more entities[/]")
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
            .AddChoiceGroup("Default (always included)", DefaultEntities.Order())
            .AddChoiceGroup("Solution Entities", solutionEntities.Except(DefaultEntities, StringComparer.OrdinalIgnoreCase).Order());

        foreach (var defaultEntity in DefaultEntities)
            prompt.Select(defaultEntity);

        var selected = AnsiConsole.Prompt(prompt);

        // Ensure defaults are always included
        var result = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        foreach (var entity in DefaultEntities)
            result.Add(entity);

        AnsiConsole.MarkupLine($"[grey]Selected {result.Count} entities.[/]");
        return result;
    }
}
