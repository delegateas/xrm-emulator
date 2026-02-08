using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;
using Spectre.Console;
using Spectre.Console.Cli;

namespace XrmEmulator.LicenseGenerator.Commands;

public sealed class ValidateLicenseSettings : CommandSettings
{
    [CommandArgument(0, "<LICENSE_KEY>")]
    [Description("The license key string to validate")]
    public string LicenseKey { get; set; } = null!;

    [CommandOption("--public-key <PATH>")]
    [Description("Path to public key file (default: ./public.key)")]
    public string PublicKeyPath { get; set; } = "./public.key";
}

public sealed class ValidateLicenseCommand : Command<ValidateLicenseSettings>
{
    public override int Execute(CommandContext context, ValidateLicenseSettings settings)
    {
        var publicKeyPath = Path.GetFullPath(settings.PublicKeyPath);
        if (!File.Exists(publicKeyPath))
        {
            AnsiConsole.MarkupLine($"[red]Public key not found:[/] {publicKeyPath}");
            AnsiConsole.MarkupLine("Provide the public key with [blue]--public-key <path>[/].");
            return 1;
        }

        var parts = settings.LicenseKey.Split('.');
        if (parts.Length != 2)
        {
            AnsiConsole.MarkupLine("[red]Invalid license format.[/] Expected format: base64url(payload).base64url(signature)");
            return 1;
        }

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signatureBytes = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            AnsiConsole.MarkupLine("[red]Invalid license format.[/] Could not decode base64url data.");
            return 1;
        }

        // Verify signature
        var algorithm = SignatureAlgorithm.Ed25519;
        var publicKeyData = File.ReadAllBytes(publicKeyPath);

        bool signatureValid;
        try
        {
            var publicKey = PublicKey.Import(algorithm, publicKeyData, KeyBlobFormat.RawPublicKey);
            signatureValid = algorithm.Verify(publicKey, payloadBytes, signatureBytes);
        }
        catch (Exception)
        {
            signatureValid = false;
        }

        AnsiConsole.WriteLine();

        if (!signatureValid)
        {
            AnsiConsole.Write(new Panel("[red bold]INVALID - Signature verification failed[/]")
                .Header("[red]License Validation[/]")
                .Border(BoxBorder.Rounded));
            return 1;
        }

        // Deserialize payload
        LicensePayloadRead? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayloadRead>(payloadBytes);
            if (payload is null)
            {
                AnsiConsole.MarkupLine("[red]Failed to deserialize license payload.[/]");
                return 1;
            }
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[red]Invalid JSON payload:[/] {ex.Message}");
            return 1;
        }

        // Check expiry
        var isExpired = false;
        DateTimeOffset? expiresAt = null;
        if (payload.ExpiresAt is not null)
        {
            if (DateTimeOffset.TryParse(payload.ExpiresAt, out var parsed))
            {
                expiresAt = parsed;
                isExpired = parsed < DateTimeOffset.UtcNow;
            }
        }

        // Display results
        var statusColor = isExpired ? "yellow" : "green";
        var statusText = isExpired ? "VALID but EXPIRED" : "VALID";

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[{statusColor} bold]{statusText}[/]")
            .AddColumn("Field")
            .AddColumn("Value");

        table.AddRow("License ID", payload.LicenseId ?? "N/A");
        table.AddRow("Licensee", payload.Subject ?? "N/A");
        table.AddRow("Issued At", payload.IssuedAt ?? "N/A");
        table.AddRow("Expires", expiresAt?.ToString("o") ?? "Never (perpetual)");
        table.AddRow("Features", string.Join(", ", payload.Features ?? []));
        table.AddRow("Seats", payload.Seats == 0 ? "Unlimited" : payload.Seats.ToString());
        table.AddRow("Signature", $"[{statusColor}]Verified[/]");

        if (isExpired)
        {
            table.AddRow("Status", "[yellow]Expired[/]");
        }

        AnsiConsole.Write(table);

        return isExpired ? 2 : 0;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}

internal sealed class LicensePayloadRead
{
    [JsonPropertyName("lid")]
    public string? LicenseId { get; set; }

    [JsonPropertyName("sub")]
    public string? Subject { get; set; }

    [JsonPropertyName("iat")]
    public string? IssuedAt { get; set; }

    [JsonPropertyName("exp")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("features")]
    public string[]? Features { get; set; }

    [JsonPropertyName("seats")]
    public int Seats { get; set; }

    [JsonPropertyName("meta")]
    public Dictionary<string, string>? Meta { get; set; }
}
