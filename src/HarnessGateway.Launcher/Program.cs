/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using CliInvoke;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Wizard;
using HarnessGateway.Launcher.Wizard.Abstractions;
using HarnessGateway.Launcher.Wizard.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelsDotDevSharp.Abstractions;
using ModelsDotDevSharp.Extensions;

// Composition root: every collaborator the launch flow and the wizard
// consume is registered on one service collection, so the services the
// model/provider disambiguation feature adds (the models.dev client, the
// catalog cache, the API-key scrub list) register through this same
// mechanism instead of another hand-rolled construction block. Everything
// is a singleton: one LaunchFlow instance serves both the CLI
// orchestrator and the wizard handoff, and that sharing is structural -
// the container hands out one instance - rather than incidental (a local
// both paths happen to close over).
string settingsDir = SettingsStore.GetPlatformConfigDirectory();
string backupsDir = Path.Combine(settingsDir, "backups");
Directory.CreateDirectory(backupsDir);

ServiceCollection services = new();

// The store shares the flow's backup service so the load-time migration's
// backup-before-write markers live in the same directory SelfHeal replays.
services.AddSingleton<ISecureKeyStore>(_ => SecureKeyStoreFactory.Create(settingsDir));
services.AddSingleton(_ => new BackupService(backupsDir));
services.AddSingleton(serviceProvider => new SettingsStore(
    settingsDir,
    serviceProvider.GetRequiredService<ISecureKeyStore>(),
    serviceProvider.GetRequiredService<BackupService>()));
services.AddSingleton<ILaunchRunner>(_ => new LaunchRunner());

// Each adapter registers as IHarnessAdapter; the one list every consumer
// shares - the flow, the root command factory, the wizard - is materialised
// from those registrations once, in registration order, so the list can
// never drift from what is registered.
services.AddSingleton<IHarnessAdapter>(_ => new OpenCodeAdapter());
services.AddSingleton<IHarnessAdapter>(_ => new OpenCodeDesktopAdapter());
services.AddSingleton<IHarnessAdapter>(_ => new KiloAdapter());
services.AddSingleton<IHarnessAdapter>(_ => new CopilotCliAdapter());
services.AddSingleton<IHarnessAdapter>(_ => new PiAdapter());
services.AddSingleton<IHarnessAdapter>(_ => new CodexAdapter());
services.AddSingleton<IHarnessAdapter>(_ => new ClaudeCodeAdapter());
services.AddSingleton<IReadOnlyList<IHarnessAdapter>>(serviceProvider =>
    serviceProvider.GetServices<IHarnessAdapter>().ToList());

// GUI collaborators, registered alongside the ones above: install discovery
// (CliInvoke's default resolver first, then this OS's conventional roots),
// the running-instance detector for the OS this binary runs on, and the
// terminal confirmation prompt the flow asks its live-instance question
// through. Detection and discovery read the OS only; nothing here starts a
// process or writes a file.
services.AddSingleton<IGuiInstallDiscovery>(_ => new GuiInstallDiscovery(new FilePathResolver()));
services.AddSingleton<IRunningInstanceDetector>(_ => CreateInstanceDetector());
services.AddSingleton<IConfirmationPrompt>(_ => new TerminalConfirmationPrompt());

// The terminal gate's production probe: an interactive CLI launch only
// runs while this process is attached to a real terminal, and this reads
// that fact off the console handles. Registered like the collaborators
// above so the flow never reads the console directly.
services.AddSingleton<ITerminalProbe>(_ => new TerminalProbe());

// The one shared protocol-to-env-var mapping: registered here at
// the composition root and handed to the launch flow, which forwards it
// to every adapter on the launch payload - the mapping is reached
// through this wiring, never through statics.
services.AddSingleton<IProtocolEmissionPlanner>(_ => new ProtocolEmissionPlanner());

// The curated API-key list behind --clean-env: the same instance the flow
// uses to finalize the child's environment, registered beside the other
// provider services rather than constructed at the call site.
services.AddSingleton<IApiKeyScrubList>(_ => new ApiKeyScrubList());

