using Microsoft.Extensions.DependencyInjection;

namespace XrmEmulator.Licensing;

public static class LicenseServiceExtensions
{
    public static IServiceCollection AddLicensing(this IServiceCollection services)
    {
        services.AddSingleton<ILicenseProvider, LicenseProvider>();
        return services;
    }
}
