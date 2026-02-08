using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for configuring XRM Emulator resources
/// </summary>
public static class XrmEmulatorExtensions
{
    /// <summary>
    /// Disables IPv6 for the resource to avoid slow localhost connections on Windows.
    /// </summary>
    /// <remarks>
    /// On Windows, connecting to "localhost" can take up to 2 minutes on first connection
    /// because the OS tries IPv6 (::1) first and waits for timeout before falling back to IPv4.
    /// This is a known .NET/Windows networking issue. Setting DOTNET_SYSTEM_NET_DISABLEIPV6=1
    /// forces the runtime to use IPv4 only, avoiding the delay.
    /// See: https://github.com/dotnet/runtime/issues/65375
    /// </remarks>
    /// <param name="builder">The resource builder.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<T> DisableIPv6<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment
    {
        return builder.WithEnvironment("DOTNET_SYSTEM_NET_DISABLEIPV6", "1");
    }

    /// <summary>
    /// Adds snapshot persistence to the XRM Emulator.
    /// This allows the emulator to save and restore its database state across restarts.
    /// </summary>
    /// <param name="builder">The resource builder for the XRM Emulator project.</param>
    /// <param name="saveIntervalSeconds">Interval in seconds between snapshot saves. Defaults to 10 seconds.</param>
    /// <param name="dataPath">Optional custom path where snapshot file will be stored. If not specified, uses "./xrm-emulator-snapshot.zip" in the bin folder.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<ProjectResource> WithSnapshotPersistence(
        this IResourceBuilder<ProjectResource> builder,
        int saveIntervalSeconds = 10,
        string? dataPath = null)
    {
        // Configure snapshot options via environment variables
        builder.WithEnvironment("Snapshot__Enabled", "true");

        // Only set custom path if provided
        if (!string.IsNullOrEmpty(dataPath))
        {
            builder.WithEnvironment("Snapshot__FilePath", dataPath);
        }

        builder.WithEnvironment("Snapshot__SaveIntervalSeconds", saveIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.WithEnvironment("Snapshot__SaveOnShutdown", "true");
        builder.WithEnvironment("Snapshot__RestoreOnStartup", "true");

        return builder;
    }

    /// <summary>
    /// Disables snapshot persistence for the XRM Emulator.
    /// Useful for test scenarios where you want a clean state on each run.
    /// </summary>
    /// <param name="builder">The resource builder for the XRM Emulator project</param>
    /// <returns>The resource builder for chaining</returns>
    public static IResourceBuilder<ProjectResource> WithoutSnapshotPersistence(
        this IResourceBuilder<ProjectResource> builder)
    {
        builder.WithEnvironment("Snapshot__Enabled", "false");
        return builder;
    }
}
