using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Settings.Security;

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
        string agentName,
        string? backendName,
        string? endpoint,
        string? protocol,
        string? model,
        IReadOnlyList<string> persistValues,
        string workingDirectory,
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

            if (!resolution.IsSuccess)
            {
                Console.Error.WriteLine(resolution.Message);
                return ExitCodeFor(resolution.Failure);
            }

            Backend? backend = resolution.Backend;

            await _backupService.SelfHealAsync(cancellationToken);

            IHarnessAdapter? adapter = FindAdapter(agentName);
            if (adapter is null)
            {
                Console.Error.WriteLine($"Unknown agent: {agentName}");
                return (int)LaunchExitCode.UnknownAgent;
            }

            bool isAvailable = await adapter.IsAvailableAsync(cancellationToken);
            if (!isAvailable)
            {
                Console.Error.WriteLine(adapter.Metadata.InstallHint);
                return (int)LaunchExitCode.HarnessUnavailable;
            }

            // A successful resolution can still carry no backend at all: empty
            // settings with no --endpoint/--protocol override. Adapters need a
            // backend, so refuse here with a readable message instead of
            // NRE-ing inside PlanLaunchAsync. Exit 7 preserves the code the
            // unhandled-exception path produced before this guard existed (the
            // exit-code table is append-only, so no dedicated code yet).
            if (backend is null)
            {
                Console.Error.WriteLine(
                    "Launch failed: no backend is configured. Run 'harnessgateway " +
                    "config set-default-endpoint <url> <protocol>' to save one, or pass " +
                    "--endpoint <url> --protocol <protocol> for a one-off launch.");
                return (int)LaunchExitCode.ConfigRestoreFailure;
            }

            // Fail before any configuration action when the resolved backend's
            // protocol is not supported by the chosen harness.
            if (!adapter.Metadata.SupportedProtocols.Contains(backend.Protocol))
            {
                string supported = string.Join(
                    ", ",
                    adapter.Metadata.SupportedProtocols.Select(LaunchCommand.ProtocolName));
                Console.Error.WriteLine(
                    $"Refusing to launch {agentName}: the selected backend's protocol " +
                    $"({LaunchCommand.ProtocolName(backend.Protocol)}) is not supported by " +
                    $"{adapter.Metadata.Name}, which supports: {supported}. " +
                    "Override the launch with --endpoint <backend-url> --protocol <supported-protocol>, " +
                    "or re-point the default backend with " +
                    "'harnessgateway config set-default-endpoint <url> <protocol>'.");
                return (int)LaunchExitCode.UnknownAgent;
            }

            string? resolvedModel = ResolveModel(model, backend, adapter, persistValues);

            // Resolve the at-rest key reference into the secret on this local
            // copy only: `settings` keeps carrying the reference, so a later
            // Save (PersistDefaultModel) can never write a secret back out.
            backend = await BackendKeyResolver.ResolveAsync(backend, _keyStore, cancellationToken);

            LaunchRequest request = new(backend, resolvedModel, workingDirectory);
            LaunchPlan plan = await adapter.PlanLaunchAsync(request, cancellationToken);

            foreach (ConfigurationAction action in plan.ConfigurationActions)
            {
                await ExecuteConfigurationActionAsync(action, cancellationToken);
            }

            int exitCode = await _runner.RunAsync(plan, cancellationToken);

            bool persistConfig = persistValues.Contains("config");
            await _backupService.RestoreOnExitAsync(persistConfig, cancellationToken);

            if (persistValues.Contains("model") && resolvedModel is not null)
            {
                PersistDefaultModel(settings, backend, resolvedModel);
            }

            return exitCode;
        }
        catch (LaunchRefusalException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)LaunchExitCode.ConfigConflict;
        }
        catch (SecureKeyStoreException ex)
        {
            // User-rectifiable: the message names the backend and how to re-save.
            Console.Error.WriteLine(ex.Message);
            return (int)LaunchExitCode.ConfigRestoreFailure;
        }
        catch (UnsupportedSettingsSchemaException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)LaunchExitCode.UnsupportedSettingsSchema;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Launch failed: {ex.Message}");
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
        _adapters.FirstOrDefault(a =>
            string.Equals(a.Metadata.Name, agentName, StringComparison.OrdinalIgnoreCase));

    private string? ResolveModel(string? model, Backend? backend, IHarnessAdapter adapter, IReadOnlyList<string> persistValues)
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
                await _backupService.BackupAsync(fileWrite.TargetPath, cancellationToken);
                await File.WriteAllTextAsync(fileWrite.TargetPath, fileWrite.Content, cancellationToken);
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
