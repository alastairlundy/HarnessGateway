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
/// </remarks>
public sealed class LaunchFlow
{
    private const string NoBackendMessage =
        "No backend is configured. Run 'harnessgateway " +
        "config backend add <name> --endpoint <url> --protocol <protocol>' to save one, or pass " +
        "--endpoint <url> --protocol <protocol> for a one-off launch.";

    private readonly SettingsStore _settingsStore;
    private readonly BackupService _backupService;
    private readonly ILaunchRunner _runner;
    private readonly IReadOnlyList<IHarnessAdapter> _adapters;
    private readonly ISecureKeyStore _keyStore;

    public LaunchFlow(
        SettingsStore settingsStore,
        BackupService backupService,
        ILaunchRunner runner,
        IReadOnlyList<IHarnessAdapter> adapters,
        ISecureKeyStore keyStore)
    {
        _settingsStore = settingsStore;
        _backupService = backupService;
        _runner = runner;
        _adapters = adapters;
        _keyStore = keyStore;
    }

    /// <summary>
    /// Executes one launch submission and returns its terminal outcome. The
    /// working directory is captured from <see cref="Environment.CurrentDirectory"/>
    /// at call time (decision I003): both callers used to pass
    /// <c>Directory.GetCurrentDirectory()</c>, the same value for an in-process
    /// CLI, so <see cref="LaunchRequest"/> stays purely declarative.
    /// </summary>
    public async Task<LaunchOutcome> ExecuteAsync(LaunchRequest request, CancellationToken ct = default)
    {
        string workingDirectory = Environment.CurrentDirectory;

        try
        {
            LauncherSettings settings = _settingsStore.Load();

            BackendResolutionResult resolution = BackendResolution.Resolve(
                settings,
                request.BackendName,
                request.Endpoint,
                request.Protocol,
                request.Model);

            if (resolution is BackendResolutionResult.Failed failed)
            {
                return new LaunchRefused(RefusalKindFor(failed.Failure), failed.Message);
            }

            // Resolved carries its backend non-null; EmptySettings is the
            // success-without-a-backend variant and falls through to the
            // usage-error guard below.
            Backend? backend = resolution switch
            {
                BackendResolutionResult.Resolved resolved => resolved.Backend,
                BackendResolutionResult.EmptySettings => null,
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

            bool isAvailable = await adapter.IsAvailableAsync(ct).ConfigureAwait(false);
            if (!isAvailable)
            {
                return new LaunchRefused(LaunchRefusalKind.HarnessUnavailable, adapter.Metadata.InstallHint);
            }

            // A successful resolution can still carry no backend at all: empty
            // settings with no --endpoint/--protocol override. Adapters need a
            // backend, so refuse here with a readable message instead of
            // NRE-ing inside PlanLaunchAsync. This is a usage error, not an
            // internal fault: nothing is corrupt and nothing threw.
            if (backend is null)
            {
                return new LaunchRefused(LaunchRefusalKind.NoBackend, NoBackendMessage);
            }

            // Fail before any configuration action when the resolved backend's
            // protocol is not supported by the chosen harness.
            if (!adapter.Metadata.SupportedProtocols.Contains(backend.Protocol))
            {
                string supported = string.Join(
                    ", ",
                    adapter.Metadata.SupportedProtocols.Select(BackendResolution.ProtocolName));
                return new LaunchRefused(
                    LaunchRefusalKind.ProtocolNotSupported,
                    $"Refusing to launch {request.Harness}: the selected backend's protocol " +
                    $"({BackendResolution.ProtocolName(backend.Protocol)}) is not supported by " +
                    $"{adapter.Metadata.Name}, which supports: {supported}. " +
                    "Override the launch with --endpoint <backend-url> --protocol <supported-protocol>, " +
                    "or re-point the default backend with 'harnessgateway config backend add " +
                    $"{backend.Name} --endpoint <url> --protocol <supported-protocol>'.");
            }

            string? resolvedModel = ResolveModel(request.Model, backend, adapter);

            // Resolve the at-rest key reference into the secret on this local
            // copy only: `settings` keeps carrying the reference, so a later
            // Save (PersistDefaultModel) can never write a secret back out.
            backend = await BackendKeyResolver.ResolveAsync(backend, _keyStore, ct).ConfigureAwait(false);

            // The wizard's collected key (and only that carry-over) overrides
            // on this local copy, never on the stored entry: `settings` keeps
            // carrying its reference, so a later Save (PersistDefaultModel)
            // can never write the launch-time secret back out. The wizard
            // supplies a key only when the resolved entry had none or the user
            // typed one, so this layering can only fill a gap; it never masks
            // a stored key with a stale copy.
            if (!string.IsNullOrEmpty(request.SessionKey))
            {
                backend = backend with { ApiKey = request.SessionKey };
            }

            ResolvedLaunch payload = new(backend, resolvedModel, workingDirectory);

            LaunchPlan plan;
            try
            {
                plan = await adapter.PlanLaunchAsync(payload, ct).ConfigureAwait(false);
            }
            catch (LaunchRefusalException ex)
            {
                // The adapter refused before any write: a user-owned config
                // collision. Folded here so the exe never sees the exception.
                return new LaunchRefused(LaunchRefusalKind.ProviderBlockConflict, ex.Message);
            }

            bool persistConfig = request.Persist.Contains("config", StringComparer.OrdinalIgnoreCase);

            // The restore guarantee, structural: whatever happens inside this
            // try - a config-write fault, a runner fault, a cancellation - the
            // finally attempts the restore. Nothing registered means the
            // restore is a no-op; --persist config opts out via the unchanged
            // parameter (the write is then durable by design).
            int exitStatus;
            try
            {
                foreach (ConfigurationAction action in plan.ConfigurationActions)
                {
                    await ExecuteConfigurationActionAsync(action, ct).ConfigureAwait(false);
                }

                exitStatus = await _runner.RunAsync(plan, ct).ConfigureAwait(false);
            }
            finally
            {
                await _backupService.RestoreOnExitAsync(persistConfig, ct).ConfigureAwait(false);
            }

            // Persist after run and restore, outside the try block: a restore
            // fault in the finally propagates and lands in the terminal fold,
            // so the model is never saved onto a launch whose configuration
            // could not be restored (today's ordering where restore throwing
            // meant no persist).
            if (request.Persist.Contains("model", StringComparer.OrdinalIgnoreCase) && resolvedModel is not null)
            {
                PersistDefaultModel(settings, backend, resolvedModel);
            }

            return new LaunchCompleted(exitStatus);
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

    private static LaunchRefusalKind RefusalKindFor(BackendResolutionFailure failure) => failure switch
    {
        BackendResolutionFailure.UnknownBackendName => LaunchRefusalKind.UnknownBackendName,
        BackendResolutionFailure.DanglingDefaultBackend => LaunchRefusalKind.DanglingDefaultBackend,
        BackendResolutionFailure.AbsentDefaultBackend => LaunchRefusalKind.AbsentDefaultBackend,
        BackendResolutionFailure.ProtocolWithoutEndpoint => LaunchRefusalKind.ProtocolWithoutEndpoint,
        BackendResolutionFailure.EndpointWithoutProtocol => LaunchRefusalKind.EndpointWithoutProtocol,
        BackendResolutionFailure.InvalidEndpoint => LaunchRefusalKind.InvalidEndpoint,
        BackendResolutionFailure.InvalidProtocol => LaunchRefusalKind.InvalidProtocol,
        _ => throw new InvalidOperationException($"unexpected resolution failure '{failure}'"),
    };

    /// <summary>
    /// Picks the model for this launch: the explicit <c>--model</c> value, else
    /// the resolved entry's saved default.
    /// </summary>
    /// <remarks>
    /// Deliberately unaware of <c>--persist</c>. Whether the model is saved is
    /// decided after the child process exits, not here, so this takes only what
    /// model resolution actually needs. This is also where a missing required
    /// model is warned about - a warning, never a refusal.
    /// </remarks>
    private string? ResolveModel(string? model, Backend? backend, IHarnessAdapter adapter)
    {
        string? resolvedModel = model;

        if (resolvedModel is null && backend?.DefaultModel is not null)
        {
            resolvedModel = backend.DefaultModel;
        }

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

    private void PersistDefaultModel(LauncherSettings settings, Backend? backend, string model)
    {
        if (backend is null) return;

        // Persist onto the stored entry by name only: per-launch field
        // overrides (--endpoint/--protocol/--model layering) never ride back
        // into settings, and a --backend launch never re-points DefaultBackend.
        int index = settings.Backends.FindIndex(b =>
            string.Equals(b.Name, backend.Name, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            // Ad-hoc raw-URL override against an empty settings file: there is
            // no durable entry to persist a default model onto.
            return;
        }

        settings.Backends[index] = settings.Backends[index] with { DefaultModel = model };
        _settingsStore.Save(settings);
    }
}
