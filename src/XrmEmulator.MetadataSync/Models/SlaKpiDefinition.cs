namespace XrmEmulator.MetadataSync.Models;

public record SlaKpiDefinition
{
    public required string Name { get; init; }                    // e.g. "LEAD-Møde Booket SLA"
    public required string EntityName { get; init; }              // e.g. "lead"
    public required string KpiField { get; init; }                // Lookup field on the entity → slakpiinstance
    public string ApplicableFromField { get; init; } = "createdon"; // Which datetime field starts the timer
    public string? SolutionUniqueName { get; init; }
}
