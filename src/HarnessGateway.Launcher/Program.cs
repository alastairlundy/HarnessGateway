using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Backends.Models;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Wizard;

List<IHarnessAdapter> adapters =
[
    new OpenCodeAdapter(),
    new KiloAdapter(),
    new CopilotCliAdapter(),
    new PiAdapter(),
    new CodexAdapter(),
    new ClaudeCodeAdapter()
];

string settingsDir = SettingsStore.GetPlatformConfigDirectory();
ISecureKeyStore keyStore = SecureKeyStoreFactory.Create(settingsDir);
SettingsStore settingsStore = new(settingsDir, keyStore);
string backupsDir = Path.Combine(settingsDir, "backups");
Directory.CreateDirectory(backupsDir);
BackupService backupService = new(backupsDir);
LaunchRunner runner = new();

LaunchOrchestrator orchestrator = new(
    settingsStore,
    backupService,
    runner,
    adapters,
    keyStore);
// The wizard is the no-args root action and, via LaunchCommand's own
// action, also what a bare `launch` runs - one implementation, two entry
// points, so the two can never drift in exception handling or exit codes.
async Task<int> RunWizardAsync(CancellationToken cancellationToken)
{
    SpectreConsoleAbstraction console = new();
    ModelCatalogClient catalogClient = new(new HttpClient());
    WizardPipeline wizard = new(console, adapters, settingsStore, keyStore, catalogClient);

    WizardResult? wizardResult;
    try
    {
        wizardResult = await wizard.RunAsync(cancellationToken).ConfigureAwait(true);
    }
    catch (UnsupportedSettingsSchemaException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return (int)LaunchExitCode.UnsupportedSettingsSchema;
    }
    catch (SecureKeyStoreException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return (int)LaunchExitCode.ConfigRestoreFailure;
    }

    if (wizardResult is null)
    {
        return 0;
    }

    // Direct launches run through the orchestrator too; the wizard result is
    // launch-only input (the pipeline never persists settings).
    return await orchestrator.RunAsync(
        wizardResult.AgentCommandName,
        null,
        wizardResult.Endpoint,
        LaunchCommand.ProtocolName(wizardResult.Protocol),
        wizardResult.Model,
        wizardResult.ApiKey is not null ? ["config"] : [],
        Directory.GetCurrentDirectory(),
        cancellationToken).ConfigureAwait(true);
}

// Root wiring (the --version action, launch/config registration, and the
// wizard root action) lives in RootCommandFactory so the tests can drive the
// same parse-and-invoke path this entry point uses.
RootCommand rootCommand = RootCommandFactory.Create(
    orchestrator,
    RunWizardAsync,
    adapters,
    settingsStore,
    keyStore);

return rootCommand.Parse(args).Invoke();
