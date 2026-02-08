using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace XrmEmulator.Licensing;

public class LicenseProvider : ILicenseProvider
{
    public License? CurrentLicense { get; }
    public LicenseValidationResult ValidationResult { get; }

    public LicenseProvider(IConfiguration configuration, ILogger<LicenseProvider> logger)
    {
        var licenseKey = ResolveLicenseKey(configuration);

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            ValidationResult = LicenseValidationResult.Failure("No license key found.");
            logger.LogInformation("No license key found. Running with core features only.");
            return;
        }

        ValidationResult = LicenseValidator.Validate(licenseKey);

        if (ValidationResult.IsValid)
        {
            CurrentLicense = ValidationResult.License;
            logger.LogInformation(
                "Licensed to {Subject} (features: {Features}, expires: {ExpiresAt})",
                CurrentLicense!.Subject,
                string.Join(", ", CurrentLicense.Features),
                CurrentLicense.ExpiresAt?.ToString("o") ?? "never");
        }
        else
        {
            logger.LogWarning("Invalid license: {Error}", ValidationResult.Error);
        }
    }

    public bool IsFeatureLicensed(string feature)
    {
        if (string.Equals(feature, LicenseFeatures.Core, StringComparison.OrdinalIgnoreCase))
            return true;

        return CurrentLicense?.HasFeature(feature) == true;
    }

    private static string? ResolveLicenseKey(IConfiguration configuration)
    {
        var key = Environment.GetEnvironmentVariable("XRMEMULATOR_LICENSE");
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        var filePath = Environment.GetEnvironmentVariable("XRMEMULATOR_LICENSE_FILE");
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            return File.ReadAllText(filePath).Trim();

        key = configuration["License:Key"];
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        const string defaultLicenseFile = "xrm-emulator.lic";
        if (File.Exists(defaultLicenseFile))
            return File.ReadAllText(defaultLicenseFile).Trim();

        return null;
    }
}