// The models.dev catalog behind bare-model qualification: the
// ModelsDotDevSharp client services register through the package's own
// extension, and the cache that owns load, the freshness window, fetch,
// and save registers beside them. Both are singletons, so the whole
// process shares one cache instance - the at-most-one-fetch-per-launch
// rule is structural rather than something callers must coordinate.
services.AddModelsDotDevSharp();
services.AddSingleton(serviceProvider => new CatalogCache(
    serviceProvider.GetRequiredService<ICatalogRepository>(),
    TimeProvider.System));
services.AddSingleton<ModelQualifier>();
// The Core-side picker seam is implemented by the exe's console host so the
// flow never depends on the wizard abstraction directly; the host resolves
// IConsoleAbstraction lazily, which is registered below with the wizard.
services.AddSingleton<IModelChoicePrompt>(serviceProvider =>
    new ConsoleModelChoicePrompt(
        serviceProvider.GetRequiredService<IConsoleAbstraction>()));

// One flow, both callers: the CLI-side orchestrator and the wizard
// handoff below both submit through this one container-managed instance.
services.AddSingleton(serviceProvider => new LaunchFlow(
    serviceProvider.GetRequiredService<SettingsStore>(),
    serviceProvider.GetRequiredService<BackupService>(),
    serviceProvider.GetRequiredService<ILaunchRunner>(),
    serviceProvider.GetRequiredService<IReadOnlyList<IHarnessAdapter>>(),
    serviceProvider.GetRequiredService<ISecureKeyStore>(),
    serviceProvider.GetRequiredService<IGuiInstallDiscovery>(),
    serviceProvider.GetRequiredService<IRunningInstanceDetector>(),
    serviceProvider.GetRequiredService<IConfirmationPrompt>(),
    serviceProvider.GetRequiredService<IProtocolEmissionPlanner>(),
    serviceProvider.GetRequiredService<ITerminalProbe>(),
    serviceProvider.GetRequiredService<CatalogCache>(),
    serviceProvider.GetRequiredService<ModelQualifier>(),
    serviceProvider.GetRequiredService<IModelChoicePrompt>(),
    serviceProvider.GetRequiredService<IApiKeyScrubList>()));
services.AddSingleton(serviceProvider =>
    new LaunchOrchestrator(serviceProvider.GetRequiredService<LaunchFlow>()));

// The wizard's own collaborators, registered at the same root: its console
// abstraction, the model-catalog client behind the ModelStep lookup, and
// the pipeline itself.
services.AddSingleton<IConsoleAbstraction>(_ => new SpectreConsoleAbstraction());
services.AddSingleton(_ => new ModelCatalogClient(new HttpClient()));
services.AddSingleton(serviceProvider => new WizardPipeline(
    serviceProvider.GetRequiredService<IConsoleAbstraction>(),
    serviceProvider.GetRequiredService<IReadOnlyList<IHarnessAdapter>>(),
    serviceProvider.GetRequiredService<SettingsStore>(),
    serviceProvider.GetRequiredService<ISecureKeyStore>(),
    serviceProvider.GetRequiredService<ModelCatalogClient>()));

using ServiceProvider provider = services.BuildServiceProvider();

// The roots the entry point hands over, resolved once from the container
// so the root command factory and the wizard handoff share one graph.
IReadOnlyList<IHarnessAdapter> adapters =
    provider.GetRequiredService<IReadOnlyList<IHarnessAdapter>>();
SettingsStore settingsStore = provider.GetRequiredService<SettingsStore>();
ISecureKeyStore keyStore = provider.GetRequiredService<ISecureKeyStore>();
LaunchFlow flow = provider.GetRequiredService<LaunchFlow>();
LaunchOrchestrator orchestrator = provider.GetRequiredService<LaunchOrchestrator>();

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

async Task<int> RunWizardAsync(CancellationToken cancellationToken)
{
    // Resolved from the same container the orchestrator came from: the
    // wizard's collaborators, and the flow it submits to below, are one
    // shared graph rather than a second hand-rolled construction.
    WizardPipeline wizard = provider.GetRequiredService<WizardPipeline>();

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

    // The collected ApiKey must be passed as SessionKey - without it the launch
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
        [],
        SessionKey: wizardResult.ApiKey);

    LaunchOutcome outcome = await flow.ExecuteAsync(request, cancellationToken)
        .ConfigureAwait(true);
    return await LaunchOrchestrator.MapOutcomeAsync(outcome).ConfigureAwait(true);
}
