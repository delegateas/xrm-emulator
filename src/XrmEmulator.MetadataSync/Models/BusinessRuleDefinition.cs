namespace XrmEmulator.MetadataSync.Models;

public record BusinessRuleDefinition
{
    public Guid WorkflowId { get; init; }           // Guid.Empty = new
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string PrimaryEntity { get; init; }
    public required string Xaml { get; init; }       // The .xaml file content
    public int Category { get; init; } = 2;          // 2 = Business Rule, 4 = Business Process Flow
    public int Scope { get; init; } = 4;             // 4 = Organization
    public Guid? ProcessTriggerFormId { get; init; }  // Scopes rule to a specific form
    public int? ProcessTriggerScope { get; init; }    // 1 = Form
    public bool ActivateOnCreate { get; init; }       // If true, activate after creation (needed for BPFs)
    public required string SourceFilePath { get; init; }
}
