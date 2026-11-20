using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Settings.Security;

namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The launch pipeline as one concrete module: declarative resolution, refusal
/// folding, key resolution, plan execution, and the restore guarantee. The
/// guarantee is structural, not comment-enforced: one try/finally wraps the
/// configuration-action execution and the run, so every exit path - success,
/// runner fault, config-write fault, cancellation - attempts the restore, and
/// only <c>--persist config</c> opts out. A restore fault folds to
/// <see cref="LaunchFailed"/> with <see cref="LaunchFailureKind.Unexpected"/>
/// (exit 7 upstream) and skips the model persist, which runs strictly after
/// the try/finally statement.
/// </summary>
/// <remarks>
/// Concrete by design - collaborators come in through the constructor, no
/// interface: adapters keep planning (they never restore, never persist), and
/// this flow executes their plans and interposes the guarantee. Refusals never
/// cross this seam as exceptions; <see cref="LaunchRefusalException"/> from an
/// adapter's planning step is caught here and folded into the outcome family.
/// GUI refusals are composed here Core-side rather than in the exe.
/// </remarks>
public sealed class LaunchFlow
{
    private const string NoProviderMessage =
        "No provider is configured. Run 'harnessgateway " +
        "provider add <name> --endpoint <url> --protocol <protocol>' to save one, or pass " +
        "--endpoint <url> --protocol <protocol> for a one-off launch.";

    private readonly SettingsStore _settingsStore;
    private readonly BackupService _backupService;
    private readonly ILaunchRunner _runner;
    private readonly IReadOnlyList<IHarnessAdapter> _adapters;
    private readonly ISecureKeyStore _keyStore;
    private readonly IGuiInstallDiscovery _installDiscovery;
    private readonly IRunningInstanceDetector _instanceDetector;
    private readonly IConfirmationPrompt _confirmationPrompt;
    private readonly IProtocolEmissionPlanner _emissionPlanner;
    private readonly ITerminalProbe _terminalProbe;

    public LaunchFlow(
        SettingsStore settingsStore,
        BackupService backupService,
        ILaunchRunner runner,
        IReadOnlyList<IHarnessAdapter> adapters,
        ISecureKeyStore keyStore,
        IGuiInstallDiscovery installDiscovery,
        IRunningInstanceDetector instanceDetector,
        IConfirmationPrompt confirmationPrompt,
        IProtocolEmissionPlanner emissionPlanner,
        ITerminalProbe terminalProbe)
    {
        _settingsStore = settingsStore;
        _backupService = backupService;
        _runner = runner;
        _adapters = adapters;
        _keyStore = keyStore;
        _installDiscovery = installDiscovery;
        _instanceDetector = instanceDetector;
        _confirmationPrompt = confirmationPrompt;
        _emissionPlanner = emissionPlanner;
        _terminalProbe = terminalProbe;
    }

