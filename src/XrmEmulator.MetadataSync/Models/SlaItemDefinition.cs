namespace XrmEmulator.MetadataSync.Models;

public record SlaItemDefinition
{
    public required string SlaId { get; init; }              // Parent SLA GUID
    public required string Name { get; init; }               // Item name
    public required string KpiId { get; init; }              // SLA KPI GUID (msdyn_slakpiid)
    public required int FailureAfter { get; init; }          // Minutes
    public required int WarnAfter { get; init; }             // Minutes
    public required string ApplicableWhenXml { get; init; }  // FetchXML
    public required string SuccessConditionsXml { get; init; }  // FetchXML
    public bool AllowPauseResume { get; init; } = true;
    public string? SolutionUniqueName { get; init; }
}
