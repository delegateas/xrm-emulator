namespace XrmEmulator.MetadataSync.Models;

public record SystemFormDefinition
{
    public Guid FormId { get; init; }              // Guid.Empty = new form
    public required string Name { get; init; }
    public required string FormXml { get; init; }  // The inner <form> element as string
    public string? ObjectTypeCode { get; init; }   // Entity logical name (for new forms)
    public int FormType { get; init; } = 2;        // 2 = main
    public required string SourceFilePath { get; init; }
}