    /// <summary>
    /// Executes one launch submission and returns its terminal outcome. The
    /// working directory is captured from <see cref="Environment.CurrentDirectory"/>
    /// at call time so <see cref="LaunchRequest"/> stays purely declarative.
    /// </summary>
    public async Task<LaunchOutcome> ExecuteAsync(LaunchRequest request, CancellationToken ct = default)
    {
        string workingDirectory = Environment.CurrentDirectory;

        try
        {
            LauncherSettings settings = await _settingsStore.LoadAsync(ct).ConfigureAwait(false);

            ProviderResolutionResult resolution = ProviderResolution.Resolve(
                settings,
                request.ProviderName,
                request.Endpoint,
                request.Protocol,
                request.Model);

            if (resolution is ProviderResolutionResult.Failed failed)
            {
                return new LaunchRefused(RefusalKindFor(failed.Failure), failed.Message);
            }

            // Resolved carries its launch case non-null; EmptySettings is the
            // success-without-a-provider variant and falls through to the
            // usage-error guard below. The family itself travels on to the
            // adapter payload - the transitional bridge that synthesized a
            // stored-looking provider for the ad-hoc case is gone, so a
            // raw-URL launch reaches the adapter as the keyless case it is.
            LaunchResolution? launch = resolution switch
            {
                ProviderResolutionResult.Resolved resolved => resolved.Launch,
                ProviderResolutionResult.EmptySettings => null,
                _ => throw new InvalidOperationException(
                    $"unexpected resolution variant '{resolution.GetType().Name}'"),
            };

            await _backupService.SelfHealAsync(ct).ConfigureAwait(false);

            Result<IHarnessAdapter> adapterResult = HarnessLookup.TryResolve(_adapters, request.Harness);
            if (!adapterResult.Success)
            {
                return new LaunchRefused(
                    LaunchRefusalKind.UnknownHarness,
                    HarnessLookup.UnknownAgentMessage(_adapters, request.Harness));
            }

            IHarnessAdapter adapter = adapterResult.Value;

            // Availability takes the Kind's own mechanism: Kind=Cli keeps
            // the detection-command probe, Kind=Gui asks the operating
            // system through discovery. An exhausted search is an honest
            // HarnessUnavailable refusal, never a crash.
            bool isAvailable = adapter.Metadata.Kind == HarnessKind.Gui
                ? _installDiscovery.Discover(adapter.Metadata.InstanceProcessNames).Found
                : await adapter.IsAvailableAsync(ct).ConfigureAwait(false);
            if (!isAvailable)
            {
                return new LaunchRefused(LaunchRefusalKind.HarnessUnavailable, adapter.Metadata.InstallHint);
            }

            // Misuse check, before anything is resolved for it:
            // --confirm-running asserts the answer to a live-instance
            // confirmation only a GUI harness has.
            if (request.ConfirmRunning && adapter.Metadata.Kind == HarnessKind.Cli)
            {
                return new LaunchRefused(
                    LaunchRefusalKind.ConfirmUnsupportedForHarness,
                    $"Refusing to launch {request.Harness}: --confirm-running confirms an "
                    + "already-running GUI application instance, and this harness runs as a "
                    + "CLI. Re-run without --confirm-running.");
            }

            // How this launch runs, decided before anything else is resolved
            // for it. Two modes: -p/--print is a one-shot non-interactive
            // run and needs a harness that has one plus a prompt to run
            // with; without it a CLI harness runs as an interactive
            // terminal session, which is only possible while this process is
            // attached to a terminal. A piped/CI context would hand the
            // child handles nobody can read - a TUI would render into them
            // invisibly, and a harness that detects the difference (Claude
            // Code reads a non-TTY stdout as print mode) would refuse with a
            // confusing error of its own.
            INonInteractiveLaunchAdapter? nonInteractiveAdapter =
                adapter as INonInteractiveLaunchAdapter;

            LaunchRefused? modeRefusal = ModeRefusal(adapter, nonInteractiveAdapter, request);
            if (modeRefusal is not null)
            {
                return modeRefusal;
            }

            // A successful resolution can still carry no launch at all: empty
            // settings with no --endpoint/--protocol override. Adapters plan
            // from a resolved case, so refuse here with a readable message
            // instead of NRE-ing inside PlanLaunchAsync. This is a usage
            // error, not an internal fault: nothing is corrupt and nothing
            // threw.
            if (launch is null)
            {
                return new LaunchRefused(LaunchRefusalKind.NoProvider, NoProviderMessage);
            }

            // Fail before any configuration action when the resolved
            // protocol is not supported by the chosen harness.
            if (!adapter.Metadata.SupportedProtocols.Contains(launch.Protocol))
            {
                // The refusal prose quotes a provider name: the stored
                // entry's for the provider case, and the "override"
                // spelling today's synthesised bridge printed for a
                // raw-URL launch for the ad-hoc case.
                string providerName = launch switch
                {
                    LaunchResolution.ProviderCase stored => stored.Provider.Name,
                    LaunchResolution.AdHocCase => "override",
                    _ => throw new InvalidOperationException(
                        $"unexpected launch resolution case '{launch.GetType().Name}'"),
                };

                string supported = string.Join(
                    ", ",
                    adapter.Metadata.SupportedProtocols.Select(ProviderResolution.ProtocolName));
                return new LaunchRefused(
                    LaunchRefusalKind.ProtocolNotSupported,
                    $"Refusing to launch {request.Harness}: the selected provider's protocol " +
                    $"({ProviderResolution.ProtocolName(launch.Protocol)}) is not supported by " +
                    $"{adapter.Metadata.Name}, which supports: {supported}. " +
                    "Override the launch with --endpoint <provider-url> --protocol <supported-protocol>, " +
                    "or re-point the default provider with 'harnessgateway provider add " +
                    $"{providerName} --endpoint <url> --protocol <supported-protocol>'.");
            }

            string? resolvedModel = ResolveModel(launch, adapter);

            // The launch-scoped secret: the provider case's stored reference
            // resolved into the secret on this local copy only - `settings`
            // keeps carrying the reference, so a later Save
            // (PersistDefaultModel) can never write a secret back out - and
            // the ad-hoc case keyless by design, since a raw-URL launch has
            // no stored reference of any kind.
            string? apiKey;
            switch (launch)
            {
                case LaunchResolution.ProviderCase providerCase:
                    Provider resolvedProvider = await ProviderKeyResolver
                        .ResolveAsync(providerCase.Provider, _keyStore, ct)
                        .ConfigureAwait(false);
                    apiKey = resolvedProvider.ApiKey;
                    break;
                case LaunchResolution.AdHocCase:
                    apiKey = null;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"unexpected launch resolution case '{launch.GetType().Name}'");
            }

            // The wizard's collected key (and only that carry-over) overrides
            // on this launch copy, never on the stored entry. The wizard
            // supplies a key only when the resolved entry had none or the user
            // typed one, so this layering can only fill a gap; it never masks
            // a stored key with a stale copy. It is also what makes a
            // first-run ad-hoc launch credentialled at all.
            if (!string.IsNullOrEmpty(request.SessionKey))
            {
                apiKey = request.SessionKey;
            }

            if (adapter.Metadata.Kind == HarnessKind.Gui)
            {
                // The live-instance confirmation: a detected instance asks,
                // the scripted override skips the prompt in every context,
                // and a decline or an unanswerable prompt stops the launch
                // before anything is written.
                LaunchRefused? unconfirmed = await ConfirmRunningInstanceAsync(
                    adapter, request, ct).ConfigureAwait(false);
                if (unconfirmed is not null)
                {
                    return unconfirmed;
                }

                // Capability check, still before planning: what this launch
                // asks to deliver must ride a surface the harness declares.
                LaunchRefused? surfaceRefusal = DeclaredSurfaceRefusal(adapter, request, apiKey);
                if (surfaceRefusal is not null)
                {
                    return surfaceRefusal;
                }
            }

            ResolvedLaunch payload = new(launch, apiKey, workingDirectory, _emissionPlanner);

            LaunchPlan plan;
            try
            {
                // Print planning goes through the capability the gate above
                // already proved present: the adapter puts the prompt where
                // the harness's own one-shot mode expects it.
                plan = request.Print
                    ? await nonInteractiveAdapter!.PlanNonInteractiveAsync(
                        payload, request.PrintPrompt!, ct).ConfigureAwait(false)
                    : await adapter.PlanLaunchAsync(payload, ct).ConfigureAwait(false);
            }
            catch (LaunchRefusalException ex)
            {
                // The adapter refused before any write: a user-owned config
                // collision. Folded here so the exe never sees the exception.
                return new LaunchRefused(LaunchRefusalKind.ProviderBlockConflict, ex.Message);
            }

            if (adapter.Metadata.Kind == HarnessKind.Gui)
            {
                // Configuration rides only the surfaces the harness declares:
                // a file write needs Files and a flag action needs Flags.
                // (Key material is already gated above - it only ever rides
                // environment variables, and only when Environment is
                // declared - and the launcher never writes keys to disk.)
                plan = FilterToDeclaredSurfaces(plan, adapter.Metadata.SupportedSurfaces);
            }

            bool persistConfig = request.Persist.Contains("config", StringComparer.OrdinalIgnoreCase);

            // Mode dispatch: a Kind=Gui launch that persists its config
            // applies the writes and returns at process handoff
            // (fire-and-forget); every other launch blocks on the harness,
            // and the finally below restores after the child exits exactly
            // as a CLI launch does.
            bool detached = adapter.Metadata.Kind == HarnessKind.Gui && persistConfig;

            // The restore guarantee, structural: whatever happens inside this
            // try - a config-write fault, a runner fault, a cancellation - the
            // finally attempts the restore. Nothing registered means the
            // restore is a no-op; --persist config opts out via the unchanged
            // parameter (the write is then durable by design).
            CompletedPayload completed;
            try
            {
                foreach (ConfigurationAction action in plan.ConfigurationActions)
                {
                    await ExecuteConfigurationActionAsync(action, ct).ConfigureAwait(false);
                }

                if (detached)
                {
                    completed = new CompletedPayload.HandedOff(_runner.Handoff(plan));
                }
                else
                {
                    // The blocking wait is where the three modes diverge: a
                    // GUI launch waits on a human closing the application, so
                    // its run carries no timeout - the process library's
                    // default 2-minute bound would kill the app mid-session.
                    // A -p/--print launch runs without that bound too (a
                    // one-shot agent run legitimately outlives two minutes)
                    // and stays buffered, so its output is captured and
                    // echoed when it exits. An interactive CLI launch hands
                    // the terminal over instead: no redirection, no clock -
                    // the gate above already proved there is a terminal to
                    // hand over.
                    Task<int> blockingRun = adapter.Metadata.Kind == HarnessKind.Gui
                                            || request.Print
                        ? _runner.RunWithoutTimeoutAsync(plan, ct)
                        : _runner.RunInteractiveAsync(plan, ct);

                    completed = new CompletedPayload.ExitStatus(
                        await blockingRun.ConfigureAwait(false));
                }
            }
            finally
            {
                await _backupService.RestoreOnExitAsync(persistConfig, ct).ConfigureAwait(false);
            }

            // Persist after run and restore, outside the try block: a restore
            // fault in the finally propagates and lands in the terminal fold,
            // so the model is never saved onto a launch whose configuration
            // could not be restored (today's ordering where restore throwing
            // meant no persist). Only the provider case has a stored entry to
            // persist onto; an ad-hoc launch never writes settings (it can
            // only arise when no entries exist at all).
            if (request.Persist.Contains("model", StringComparer.OrdinalIgnoreCase)
                && resolvedModel is not null
                && launch is LaunchResolution.ProviderCase selected)
            {
                PersistDefaultModel(settings, selected.Provider.Name, resolvedModel);
            }

            return new LaunchCompleted(completed);
        }
        catch (UnsupportedSettingsSchemaException ex)
        {
            return new LaunchFailed(LaunchFailureKind.UnsupportedSettingsSchema, ex.Message);
        }
        catch (SecureKeyStoreException ex)
        {
            // The absent-key miss is user-rectifiable and message-complete;
            // every other store failure is an operational credential-store
            // error. Both keep today's raw-message, exit-7 surfacing.
            return new LaunchFailed(
                ex.IsAbsentKey
                    ? LaunchFailureKind.CredentialStoreAbsentKey
                    : LaunchFailureKind.CredentialStoreError,
                ex.Message);
        }
        catch (Exception ex)
        {
            // The terminal fold: any other exception - including a restore
            // fault thrown from the finally - becomes one typed failure. The
            // caller decides surfacing (exit 7 with the "Launch failed:"
            // prefix for this kind).
            return new LaunchFailed(LaunchFailureKind.Unexpected, ex.Message);
        }
    }

    private static LaunchRefusalKind RefusalKindFor(ProviderResolutionFailure failure) => failure switch
    {
        ProviderResolutionFailure.UnknownProviderName => LaunchRefusalKind.UnknownProviderName,
        ProviderResolutionFailure.DanglingDefaultProvider => LaunchRefusalKind.DanglingDefaultProvider,
        ProviderResolutionFailure.AbsentDefaultProvider => LaunchRefusalKind.AbsentDefaultProvider,
        ProviderResolutionFailure.ProtocolWithoutEndpoint => LaunchRefusalKind.ProtocolWithoutEndpoint,
        ProviderResolutionFailure.EndpointWithoutProtocol => LaunchRefusalKind.EndpointWithoutProtocol,
        ProviderResolutionFailure.InvalidEndpoint => LaunchRefusalKind.InvalidEndpoint,
        ProviderResolutionFailure.InvalidProtocol => LaunchRefusalKind.InvalidProtocol,
        ProviderResolutionFailure.UnlistedModel => LaunchRefusalKind.UnlistedModel,
        _ => throw new InvalidOperationException($"unexpected resolution failure '{failure}'"),
    };

    /// <summary>
    /// The mode gate for one launch: <c>-p/--print</c> needs a harness with
    /// a non-interactive mode and a non-whitespace prompt to run with, and
    /// without it an interactive CLI launch needs this process to be
    /// attached to a terminal on both stdin and stdout. A GUI launch is
    /// exempt from the terminal half - it hands off or blocks headlessly -
    /// but never from the print half: a GUI application has no one-shot
    /// mode either.
    /// </summary>
    /// <returns>The refusal to stop with, or <c>null</c> to proceed.</returns>
    private LaunchRefused? ModeRefusal(
        IHarnessAdapter adapter,
        INonInteractiveLaunchAdapter? nonInteractiveAdapter,
        LaunchRequest request)
    {
        if (request.Print)
        {
            if (nonInteractiveAdapter is null)
            {
                string capability = adapter.Metadata.Kind == HarnessKind.Gui
                    ? $"{adapter.Metadata.Name} is a GUI application and has no "
                      + "non-interactive mode"
                    : $"{adapter.Metadata.Name} has no non-interactive mode the "
                      + "launcher knows how to drive";
                return new LaunchRefused(
                    LaunchRefusalKind.PrintUnsupportedForHarness,
                    $"Refusing to launch {request.Harness}: -p/--print asks for a "
                    + $"one-shot non-interactive run, but {capability}. Re-run without "
                    + "-p/--print to launch it normally.");
            }

            if (string.IsNullOrWhiteSpace(request.PrintPrompt))
            {
                return new LaunchRefused(
                    LaunchRefusalKind.PrintPromptMissing,
                    $"Refusing to launch {request.Harness}: -p/--print needs a prompt. "
                    + "Pass one as an argument (-p \"<prompt>\") or pipe one on stdin "
                    + $"(echo \"...\" | harnessgateway launch {request.Harness} -p).");
            }

            return null;
        }

        if (adapter.Metadata.Kind == HarnessKind.Cli && !_terminalProbe.IsInteractiveTerminal)
        {
            return new LaunchRefused(
                LaunchRefusalKind.TerminalRequired,
                $"Refusing to launch {request.Harness}: this harness runs as an "
                + "interactive terminal session, but this launch has no terminal - "
                + "stdin or stdout is redirected or piped. Run HarnessGateway from a "
                + "real terminal, or pass -p/--print \"<prompt>\" for a one-shot "
                + "non-interactive run.");
        }

        return null;
    }

    /// <summary>
    /// The live-instance confirmation chain for a Kind=Gui launch: a
    /// detected instance asks through the injected prompt, the request's
    /// scripted override skips that prompt in every context, and a decline -
    /// or a prompt nothing could answer - stops as
    /// <see cref="LaunchRefusalKind.RunningInstanceUnconfirmed"/> with its
    /// message composed here.
    /// </summary>
    /// <returns>The refusal to stop with, or <c>null</c> to proceed.</returns>
    private async Task<LaunchRefused?> ConfirmRunningInstanceAsync(
        IHarnessAdapter adapter,
        LaunchRequest request,
        CancellationToken cancellationToken)
    {
        bool found = _instanceDetector.DetectRunningInstances(
            adapter.Metadata.InstanceProcessNames).Found;
        if (!found || request.ConfirmRunning)
        {
            // No live instance, or the caller already asserted the answer
            // the prompt would have collected.
            return null;
        }

        string prompt =
            $"{adapter.Metadata.Name} is already running. Launching now applies configuration "
            + "to an application that is open, which the running instance may overwrite or "
            + "ignore. Proceed anyway? [y/N]";

        bool confirmed = await _confirmationPrompt.ConfirmAsync(prompt, cancellationToken)
            .ConfigureAwait(false);
        if (confirmed)
        {
            return null;
        }

        return new LaunchRefused(
            LaunchRefusalKind.RunningInstanceUnconfirmed,
            $"Refusing to launch {adapter.Metadata.Name}: an instance is already running and "
            + "the launch was not confirmed. Close the application first, or re-run with "
            + "--confirm-running to launch while it is open.");
    }

    /// <summary>
    /// The declared-surface capability gate for a Kind=Gui launch: what this
    /// launch asks to deliver must ride a surface the harness declares.
    /// Key material is delivered only as environment variables on the child
    /// process - never written to disk - so a key against a harness without
    /// that surface refuses, as does a backend or model override against a
    /// harness declaring no surface at all. A launch that asks for neither
    /// never produces this refusal.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="apiKey">
    /// The launch-scoped resolved secret, if this launch carries one.
    /// </param>
    /// <param name="adapter"></param>
    /// <returns>The refusal to stop with, or <see langword="null"/> to proceed.</returns>
    private static LaunchRefused? DeclaredSurfaceRefusal(
        IHarnessAdapter adapter,
        LaunchRequest request,
        string? apiKey)
    {
        IReadOnlySet<ConfigurationSurface> surfaces = adapter.Metadata.SupportedSurfaces;
        string name = adapter.Metadata.Name;

        if (!string.IsNullOrEmpty(apiKey)
            && !surfaces.Contains(ConfigurationSurface.Environment))
        {
            return new LaunchRefused(
                LaunchRefusalKind.GuiProviderUnsupported,
                $"Refusing to launch {name}: this launch carries a saved API key, but the "
                + "harness declares no environment surface to receive it. Keys ride child "
                + "process environment variables only, and the launcher never writes key "
                + "material to a configuration file. Launch this harness with a key-free "
                + "provider, or use a harness that accepts environment configuration.");
        }

        bool backendOverride = request.ProviderName is not null
                               || request.Endpoint is not null
                               || request.Protocol is not null;
        if ((backendOverride || request.Model is not null) && surfaces.Count == 0)
        {
            return new LaunchRefused(
                LaunchRefusalKind.GuiProviderUnsupported,
                $"Refusing to launch {name}: a provider or model override was requested, but "
                + "the harness declares no configuration surface that could carry it, so "
                + "the override could never reach the application. Launch this harness "
                + "without provider or model overrides.");
        }

        return null;
    }

    /// <summary>
    /// Restricts a GUI plan's configuration actions to the surfaces the
    /// harness declares: file writes need <see cref="ConfigurationSurface.Files"/>,
    /// flag actions need <see cref="ConfigurationSurface.Flags"/>. An action
    /// on an undeclared surface is dropped rather than executed, so a plan
    /// can never configure through a mechanism the harness does not support.
    /// </summary>
    private static LaunchPlan FilterToDeclaredSurfaces(
        LaunchPlan plan,
        IReadOnlySet<ConfigurationSurface> surfaces)
    {
        IReadOnlyList<ConfigurationAction> actions = plan.ConfigurationActions
            .Where(action => action switch
            {
                ConfigFileWriteAction => surfaces.Contains(ConfigurationSurface.Files),
                CliArgumentAction => surfaces.Contains(ConfigurationSurface.Flags),
                _ => throw new InvalidOperationException(
                    $"unexpected configuration action variant '{action.GetType().Name}'"),
            })
            .ToArray();

        return plan with { ConfigurationActions = actions };
    }

    /// <summary>
    /// Picks the model for this launch: the case's effective model - the
    /// resolution layer already applied <c>--model</c> over the stored
    /// default before the family was built, so both cases carry the value
    /// this launch runs with.
    /// </summary>
    /// <remarks>
    /// Deliberately unaware of <c>--persist</c>. Whether the model is saved is
    /// decided after the child process exits, not here, so this takes only what
    /// model resolution actually needs. This is also where a missing required
    /// model is warned about - a warning, never a refusal.
    /// </remarks>
    private static string? ResolveModel(LaunchResolution launch, IHarnessAdapter adapter)
    {
        string? resolvedModel = launch switch
        {
            LaunchResolution.ProviderCase providerCase => providerCase.Model,
            LaunchResolution.AdHocCase adHoc => adHoc.Model,
            _ => throw new InvalidOperationException(
                $"unexpected launch resolution case '{launch.GetType().Name}'"),
        };

        if (adapter.Metadata.RequiresModel && resolvedModel is null)
        {
            Console.Error.WriteLine($"Warning: {adapter.Metadata.Name} requires a model but none was specified. Launching anyway.");
        }

        return resolvedModel;
    }

    private async Task ExecuteConfigurationActionAsync(ConfigurationAction action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case ConfigFileWriteAction fileWrite:
                string? directory = Path.GetDirectoryName(fileWrite.TargetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Backup before write: the marker is what the finally's
                // restore (or SelfHealAsync after a crash) replays.
                await _backupService.BackupAsync(fileWrite.TargetPath, cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(fileWrite.TargetPath, fileWrite.Content, cancellationToken).ConfigureAwait(false);
                break;

            case CliArgumentAction:
                break;
        }
    }

    private void PersistDefaultModel(LauncherSettings settings, string providerName, string model)
    {
        // Persist onto the stored entry by name only: per-launch field
        // overrides (--endpoint/--protocol/--model layering) never ride back
        // into settings, and a --provider launch never re-points DefaultProvider.
        // Only the provider case calls this - an ad-hoc launch has no stored
        // entry to persist onto and never reaches this method.
        int index = settings.Providers.FindIndex(b =>
            string.Equals(b.Name, providerName, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return;
        }

        settings.Providers[index] = settings.Providers[index] with { DefaultModel = model };
        _settingsStore.Save(settings);
    }
}