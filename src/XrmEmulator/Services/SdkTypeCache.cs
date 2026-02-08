using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Metadata;

namespace XrmEmulator.DataverseFakeApi.Services;

/// <summary>
/// Provides cached access to SDK types for efficient runtime type resolution.
/// Scans SDK assemblies once at startup instead of on every deserialization.
/// </summary>
internal sealed class SdkTypeCache
{
    private static readonly Lazy<SdkTypeCache> _instance = new(() => new SdkTypeCache());

    public static SdkTypeCache Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, Type> _enumTypes;
    private readonly ConcurrentDictionary<string, Type> _requestTypes;
    private readonly ConcurrentDictionary<string, Type> _complexTypes;

    private SdkTypeCache()
    {
        _enumTypes = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        _requestTypes = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        _complexTypes = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        InitializeCache();
    }

    private void InitializeCache()
    {
        // Scan SDK assemblies once at initialization
        var assemblies = new[]
        {
            typeof(Entity).Assembly,              // Microsoft.Xrm.Sdk
            typeof(WhoAmIRequest).Assembly,       // Microsoft.Crm.Sdk.Messages
            typeof(RetrieveRequest).Assembly,     // Microsoft.Xrm.Sdk.Messages (same as above usually)
            typeof(QueryExpression).Assembly,     // Microsoft.Xrm.Sdk.Query
            typeof(EntityMetadata).Assembly       // Microsoft.Xrm.Sdk.Metadata
        }.Distinct();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                // Cache enum types
                if (type.IsEnum)
                {
                    _enumTypes.TryAdd(type.Name, type);

                    // Also add with namespace prefix for more specific lookups
                    if (type.Namespace != null)
                    {
                        var shortNamespace = type.Namespace.Split('.').Last();
                        _enumTypes.TryAdd($"{shortNamespace}.{type.Name}", type);
                    }
                }

                // Cache request types (derived from OrganizationRequest)
                else if (typeof(OrganizationRequest).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    _requestTypes.TryAdd(type.Name, type);

                    // Add without "Request" suffix for convenience
                    if (type.Name.EndsWith("Request", StringComparison.Ordinal))
                    {
                        var shortName = type.Name.Substring(0, type.Name.Length - "Request".Length);
                        _requestTypes.TryAdd(shortName, type);
                    }
                }

                // Cache complex types (classes with DataContract attribute)
                else if (type.IsClass && !type.IsAbstract)
                {
                    var dataContractAttr = type.GetCustomAttribute<System.Runtime.Serialization.DataContractAttribute>();
                    if (dataContractAttr != null)
                    {
                        _complexTypes.TryAdd(type.Name, type);

                        // Use the DataContract name if different from type name
                        if (!string.IsNullOrEmpty(dataContractAttr.Name) &&
                            dataContractAttr.Name != type.Name)
                        {
                            _complexTypes.TryAdd(dataContractAttr.Name, type);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Tries to resolve an enum type from the SDK assemblies.
    /// </summary>
    public Type? TryGetEnumType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        // Extract type name from namespace-qualified names like "c:SomeEnumType"
        var cleanName = typeName.Contains(':', StringComparison.Ordinal)
            ? typeName.Split(':').Last()
            : typeName;

        // Try exact match first
        if (_enumTypes.TryGetValue(cleanName, out var exactType))
            return exactType;

        // Try fuzzy match by checking if any cached enum contains the search term
        foreach (var kvp in _enumTypes)
        {
            if (kvp.Key.Contains(cleanName, StringComparison.OrdinalIgnoreCase) ||
                cleanName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to resolve a request type from the SDK assemblies.
    /// </summary>
    public Type? TryGetRequestType(string requestName)
    {
        if (string.IsNullOrWhiteSpace(requestName))
            return null;

        return _requestTypes.TryGetValue(requestName, out var type) ? type : null;
    }

    /// <summary>
    /// Tries to resolve a complex type (Entity, EntityReference, etc.) from the SDK assemblies.
    /// </summary>
    public Type? TryGetComplexType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var cleanName = typeName.Contains(':', StringComparison.Ordinal)
            ? typeName.Split(':').Last()
            : typeName;

        return _complexTypes.TryGetValue(cleanName, out var type) ? type : null;
    }

    /// <summary>
    /// Gets all cached enum types.
    /// </summary>
    public IReadOnlyDictionary<string, Type> GetAllEnumTypes() => _enumTypes;

    /// <summary>
    /// Gets all cached request types.
    /// </summary>
    public IReadOnlyDictionary<string, Type> GetAllRequestTypes() => _requestTypes;

    /// <summary>
    /// Gets all cached complex types.
    /// </summary>
    public IReadOnlyDictionary<string, Type> GetAllComplexTypes() => _complexTypes;
}
