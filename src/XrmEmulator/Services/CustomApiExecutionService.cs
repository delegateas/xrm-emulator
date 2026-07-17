using Microsoft.Xrm.Sdk;
using XrmEmulator.Models.CrmMetadata;

namespace XrmEmulator.Services;

public class CustomApiExecutionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string?> OutputParameters { get; set; } = [];
    public List<string> TraceLog { get; set; } = [];
}

/// <summary>
/// Directly instantiates and invokes a Custom API's backing plugin, bypassing XrmMockup's Core.Execute
/// pipeline entirely: Core can only derive an entityInfo/plugin match for CRUD-triggered messages, not
/// for a bare unbound OrganizationRequest, and none of KF's Custom API plugins implement the
/// ICustomApiConfig shape XrmMockupSettings.BaseCustomApiTypes expects (they're plain IPlugin). This is
/// the manual-trigger stand-in for the scheduled Cloud Flows that call these Custom APIs in production.
/// </summary>
public class CustomApiExecutionService
{
    private readonly OrganizationServiceResolver _serviceResolver;
    private readonly CustomApiExecutionHistoryStore _historyStore;
    private readonly ILogger<CustomApiExecutionService> _logger;

    public CustomApiExecutionService(
        OrganizationServiceResolver serviceResolver,
        CustomApiExecutionHistoryStore historyStore,
        ILogger<CustomApiExecutionService> logger)
    {
        _serviceResolver = serviceResolver;
        _historyStore = historyStore;
        _logger = logger;
    }

    public CustomApiExecutionResult Execute(CrmCustomApi api, IReadOnlyDictionary<string, string> rawFormValues)
    {
        var result = new CustomApiExecutionResult();
        var inputParametersForHistory = new Dictionary<string, string?>();

        if (string.IsNullOrEmpty(api.PluginTypeName))
        {
            result.ErrorMessage = $"Custom API '{api.UniqueName}' has no resolvable plugin type (plugintypeid not found in the exported plugin assembly).";
            _historyStore.Record(api.UniqueName, api.PluginTypeName ?? "", false, result.ErrorMessage, inputParametersForHistory, result.OutputParameters);
            return result;
        }

        var pluginType = PluginAssemblyBridge.TryGetPluginType(api.PluginTypeName);
        if (pluginType == null)
        {
            result.ErrorMessage = $"Plugin type '{api.PluginTypeName}' is not loaded (assembly not found, or the type was renamed since the solution was exported).";
            _historyStore.Record(api.UniqueName, api.PluginTypeName, false, result.ErrorMessage, inputParametersForHistory, result.OutputParameters);
            return result;
        }

        var context = new SimplePluginExecutionContext
        {
            MessageName = api.UniqueName,
        };

        foreach (var param in api.RequestParameters)
        {
            rawFormValues.TryGetValue(param.UniqueName, out var rawValue);
            if (string.IsNullOrEmpty(rawValue))
            {
                if (!param.IsOptional)
                {
                    result.ErrorMessage = $"Required parameter '{param.DisplayName}' ({param.UniqueName}) was not provided.";
                    _historyStore.Record(api.UniqueName, api.PluginTypeName, false, result.ErrorMessage, inputParametersForHistory, result.OutputParameters);
                    return result;
                }
                continue;
            }

            inputParametersForHistory[param.UniqueName] = rawValue;

            object convertedValue;
            try
            {
                convertedValue = ConvertParameterValue(param, rawValue);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Could not convert parameter '{param.DisplayName}' ({param.UniqueName}) value '{rawValue}' to {DescribeType(param.Type)}: {ex.Message}";
                _historyStore.Record(api.UniqueName, api.PluginTypeName, false, result.ErrorMessage, inputParametersForHistory, result.OutputParameters);
                return result;
            }

            context.InputParameters[param.UniqueName] = convertedValue;
        }

        var tracing = new SimpleTracingService(result.TraceLog);
        var serviceFactory = new SimpleOrganizationServiceFactory(_serviceResolver.Default);
        var provider = new SimpleServiceProvider(context, tracing, serviceFactory);

        try
        {
            var plugin = (IPlugin)Activator.CreateInstance(pluginType)!;
            plugin.Execute(provider);

            foreach (var key in context.OutputParameters.Keys)
                result.OutputParameters[key] = context.OutputParameters[key]?.ToString();

            result.Success = true;
        }
        catch (InvalidPluginExecutionException ex)
        {
            result.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Custom API '{UniqueName}' ({PluginType}) threw an unhandled exception", api.UniqueName, api.PluginTypeName);
            result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
        }

        _historyStore.Record(api.UniqueName, api.PluginTypeName, result.Success, result.ErrorMessage, inputParametersForHistory, result.OutputParameters);
        return result;
    }

