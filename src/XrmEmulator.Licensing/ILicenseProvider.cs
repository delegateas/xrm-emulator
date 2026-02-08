namespace XrmEmulator.Licensing;

public interface ILicenseProvider
{
    License? CurrentLicense { get; }
    bool IsFeatureLicensed(string feature);
    LicenseValidationResult ValidationResult { get; }
}
