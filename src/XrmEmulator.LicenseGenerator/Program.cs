using Spectre.Console.Cli;
using XrmEmulator.LicenseGenerator.Commands;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("xrm-license");

    config.AddCommand<GenerateKeysCommand>("generate-keys")
        .WithDescription("Generate an Ed25519 key pair for license signing");

    config.AddCommand<CreateLicenseCommand>("create")
        .WithDescription("Create a signed license key");

    config.AddCommand<ValidateLicenseCommand>("validate")
        .WithDescription("Validate an existing license key");

    config.AddCommand<ListFeaturesCommand>("list-features")
        .WithDescription("List available feature keys");
});

return app.Run(args);
