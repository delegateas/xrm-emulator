using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;
using Spectre.Console;
using Spectre.Console.Cli;

namespace XrmEmulator.LicenseGenerator.Commands;

public sealed class CreateLicenseSettings : CommandSettings
{
    [CommandOption("--licensee <NAME>")]
    [Description("Name of the licensee (required)")]
    public string Licensee { get; set; } = null!;

    [CommandOption("--features <FEATURES>")]
    [Description("Comma-separated list of feature keys (required)")]
    public string Features { get; set; } = null!;

    [CommandOption("--expires <DATE>")]
    [Description("Expiry date (e.g. 2026-06-01). Omit for perpetual license")]
    public string? Expires { get; set; }

    [CommandOption("--private-key <PATH>")]
    [Description("Path to private key file (default: ./private.key)")]
    public string PrivateKeyPath { get; set; } = "./private.key";

    [CommandOption("--private-key-base64 <KEY>")]
    [Description("Base64-encoded private key (alternative to --private-key file)")]
    public string? PrivateKeyBase64 { get; set; }

    [CommandOption("--output <FILE>")]
    [Description("Output .lic file path (optional, also prints to stdout)")]
    public string? OutputFile { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Licensee))
            return ValidationResult.Error("--licensee is required");
        if (string.IsNullOrWhiteSpace(Features))
            return ValidationResult.Error("--features is required");
        return ValidationResult.Success();
    }
}

public sealed class CreateLicenseCommand : Command<CreateLicenseSettings>
{
    public override int Execute(CommandContext context, CreateLicenseSettings settings)
    {
        byte[] privateKeyData;

        if (!string.IsNullOrEmpty(settings.PrivateKeyBase64))
        {
            try
            {
                privateKeyData = Convert.FromBase64String(settings.PrivateKeyBase64);
            }
            catch (FormatException)
            {
                AnsiConsole.MarkupLine("[red]Invalid base64 for --private-key-base64[/]");
                return 1;
            }
        }
        else
        {
            var privateKeyPath = Path.GetFullPath(settings.PrivateKeyPath);
            if (!File.Exists(privateKeyPath))
            {
                AnsiConsole.MarkupLine($"[red]Private key not found:[/] {privateKeyPath}");
                AnsiConsole.MarkupLine("Use [blue]--private-key <path>[/] or [blue]--private-key-base64 <key>[/].");
                return 1;
            }
            privateKeyData = File.ReadAllBytes(privateKeyPath);
        }

        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrEmpty(settings.Expires))
        {
            if (!DateTimeOffset.TryParse(settings.Expires, out var parsed))
            {
                AnsiConsole.MarkupLine($"[red]Invalid date format:[/] {settings.Expires}");
                return 1;
            }
            expiresAt = parsed.ToUniversalTime();
        }

        var features = settings.Features
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        var payload = new LicensePayload
        {
            LicenseId = Guid.NewGuid().ToString(),
            Subject = settings.Licensee,
            IssuedAt = DateTimeOffset.UtcNow.ToString("o"),
            ExpiresAt = expiresAt?.ToString("o"),
            Features = features,
            Seats = 0,
            Meta = new Dictionary<string, string>()
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        var payloadJson = JsonSerializer.Serialize(payload, jsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        // Sign with Ed25519
        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Import(algorithm, privateKeyData, KeyBlobFormat.RawPrivateKey);
        var signature = algorithm.Sign(key, payloadBytes);

        // Build license string: base64url(payload).base64url(signature)
        var licenseKey = $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";

        // Display license info
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold green]License Created[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Field")
            .AddColumn("Value");

        table.AddRow("License ID", payload.LicenseId);
        table.AddRow("Licensee", payload.Subject);
        table.AddRow("Issued At", payload.IssuedAt);
        table.AddRow("Expires", expiresAt?.ToString("o") ?? "Never (perpetual)");
        table.AddRow("Features", string.Join(", ", features));

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]License Key:[/]");
        AnsiConsole.WriteLine(licenseKey);

        if (!string.IsNullOrEmpty(settings.OutputFile))
        {
            var outputPath = Path.GetFullPath(settings.OutputFile);
            File.WriteAllText(outputPath, licenseKey);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]Written to:[/] {outputPath}");
        }

        return 0;
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

internal sealed class LicensePayload
{
    [JsonPropertyName("lid")]
    public string LicenseId { get; set; } = null!;

    [JsonPropertyName("sub")]
    public string Subject { get; set; } = null!;

    [JsonPropertyName("iat")]
    public string IssuedAt { get; set; } = null!;

    [JsonPropertyName("exp")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("features")]
    public string[] Features { get; set; } = [];

    [JsonPropertyName("seats")]
    public int Seats { get; set; }

    [JsonPropertyName("meta")]
    public Dictionary<string, string> Meta { get; set; } = new();
}
