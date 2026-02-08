using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spectre.Console;

namespace XrmEmulator.MetadataSync.Interactive;

public static class SolutionPicker
{
    public static Guid Run(IOrganizationService service)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold blue]Solution Selection[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var solutions = RetrieveSolutions(service);

        if (solutions.Count == 0)
        {
            throw new InvalidOperationException("No visible solutions found in the environment.");
        }

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<SolutionInfo>()
                .Title("Select a [green]solution[/] to sync metadata from:")
                .PageSize(20)
                .MoreChoicesText("[grey]Move up and down to reveal more solutions[/]")
                .AddChoices(solutions)
                .UseConverter(s => s.DisplayName));

        AnsiConsole.MarkupLine($"[grey]Selected: {selected.DisplayName}[/]");
        return selected.SolutionId;
    }

    private static List<SolutionInfo> RetrieveSolutions(IOrganizationService service)
    {
        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("friendlyname", "uniquename", "solutionid", "ismanaged"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("isvisible", ConditionOperator.Equal, true)
                }
            },
            Orders =
            {
                new OrderExpression("friendlyname", OrderType.Ascending)
            }
        };

        var results = service.RetrieveMultiple(query);

        return results.Entities
            .Select(e => new SolutionInfo
            {
                SolutionId = e.GetAttributeValue<Guid>("solutionid"),
                FriendlyName = e.GetAttributeValue<string>("friendlyname") ?? "Unknown",
                UniqueName = e.GetAttributeValue<string>("uniquename") ?? "Unknown",
                IsManaged = e.GetAttributeValue<bool>("ismanaged")
            })
            .ToList();
    }

    private sealed class SolutionInfo
    {
        public Guid SolutionId { get; init; }
        public required string FriendlyName { get; init; }
        public required string UniqueName { get; init; }
        public bool IsManaged { get; init; }

        public string DisplayName =>
            $"{FriendlyName} ({UniqueName}) [{(IsManaged ? "Managed" : "Unmanaged")}]";
    }
}