    private static object ConvertParameterValue(CrmCustomApiParameter param, string rawValue) => param.Type switch
    {
        CrmCustomApiParameter.TypeBoolean => rawValue is "on" or "true" or "1",
        CrmCustomApiParameter.TypeInteger or CrmCustomApiParameter.TypePicklist => int.Parse(rawValue),
        CrmCustomApiParameter.TypeFloat => double.Parse(rawValue),
        CrmCustomApiParameter.TypeDecimal or CrmCustomApiParameter.TypeMoney => decimal.Parse(rawValue),
        CrmCustomApiParameter.TypeGuid => Guid.Parse(rawValue),
        CrmCustomApiParameter.TypeDateTime => DateTime.Parse(rawValue),
        CrmCustomApiParameter.TypeString or CrmCustomApiParameter.TypeStringArray => rawValue,
        CrmCustomApiParameter.TypeEntity or CrmCustomApiParameter.TypeEntityCollection or CrmCustomApiParameter.TypeEntityReference =>
            throw new NotSupportedException("Entity/EntityCollection/EntityReference parameters are not supported for manual triggering"),
        _ => rawValue,
    };

    private static string DescribeType(int type) => type switch
    {
        CrmCustomApiParameter.TypeBoolean => "Boolean",
        CrmCustomApiParameter.TypeDateTime => "DateTime",
        CrmCustomApiParameter.TypeDecimal => "Decimal",
        CrmCustomApiParameter.TypeFloat => "Float",
        CrmCustomApiParameter.TypeMoney => "Money",
        CrmCustomApiParameter.TypeGuid => "Guid",
        CrmCustomApiParameter.TypeInteger => "Integer",
        CrmCustomApiParameter.TypePicklist => "Picklist (Integer)",
        CrmCustomApiParameter.TypeString => "String",
        CrmCustomApiParameter.TypeStringArray => "StringArray",
        _ => "Entity reference type",
    };
}

file class SimpleTracingService(List<string> log) : ITracingService
{
    public void Trace(string format, params object[] args)
    {
        try { log.Add(string.Format(format, args)); }
        catch { log.Add(format); }
    }
}

file class SimpleOrganizationServiceFactory(IOrganizationService service) : IOrganizationServiceFactory
{
    public IOrganizationService CreateOrganizationService(Guid? userId) => service;
}

file class SimpleServiceProvider(
    IPluginExecutionContext context,
    ITracingService tracing,
    IOrganizationServiceFactory serviceFactory) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IPluginExecutionContext)) return context;
        if (serviceType == typeof(ITracingService)) return tracing;
        if (serviceType == typeof(IOrganizationServiceFactory)) return serviceFactory;
        return null;
    }
}

/// <summary>
/// Minimal hand-rolled <see cref="IPluginExecutionContext"/> — much simpler than XrmMockup365's
/// internal DG.Tools.XrmMockup.PluginContext, which is inaccessible outside that assembly and tightly
/// coupled to XrmMockup's own Core. Only the members KF's Custom API plugins actually read are given
/// meaningful values; the rest are safe/inert defaults.
/// </summary>
file class SimplePluginExecutionContext : IPluginExecutionContext
{
    public int Stage { get; set; } = 30;
    public IPluginExecutionContext? ParentContext { get; set; }
    public int Mode { get; set; } = 0;
    public int IsolationMode { get; set; } = 2;
    public int Depth { get; set; } = 1;
    public string MessageName { get; set; } = "";
    public string? PrimaryEntityName { get; set; } = "";
    public Guid? RequestId { get; set; }
    public string? SecondaryEntityName { get; set; }
    public ParameterCollection InputParameters { get; set; } = [];
    public ParameterCollection OutputParameters { get; set; } = [];
    public ParameterCollection SharedVariables { get; set; } = [];
    public Guid UserId { get; set; } = Guid.Empty;
    public Guid InitiatingUserId { get; set; } = Guid.Empty;
    public Guid BusinessUnitId { get; set; } = Guid.Empty;
    public Guid OrganizationId { get; set; } = Guid.Empty;
    public string OrganizationName { get; set; } = "XrmEmulator";
    public Guid PrimaryEntityId { get; set; } = Guid.Empty;
    public EntityImageCollection PreEntityImages { get; set; } = [];
    public EntityImageCollection PostEntityImages { get; set; } = [];
    public EntityReference? OwningExtension { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public bool IsExecutingOffline { get; set; }
    public bool IsOfflinePlayback { get; set; }
    public bool IsInTransaction { get; set; }
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public DateTime OperationCreatedOn { get; set; } = DateTime.UtcNow;
}
