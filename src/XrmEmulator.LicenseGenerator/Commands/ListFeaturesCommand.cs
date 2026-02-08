using Spectre.Console;
using Spectre.Console.Cli;

namespace XrmEmulator.LicenseGenerator.Commands;

public sealed class ListFeaturesCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold blue]Available Feature Keys[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Feature Key")
            .AddColumn("Description")
            .AddColumn("Free");

        table.AddRow("core", "Basic CRUD, OData, SOAP endpoints", "[green]Yes[/]");
        table.AddRow("snapshots", "Snapshot save/restore", "[red]No[/]");
        table.AddRow("multi-org", "Multiple organization instances", "[red]No[/]");
        table.AddRow("plugins", "Plugin execution support", "[red]No[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Use --features core,snapshots,plugins when creating a license.[/]");

        return 0;
    }
}
