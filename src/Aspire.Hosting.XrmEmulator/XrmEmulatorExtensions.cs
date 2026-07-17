using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for configuring XRM Emulator resources
/// </summary>
public static class XrmEmulatorExtensions
{
    private const string ContainerImage = "ghcr.io/delegateas/xrm-emulator";
    private const string DefaultTag = "latest";

    /// <summary>
    /// Adds an XRM Emulator container resource from the GitHub Container Registry.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="tag">The container image tag. Defaults to "latest".</param>
    /// <returns>A resource builder for the XRM Emulator container.</returns>
    public static IResourceBuilder<ContainerResource> AddXrmEmulatorContainer(
        this IDistributedApplicationBuilder builder,
        string name,
        string tag = DefaultTag)
    {
        return builder.AddContainer(name, ContainerImage, tag)
            .WithHttpEndpoint(targetPort: 8080)
            .WithHttpHealthCheck("/health");
    }

    /// <summary>
    /// Mounts a local metadata directory into the XRM Emulator container.
    /// The metadata folder contains XrmMockup entity metadata files.
    /// </summary>
    /// <param name="builder">The resource builder for the XRM Emulator container.</param>
    /// <param name="metadataPath">The local path to the metadata directory.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<ContainerResource> WithMetadataFolder(
        this IResourceBuilder<ContainerResource> builder,
        string metadataPath)
    {
        return builder.WithBindMount(metadataPath, "/app/Metadata", isReadOnly: true);
    }

    /// <summary>
    /// Adds snapshot persistence to the XRM Emulator container.
    /// This allows the emulator to save and restore its database state across restarts.
    /// </summary>
    /// <param name="builder">The resource builder for the XRM Emulator container.</param>
    /// <param name="saveIntervalSeconds">Interval in seconds between snapshot saves. Defaults to 10 seconds.</param>
    /// <param name="dataPath">Optional host path for snapshot data. If not specified, uses a named volume.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<ContainerResource> WithSnapshotPersistence(
        this IResourceBuilder<ContainerResource> builder,
        int saveIntervalSeconds = 10,
        string? dataPath = null)
    {
        builder.WithEnvironment("Snapshot__Enabled", "true");
        builder.WithEnvironment("Snapshot__SaveIntervalSeconds", saveIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.WithEnvironment("Snapshot__SaveOnShutdown", "true");
        builder.WithEnvironment("Snapshot__RestoreOnStartup", "true");

        if (!string.IsNullOrEmpty(dataPath))
        {
            builder.WithBindMount(dataPath, "/data");
        }
        else
        {
            builder.WithVolume("xrm-emulator-data", "/data");
        }

        return builder;
    }

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
    /// Adds snapshot persistence to the XRM Emulator project resource.
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
        builder.WithEnvironment("Snapshot__Enabled", "true");

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
    /// Points the plugin/custom-API execution-history logs at a data directory (alongside the
    /// snapshot) instead of the emulator's working directory. Keeps runtime logs — which contain
    /// real record ids — out of the source tree.
    /// </summary>
    /// <param name="builder">The resource builder for the XRM Emulator project.</param>
    /// <param name="dataDirectory">Directory where the execution-history .jsonl files are written.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<ProjectResource> WithExecutionHistoryPersistence(
        this IResourceBuilder<ProjectResource> builder,
        string dataDirectory)
    {
        builder.WithEnvironment("PluginExecutionHistory__FilePath",
            Path.Combine(dataDirectory, "xrm-emulator-plugin-executions.jsonl"));
        builder.WithEnvironment("CustomApiExecutionHistory__FilePath",
            Path.Combine(dataDirectory, "xrm-emulator-customapi-executions.jsonl"));

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

    /// <summary>
    /// Configures a license key for the XRM Emulator.
    /// Licensed features (snapshots, plugins, multi-org) require a valid license key.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="licenseKey">The license key string.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<T> WithLicenseKey<T>(
        this IResourceBuilder<T> builder,
        string licenseKey) where T : IResourceWithEnvironment
    {
        return builder.WithEnvironment("XRMEMULATOR_LICENSE", licenseKey);
    }

    /// <summary>
    /// Configures a license key for the XRM Emulator by reading it from a file.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="licenseFilePath">Path to a .lic file containing the license key.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<T> WithLicenseFile<T>(
        this IResourceBuilder<T> builder,
        string licenseFilePath) where T : IResourceWithEnvironment
    {
        var key = File.ReadAllText(licenseFilePath).Trim();
        return builder.WithEnvironment("XRMEMULATOR_LICENSE", key);
    }

    /// <summary>
    /// Mounts a local solution exports directory into the XRM Emulator container.
    /// The solution exports folder contains Dataverse solution packages with AppModules, SiteMaps, Entities, Views, and Forms.
    /// </summary>
    /// <param name="builder">The resource builder for the XRM Emulator container.</param>
    /// <param name="solutionExportsPath">The local path to the solution exports directory.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<ContainerResource> WithSolutionExports(
        this IResourceBuilder<ContainerResource> builder,
        string solutionExportsPath)
    {
        return builder.WithBindMount(solutionExportsPath, "/app/SolutionExports", isReadOnly: true);
    }

    /// <summary>
    /// Configures solution exports path for the XRM Emulator project resource.
    /// The solution exports folder contains Dataverse solution packages with AppModules, SiteMaps, Entities, Views, and Forms.
    /// </summary>
    /// <param name="builder">The resource builder for the XRM Emulator project.</param>
    /// <param name="solutionExportsPath">The local path to the solution exports directory.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<ProjectResource> WithSolutionExports(
        this IResourceBuilder<ProjectResource> builder,
        string solutionExportsPath)
    {
        return builder.WithEnvironment("SolutionExports__Path", solutionExportsPath);
    }

    /// <summary>
    /// Pre-configures sample request-parameter values for a Custom API on the emulator's
    /// <c>/customapis/{uniqueName}/trigger</c> dev-tool page, so manually triggering it there —
    /// as a stand-in for the scheduled Cloud Flow that calls it in production — starts from
    /// realistic data instead of a blank form.
    /// </summary>
    /// <param name="builder">The resource builder for the XRM Emulator project.</param>
    /// <param name="customApiUniqueName">The Custom API's unique name (e.g. "kf_QualifyLead").</param>
    /// <param name="exampleParameters">
    /// An object whose properties match the Custom API's request parameter unique names
    /// (e.g. <c>new { LeadId = "...", Status = 1 }</c>). Serialized to JSON and read back by the
    /// emulator to pre-fill the trigger form. Calling this again for the same Custom API replaces
    /// the previous example.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<ProjectResource> WithCustomApiExample(
        this IResourceBuilder<ProjectResource> builder,
        string customApiUniqueName,
        object exampleParameters)
    {
        return builder.WithEnvironment(
            $"CustomApiExamples__{customApiUniqueName}__Parameters",
            JsonSerializer.Serialize(exampleParameters));
    }

    /// <summary>
    /// Registers a seed action that runs once the XRM Emulator is healthy.
    /// The action receives an <see cref="IOrganizationService"/> connected to the emulator
    /// and should use it to create or upsert records. A snapshot is saved after seeding completes.
    /// </summary>
    /// <param name="builder">The resource builder for the XRM Emulator project.</param>
    /// <param name="seedAction">
    /// Delegate that receives an <see cref="IOrganizationService"/> and seeds data.
    /// Use <see cref="Microsoft.Xrm.Sdk.Messages.UpsertRequest"/> for idempotent seeds.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<ProjectResource> WithSeedData(
        this IResourceBuilder<ProjectResource> builder,
        Func<IOrganizationService, Task> seedAction)
    {
        builder.Resource.Annotations.Add(new XrmEmulatorSeedDataAnnotation(seedAction));

        // Register the lifecycle hook only once per AppHost
#pragma warning disable CS0618 // IDistributedApplicationLifecycleHook is obsolete but still functional
        if (!builder.ApplicationBuilder.Services.Any(s => s.ImplementationType == typeof(XrmEmulatorSeedHook)))
        {
            builder.ApplicationBuilder.Services
                .AddSingleton<IDistributedApplicationLifecycleHook, XrmEmulatorSeedHook>();
        }
#pragma warning restore CS0618

        return builder;
    }
}
