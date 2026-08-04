using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Xrm.Sdk;

namespace XrmEmulator.Services;

/// <summary>
/// Loads the KF CRM plugin assembly (built for net462 against Microsoft.CrmSdk.CoreAssemblies) into
/// this net10.0 host (which resolves Microsoft.Xrm.Sdk via Microsoft.PowerPlatform.Dataverse.Client)
/// and returns a representative <see cref="IPlugin"/> type suitable for
/// <c>XrmMockupSettings.BasePluginTypes</c>.
///
/// Both SDK packages ship an assembly literally named "Microsoft.Xrm.Sdk" / "Microsoft.Crm.Sdk.Proxy".
/// The plugin's own bin folder carries its own copies of those DLLs, but loading them separately
/// would give us a second, distinct `IPlugin` interface — `typeof(IPlugin).IsAssignableFrom(...)`
/// would then always be false. We redirect those specific assembly names to the copies already
/// loaded in this process so the interface/type identity matches on both sides.
/// </summary>
public static class PluginAssemblyBridge
{
    private static readonly string[] RedirectedAssemblyNames =
    [
        "Microsoft.Xrm.Sdk",
        "Microsoft.Crm.Sdk.Proxy",
        // The net462 plugin assembly resolves this against the .NET Framework in-box
        // System.Activities.dll; this host resolves System.Activities.CodeActivity from the
        // UiPath.Workflow.Runtime NuGet port (a different assembly identity). Redirecting
        // keeps CodeActivity-derived stub types identity-compatible with what WorkflowManager
        // checks against (`type.BaseType == codeActivityInstance.BaseType`).
        "System.Activities",
    ];

    private static Dictionary<string, Type> _pluginTypesByFullName = new();
    private static Assembly? _loadedAssembly;

    /// <summary>
    /// Resolves a plugin type by its full name (e.g. "KF.PartnerService.CRMPlugins.QualifyLeadCustomApiPlugin")
    /// from the assembly loaded by <see cref="TryLoadBasePluginTypes"/>, for direct invocation outside
    /// XrmMockup's CRUD-triggered plugin pipeline (Custom API manual triggering).
    /// </summary>
    public static Type? TryGetPluginType(string fullTypeName) =>
        _pluginTypesByFullName.GetValueOrDefault(fullTypeName);

    public static Type[] TryLoadBasePluginTypes(string? pluginAssemblyPath, Serilog.ILogger log)
    {
        if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
        {
            log.Information("PluginAssemblyBridge: no PluginAssembly:Path configured; plugins will not execute");
            return [];
        }

        if (!File.Exists(pluginAssemblyPath))
        {
            log.Warning("PluginAssemblyBridge: plugin assembly not found at {Path}; plugins will not execute", pluginAssemblyPath);
            return [];
        }

        AssemblyLoadContext.Default.Resolving += RedirectSdkAssemblies;

        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(pluginAssemblyPath));
            _loadedAssembly = assembly;
            var pluginTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && t.IsPublic && typeof(IPlugin).IsAssignableFrom(t))
                .ToArray();

            _pluginTypesByFullName = pluginTypes
                .Where(t => t.FullName != null)
                .ToDictionary(t => t.FullName!, t => t);

            if (pluginTypes.Length == 0)
            {
                log.Warning(
                    "PluginAssemblyBridge: loaded {Assembly} but found no types assignable to IPlugin " +
                    "(likely an assembly-identity mismatch between the host and plugin SDK references); plugins will not execute",
                    assembly.FullName);
                return [];
            }

            log.Information("PluginAssemblyBridge: loaded {Count} plugin type(s) from {Assembly}: {Types}",
                pluginTypes.Length, assembly.FullName, string.Join(", ", pluginTypes.Select(t => t.Name)));

            // RegisterDirectPlugins scans the whole owning assembly from a single representative
            // type, but only accepts types whose BaseType is object. Plugins built on a shared
            // abstract base are therefore invisible to it and would never fire. Those are picked up
            // by RegisterPlugins instead, which matches types whose BaseType is one of the entries
            // returned here — so hand it every abstract plugin base in the assembly as well.
            var pluginBaseTypes = pluginTypes
                .Select(t => t.BaseType)
                .Where(b => b != null && b != typeof(object) && b!.IsAbstract && typeof(IPlugin).IsAssignableFrom(b))
                .Distinct()
                .ToArray();

            if (pluginBaseTypes.Length > 0)
            {
                log.Information("PluginAssemblyBridge: registering {Count} plugin base type(s): {Types}",
                    pluginBaseTypes.Length, string.Join(", ", pluginBaseTypes.Select(t => t!.Name)));
            }

            return [pluginTypes[0], .. pluginBaseTypes!];
        }
        catch (ReflectionTypeLoadException ex)
        {
            log.Error(ex, "PluginAssemblyBridge: failed to reflect over types in {Path}: {LoaderErrors}",
                pluginAssemblyPath, string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message)));
            return [];
        }
        catch (Exception ex)
        {
            log.Error(ex, "PluginAssemblyBridge: failed to load plugin assembly from {Path}", pluginAssemblyPath);
            return [];
        }
    }

    /// <summary>
    /// Returns CodeActivity-derived types (e.g. local no-op stubs standing in for ISV workflow
    /// activities the emulator can't run) from the assembly loaded by
    /// <see cref="TryLoadBasePluginTypes"/>, suitable for <c>XrmMockupSettings.CodeActivityInstanceTypes</c>.
    /// Must be called after <see cref="TryLoadBasePluginTypes"/>; returns empty if that failed to load.
    /// </summary>
    public static Type[] TryLoadCodeActivityTypes(Serilog.ILogger log)
    {
        if (_loadedAssembly == null) return [];

        try
        {
            var codeActivityBaseType = typeof(System.Activities.CodeActivity);
            var codeActivityTypes = _loadedAssembly.GetTypes()
                .Where(t => !t.IsAbstract && t.IsPublic && t.BaseType == codeActivityBaseType)
                .ToArray();

            if (codeActivityTypes.Length > 0)
            {
                log.Information("PluginAssemblyBridge: loaded {Count} code activity type(s) from {Assembly}: {Types}",
                    codeActivityTypes.Length, _loadedAssembly.FullName, string.Join(", ", codeActivityTypes.Select(t => t.Name)));
            }

            return codeActivityTypes;
        }
        catch (ReflectionTypeLoadException ex)
        {
            log.Error(ex, "PluginAssemblyBridge: failed to reflect over code activity types: {LoaderErrors}",
                string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message)));
            return [];
        }
    }

    private static Assembly? RedirectSdkAssemblies(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name is null || !RedirectedAssemblyNames.Contains(name.Name))
            return null;

        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == name.Name);
    }
}
