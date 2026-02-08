using System.Text.Json;
using NSec.Cryptography;

namespace XrmEmulator.Licensing;

public static class LicenseValidator
{
    private static readonly PublicKey PublicKey = LoadEmbeddedPublicKey();

    public static LicenseValidationResult Validate(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return LicenseValidationResult.Failure("License key is empty.");

        var parts = licenseKey.Trim().Split('.');
        if (parts.Length != 2)
            return LicenseValidationResult.Failure("Invalid license format.");

        byte[] payloadBytes;
        byte[] signatureBytes;

        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signatureBytes = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return LicenseValidationResult.Failure("Invalid base64url encoding.");
        }

        var algorithm = SignatureAlgorithm.Ed25519;

        if (!algorithm.Verify(PublicKey, payloadBytes, signatureBytes))
            return LicenseValidationResult.Failure("Invalid license signature.");

        License license;
        try
        {
            license = JsonSerializer.Deserialize<License>(payloadBytes)!;
        }
        catch (JsonException ex)
        {
            return LicenseValidationResult.Failure($"Invalid license payload: {ex.Message}");
        }

        if (license.IsExpired)
            return LicenseValidationResult.Failure("License has expired.");

        return LicenseValidationResult.Success(license);
    }

    private static PublicKey LoadEmbeddedPublicKey()
    {
        var assembly = typeof(LicenseValidator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("public.key", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var keyBytes = ms.ToArray();

        return NSec.Cryptography.PublicKey.Import(
            SignatureAlgorithm.Ed25519,
            keyBytes,
            KeyBlobFormat.RawPublicKey);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }
}
