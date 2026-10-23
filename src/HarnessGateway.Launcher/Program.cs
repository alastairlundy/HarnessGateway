using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Backends.Models;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Wizard;

List<IHarnessAdapter> adapters =
[
    new OpenCodeAdapter(),
    new KiloAdapter(),
    new CopilotCliAdapter(),
    new PiAdapter(),
    new CodexAdapter(),
    new ClaudeCodeAdapter(),
];

string settingsDir = SettingsStore.GetPlatformConfigDirectory();
ISecureKeyStore keyStore = SecureKeyStoreFactory.Create(settingsDir);
SettingsStore settingsStore = new(settingsDir, keyStore);
string backupsDir = Path.Combine(settingsDir, "backups");
Directory.CreateDirectory(backupsDir);
BackupService backupService = new(backupsDir);
LaunchRunner runner = new();

// One flow, both callers (#T002): the CLI-side orchestrator and the wizard
// handoff below both submit a LaunchRequest through this single LaunchFlow,
// so there is no second launch-order convention in the exe.
LaunchFlow flow = new(settingsStore, backupService, runner, adapters, keyStore);
LaunchOrchestrator orchestrator = new(flow);
// The wizard is the no-args root action and, via LaunchCommand's own
// action, also what a bare `launch` runs - one implementation, two entry
// points, so the two can never drift in exception handling or exit codes.
async Task<int> RunWizardAsync(CancellationToken cancellationToken)
{
    SpectreConsoleAbstraction console = new();
    ModelCatalogClient catalogClient = new(new HttpClient());
    WizardPipeline wizard = new(console, adapters, settingsStore, keyStore, catalogClient);

    WizardOutcome wizardOutcome;
    try
    {
        wizardOutcome = await wizard.RunAsync(cancellationToken).ConfigureAwait(true);
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

    if (wizardOutcome is WizardAborted aborted)
    {
        return aborted.Reason switch
        {
            WizardAbortReason.Cancelled => 0,
            WizardAbortReason.DanglingDefaultBackend => (int)LaunchExitCode.UnknownBackendName,
            WizardAbortReason.AbsentDefaultBackend => (int)LaunchExitCode.UnknownBackendName,
            WizardAbortReason.NoHarnessInstalled => (int)LaunchExitCode.HarnessUnavailable,
            WizardAbortReason.NoHarnessSupportsProtocol => (int)LaunchExitCode.UnknownAgent,
            WizardAbortReason.CredentialStoreError => (int)LaunchExitCode.ConfigRestoreFailure,
            _ => (int)LaunchExitCode.ConfigRestoreFailure,
        };
    }

    WizardResult wizardResult = ((WizardCompleted)wizardOutcome).Result;

    // The wizard handoff is the flow's second caller (#T002, #T014): it
    // builds the complete declarative LaunchRequest here - every decision is
    // visible in this construction - and submits it to the same flow the
    // direct CLI path uses. The wizard pipeline itself stays write-free: it
    // only produced this input; the flow owns everything that follows.
    //
    // The collected ApiKey must ride as SessionKey - without it the launch
    // proceeds with no credential at all on a first run, and the harness
    // fails auth downstream with no clue that the key the user typed was
    // dropped. When a key was collected, persist is forced to ["config"]: a
    // launch-time credential change the user confirmed in the wizard must
    // survive the launch, so the restore-on-exit default opts out.
    LaunchRequest request = new(
        wizardResult.AgentCommandName,
        BackendName: null,
        wizardResult.Endpoint,
        LaunchCommand.ProtocolName(wizardResult.Protocol),
        wizardResult.Model,
        Persist: wizardResult.ApiKey is not null ? ["config"] : [],
        SessionKey: wizardResult.ApiKey);

    LaunchOutcome outcome = await flow.ExecuteAsync(request, cancellationToken)
        .ConfigureAwait(true);
    return await LaunchOrchestrator.MapOutcomeAsync(outcome).ConfigureAwait(true);
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

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(true);
