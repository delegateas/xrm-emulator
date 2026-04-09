namespace XrmEmulator.MetadataSync.Models;

public record PluginRegistrationDefinition
{
    public required string AssemblyName { get; init; }
    public required string AssemblyPath { get; init; }
    public int IsolationMode { get; init; } = 2;        // 2 = Sandbox
    public int SourceType { get; init; } = 0;            // 0 = Database
    public required string SolutionUniqueName { get; init; }
    public required List<PluginTypeRegistration> Types { get; init; }
}

public record PluginTypeRegistration
{
    public required string TypeName { get; init; }       // Full type name, e.g. "Namespace.ClassName"
    public required string FriendlyName { get; init; }
    public required List<PluginStepRegistration> Steps { get; init; }
}

public record PluginStepRegistration
{
    public required string MessageName { get; init; }    // "Create", "Update", "Delete"
    public required string PrimaryEntity { get; init; }  // Entity logical name
    public int Stage { get; init; } = 40;                // 10=PreValidation, 20=PreOperation, 40=PostOperation
    public int Mode { get; init; } = 0;                  // 0=Synchronous, 1=Asynchronous
    public int Rank { get; init; } = 1;
    public string? FilteringAttributes { get; init; }    // Comma-separated attribute names
    public bool AsyncAutoDelete { get; init; } = false;
    public List<PluginImageRegistration>? Images { get; init; }
}

public record PluginImageRegistration
{
    public required string Name { get; init; }
    public required string EntityAlias { get; init; }
    public int ImageType { get; init; }                  // 0=PreImage, 1=PostImage, 2=Both
    public string? Attributes { get; init; }             // Comma-separated, null = all attributes
}
