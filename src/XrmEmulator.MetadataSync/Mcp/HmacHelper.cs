using System.Security.Cryptography;
using System.Text;

namespace XrmEmulator.MetadataSync.Mcp;

public static class HmacHelper
{
    public static string ComputeHmac(string base64Key, string message)
    {
        var keyBytes = Convert.FromBase64String(base64Key);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hash = HMACSHA256.HashData(keyBytes, messageBytes);
        return Convert.ToBase64String(hash);
    }

    public static bool ValidateHmac(string base64Key, string message, string providedHmac)
    {
        var expected = ComputeHmac(base64Key, message);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(providedHmac));
    }

    public static string GenerateSigningKey()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(keyBytes);
    }
}
