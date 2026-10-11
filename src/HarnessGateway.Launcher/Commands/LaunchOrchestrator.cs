using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Commands;

public sealed class LaunchOrchestrator
{
    private readonly SettingsStore _settingsStore;
    private readonly BackupService _backupService;
    private readonly ILaunchRunner _runner;
    private readonly IReadOnlyList<IHarnessAdapter> _adapters;
    private readonly ISecureKeyStore _keyStore;

    public LaunchOrchestrator(
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

    public async Task<int> RunAsync(
        string agentName, string? backendName,
        string? endpoint, string? protocol,
        string? model, IReadOnlyList<string> persistValues,
        string workingDirectory, string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LauncherSettings settings = _settingsStore.Load();

            BackendResolutionResult resolution = BackendResolution.Resolve(
                settings,
                backendName,
                endpoint,
                protocol,
                model);

            if (resolution is BackendResolutionResult.Failed failed)
            {
                await Console.Error.WriteLineAsync(failed.Message).ConfigureAwait(true);
                return ExitCodeFor(failed.Failure);
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

            await _backupService.SelfHealAsync(cancellationToken).ConfigureAwait(true);

            IHarnessAdapter? adapter = FindAdapter(agentName);
            if (adapter is null)
            {
                await Console.Error.WriteLineAsync(HarnessLookup.UnknownAgentMessage(_adapters, agentName)).ConfigureAwait(true);
                return (int)LaunchExitCode.UnknownAgent;
            }

            bool isAvailable = await adapter.IsAvailableAsync(cancellationToken).ConfigureAwait(true);
            if (!isAvailable)
            {
                await Console.Error.WriteLineAsync(adapter.Metadata.InstallHint).ConfigureAwait(true);
                return (int)LaunchExitCode.HarnessUnavailable;
            }

            // A successful resolution can still carry no backend at all: empty
            // settings with no --endpoint/--protocol override. Adapters need a
            // backend, so refuse here with a readable message instead of
            // NRE-ing inside PlanLaunchAsync.
            //
            // This is a usage error, not a restore failure: nothing is corrupt
            // and nothing threw, the caller simply has not supplied a backend.
            // It used to exit 7 purely because that was what the
            // unhandled-exception path produced before this guard existed, and
            // scripts learned to read a first-run mistake as a config-restore
            // failure. Exit 2 already means "the invocation was missing
            // something it needs".
            //
            // No "Launch failed:" prefix: that marker is reserved for the
            // unhandled-exception path, and a user-rectifiable usage error
            // should not read like an internal one.
            if (backend is null)
            {
                await Console.Error.WriteLineAsync(
                    "No backend is configured. Run 'harnessgateway " +
                    "config backend add <name> --endpoint <url> --protocol <protocol>' to save one, or pass " +
                    "--endpoint <url> --protocol <protocol> for a one-off launch.").ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            // Fail before any configuration action when the resolved backend's
            // protocol is not supported by the chosen harness.
            if (!adapter.Metadata.SupportedProtocols.Contains(backend.Protocol))
            {
                string supported = string.Join(
                    ", ",
                    adapter.Metadata.SupportedProtocols.Select(LaunchCommand.ProtocolName));
                await Console.Error.WriteLineAsync(
                        $"Refusing to launch {agentName}: the selected backend's protocol " +
                        $"({LaunchCommand.ProtocolName(backend.Protocol)}) is not supported by " +
                        $"{adapter.Metadata.Name}, which supports: {supported}. " +
                        "Override the launch with --endpoint <backend-url> --protocol <supported-protocol>, " +
                        "or re-point the default backend with 'harnessgateway config backend add " +
                        $"{backend.Name} --endpoint <url> --protocol <supported-protocol>'.")
                    .ConfigureAwait(true);
                return (int)LaunchExitCode.UnknownAgent;
            }

            string? resolvedModel = ResolveModel(model, backend, adapter);

            // Resolve the at-rest key reference into the secret on this local
            // copy only: `settings` keeps carrying the reference, so a later
            // Save (PersistDefaultModel) can never write a secret back out.
            backend = await BackendKeyResolver.ResolveAsync(backend, _keyStore, cancellationToken).ConfigureAwait(true);

            // The wizard's collected key (and only that carry-over - the CLI
            // surface passes no apiKey at all) overrides on this local copy,
            // never on the stored entry: `settings` keeps carrying its
            // reference, so a later Save (PersistDefaultModel) can never write
            // the launch-time secret back out. The wizard supplies a key only
            // when the resolved entry had none or the user typed one, so this
            // layering can only fill a gap; it never masks a stored key with a
            // stale copy.
            if (!string.IsNullOrEmpty(apiKey))
            {
                backend = backend with { ApiKey = apiKey };
            }

            LaunchRequest request = new(backend, resolvedModel, workingDirectory);
            LaunchPlan plan = await adapter.PlanLaunchAsync(request, cancellationToken).ConfigureAwait(true);

            foreach (ConfigurationAction action in plan.ConfigurationActions)
            {
                await ExecuteConfigurationActionAsync(action, cancellationToken).ConfigureAwait(true);
            }

            int exitCode = await _runner.RunAsync(plan, cancellationToken).ConfigureAwait(true);

            bool persistConfig = persistValues.Contains("config", StringComparer.OrdinalIgnoreCase);
            await _backupService.RestoreOnExitAsync(persistConfig, cancellationToken).ConfigureAwait(true);

            if (persistValues.Contains("model", StringComparer.OrdinalIgnoreCase) && resolvedModel is not null)
            {
                PersistDefaultModel(settings, backend, resolvedModel);
            }

            return exitCode;
        }
        catch (LaunchRefusalException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(true);
            return (int)LaunchExitCode.ConfigConflict;
        }
        catch (SecureKeyStoreException ex)
        {
            // User-rectifiable: the message names the backend and how to re-save.
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(true);
            return (int)LaunchExitCode.ConfigRestoreFailure;
        }
        catch (UnsupportedSettingsSchemaException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(true);
            return (int)LaunchExitCode.UnsupportedSettingsSchema;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Launch failed: {ex.Message}").ConfigureAwait(true);
            return (int)LaunchExitCode.ConfigRestoreFailure;
        }
    }

    private static int ExitCodeFor(BackendResolutionFailure failure) =>
        failure is BackendResolutionFailure.UnknownBackendName
            or BackendResolutionFailure.DanglingDefaultBackend
            or BackendResolutionFailure.AbsentDefaultBackend
            ? (int)LaunchExitCode.UnknownBackendName
            : (int)LaunchExitCode.UsageError;

    private IHarnessAdapter? FindAdapter(string agentName) =>
        HarnessLookup.Resolve(_adapters, agentName);

    /// <summary>
    /// Picks the model for this launch: the explicit <c>--model</c> value, else
    /// the resolved entry's saved default.
    /// </summary>
    /// <remarks>
    /// Deliberately unaware of <c>--persist</c>. Whether the model is saved is
    /// decided by the caller after the child process exits, not here, so this
    /// takes only what model resolution actually needs. (It used to accept
    /// <c>persistValues</c> and ignore it, from the orchestrator's first
    /// commit.) This is also where a missing required model is warned about,
    /// and where exit code 6 would belong if that ever became a refusal.
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
                await _backupService.BackupAsync(fileWrite.TargetPath, cancellationToken).ConfigureAwait(true);
                await File.WriteAllTextAsync(fileWrite.TargetPath, fileWrite.Content, cancellationToken).ConfigureAwait(true);
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