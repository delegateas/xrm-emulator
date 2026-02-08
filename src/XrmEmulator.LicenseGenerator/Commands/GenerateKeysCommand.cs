using System.ComponentModel;
using System.Runtime.InteropServices;
using NSec.Cryptography;
using Spectre.Console;
using Spectre.Console.Cli;

namespace XrmEmulator.LicenseGenerator.Commands;

public sealed class GenerateKeysSettings : CommandSettings
{
    [CommandOption("--output <DIRECTORY>")]
    [Description("Output directory for key files (default: current directory)")]
    public string Output { get; set; } = ".";
}

public sealed class GenerateKeysCommand : Command<GenerateKeysSettings>
{
    public override int Execute(CommandContext context, GenerateKeysSettings settings)
    {
        var outputDir = Path.GetFullPath(settings.Output);
        Directory.CreateDirectory(outputDir);

        var privateKeyPath = Path.Combine(outputDir, "private.key");
        var publicKeyPath = Path.Combine(outputDir, "public.key");

        // Warn if files already exist
        if (File.Exists(privateKeyPath) || File.Exists(publicKeyPath))
        {
            if (!AnsiConsole.Confirm("[yellow]Key files already exist. Overwrite?[/]", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Key generation cancelled.[/]");
                return 0;
            }
        }

        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = Key.Create(algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });

        var privateKeyBytes = key.Export(KeyBlobFormat.RawPrivateKey);
        var publicKeyBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        File.WriteAllBytes(privateKeyPath, privateKeyBytes);
        File.WriteAllBytes(publicKeyPath, publicKeyBytes);

        // Set restrictive permissions on private key (Linux/macOS)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(privateKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        AnsiConsole.MarkupLine("[green]Ed25519 key pair generated successfully.[/]");
        AnsiConsole.MarkupLine($"  Private key: [blue]{privateKeyPath}[/]");
        AnsiConsole.MarkupLine($"  Public key:  [blue]{publicKeyPath}[/]");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AnsiConsole.MarkupLine("  [grey]Private key permissions set to 600 (owner read/write only)[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Base64 (for vault storage):[/]");
        AnsiConsole.MarkupLine($"  Private key: [blue]{Convert.ToBase64String(privateKeyBytes)}[/]");
        AnsiConsole.MarkupLine($"  Public key:  [blue]{Convert.ToBase64String(publicKeyBytes)}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Keep the private key secret. Copy public.key into XrmEmulator.Licensing/Keys/.[/]");

        return 0;
    }
}
