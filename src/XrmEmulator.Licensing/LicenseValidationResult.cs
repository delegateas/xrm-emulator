namespace XrmEmulator.Licensing;

public sealed record LicenseValidationResult
{
    public required bool IsValid { get; init; }
    public License? License { get; init; }
    public string? Error { get; init; }

    public static LicenseValidationResult Success(License license) => new()
    {
        IsValid = true,
        License = license
    };

    public static LicenseValidationResult Failure(string error) => new()
    {
        IsValid = false,
        Error = error
    };
}
