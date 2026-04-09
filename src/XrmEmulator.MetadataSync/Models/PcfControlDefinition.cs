namespace XrmEmulator.MetadataSync.Models;

public record PcfControlDefinition
{
    /// <summary>Full namespace.constructor name, e.g. "KF.Partner.EnreachQueueControl"</summary>
    public required string Name { get; init; }

    /// <summary>Path to the PCF project directory (containing .pcfproj, package.json)</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Publisher prefix for the control (e.g. "kf")</summary>
    public string PublisherPrefix { get; init; } = "kf";

    /// <summary>Solution to add the component to (unused currently — pac creates its own temp solution)</summary>
    public string? SolutionUniqueName { get; init; }
}
