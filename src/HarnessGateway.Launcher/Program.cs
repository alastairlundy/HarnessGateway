using CliInvoke;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Wizard;

List<IHarnessAdapter> adapters =
[
    new OpenCodeAdapter(),
    new OpenCodeDesktopAdapter(),
    new KiloAdapter(),
    new CopilotCliAdapter(),
    new PiAdapter(),
    new CodexAdapter(),
    new ClaudeCodeAdapter(),
];

string settingsDir = SettingsStore.GetPlatformConfigDirectory();
ISecureKeyStore keyStore = SecureKeyStoreFactory.Create(settingsDir);
string backupsDir = Path.Combine(settingsDir, "backups");
Directory.CreateDirectory(backupsDir);
BackupService backupService = new(backupsDir);
// The store shares the flow's backup service so the load-time migration's
// backup-before-write markers live in the same directory SelfHeal replays.
SettingsStore settingsStore = new(settingsDir, keyStore, backupService);
LaunchRunner runner = new();

// GUI collaborators, injected alongside the ones above: install discovery
// (CliInvoke's default resolver first, then this OS's conventional roots),
// the running-instance detector for the OS this binary runs on, and the
// terminal confirmation prompt the flow asks its live-instance question
// through. Detection and discovery read the OS only; nothing here starts a
// process or writes a file.
IGuiInstallDiscovery installDiscovery = new GuiInstallDiscovery(new FilePathResolver());
IRunningInstanceDetector instanceDetector = CreateInstanceDetector();
IConfirmationPrompt confirmationPrompt = new TerminalConfirmationPrompt();

// The one shared protocol-to-env-var mapping: registered here at
// the composition root and handed to the launch flow, which forwards it
// to every adapter on the launch payload - the mapping is reached
// through this wiring, never through statics.
IProtocolEmissionPlanner emissionPlanner = new ProtocolEmissionPlanner();

// One flow, both callers: the CLI-side orchestrator and the wizard
// handoff below both submit through this instance.
LaunchFlow flow = new(
    settingsStore,
    backupService,
    runner,
    adapters,
    keyStore,
    installDiscovery,
    instanceDetector,
    confirmationPrompt,
    emissionPlanner);
LaunchOrchestrator orchestrator = new(flow);
async Task<int> RunWizardAsync(CancellationToken cancellationToken)
{
    SpectreConsoleAbstraction console = new();
    ModelCatalogClient catalogClient = new(new HttpClient());
    WizardPipeline wizard = new(console, adapters, settingsStore, keyStore, catalogClient);

    // Load-time faults surface as messages with documented exit codes rather
    // than escaping the wizard: unsupported schema, credential-store errors,
    // and migration I/O faults (ConfigRestoreFailure, no stack trace).
    (WizardOutcome? guardedOutcome, int guardExit) =
        await RunWizardGuardedAsync(wizard, cancellationToken).ConfigureAwait(true);
    if (guardedOutcome is null)
    {
        return guardExit;
    }

    WizardOutcome wizardOutcome = guardedOutcome;

    if (wizardOutcome is WizardAborted aborted)
    {
        return aborted.Reason switch
        {
            WizardAbortReason.Cancelled => 0,
            WizardAbortReason.DanglingDefaultProvider => (int)LaunchExitCode.UnknownProviderName,
            WizardAbortReason.AbsentDefaultProvider => (int)LaunchExitCode.UnknownProviderName,
            WizardAbortReason.NoHarnessInstalled => (int)LaunchExitCode.HarnessUnavailable,
            WizardAbortReason.NoHarnessSupportsProtocol => (int)LaunchExitCode.UnknownAgent,
            WizardAbortReason.CredentialStoreError => (int)LaunchExitCode.ConfigRestoreFailure,
            _ => (int)LaunchExitCode.ConfigRestoreFailure,
        };
    }

    WizardResult wizardResult = ((WizardCompleted)wizardOutcome).Result;

    // The collected ApiKey must ride as SessionKey - without it the launch
    // proceeds with no credential at all on a first run, and the harness
    // fails auth downstream with no clue that the key the user typed was
    // dropped. Persist stays empty: a wizard-driven launch obeys the same
    // restore-on-exit guarantee as a CLI launch, so the harness config the
    // launch edits (e.g. codex config.toml) is restored when the process
    // exits. The wizard accepts no --persist flag of its own.
    LaunchRequest request = new(
        wizardResult.AgentCommandName,
        ProviderName: wizardResult.ProviderName,
        wizardResult.Endpoint,
        LaunchCommand.ProtocolName(wizardResult.Protocol),
        wizardResult.Model,
        Persist: [],
        SessionKey: wizardResult.ApiKey);

    LaunchOutcome outcome = await flow.ExecuteAsync(request, cancellationToken)
        .ConfigureAwait(true);
    return await LaunchOrchestrator.MapOutcomeAsync(outcome).ConfigureAwait(true);
}

// Runs one wizard pass with its load-time faults folded into (null, exit
// code): unsupported schema, credential-store errors, and migration I/O
// faults (ConfigRestoreFailure, message only - no stack trace, exit code
// stays inside the documented set). Mirrors how ConfigCommand maps the same
// faults on its load path.
static async Task<(WizardOutcome? Outcome, int ExitCode)> RunWizardGuardedAsync(
    WizardPipeline wizard,
    CancellationToken cancellationToken)
{
    try
    {
        return (await wizard.RunAsync(cancellationToken).ConfigureAwait(true), 0);
    }
    catch (UnsupportedSettingsSchemaException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return (null, (int)LaunchExitCode.UnsupportedSettingsSchema);
    }
    catch (SecureKeyStoreException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return (null, (int)LaunchExitCode.ConfigRestoreFailure);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        // The load-time migration writes (backup marker, temp file,
        // atomic move); mapped the same way ConfigCommand maps it.
        Console.Error.WriteLine($"Settings load failed: {ex.Message}");
        return (null, (int)LaunchExitCode.ConfigRestoreFailure);
    }
}

// Each detector is marked to the platform it serves, so the construction
// sits behind that platform's own guard; an OS outside the three has no
// detector implementation and says so instead of guessing.
static IRunningInstanceDetector CreateInstanceDetector()
{
    if (OperatingSystem.IsWindows())
    {
        return new WindowsInstanceDetector();
    }

    if (OperatingSystem.IsMacOS())
    {
        return new MacOsInstanceDetector();
    }

    if (OperatingSystem.IsLinux())
    {
        return new LinuxInstanceDetector();
    }

    throw new PlatformNotSupportedException(
        "Running-instance detection ships Windows, macOS, and Linux implementations only.");
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
