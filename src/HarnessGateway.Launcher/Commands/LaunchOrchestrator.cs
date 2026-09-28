using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Commands;

public sealed class LaunchOrchestrator
{
    private readonly SettingsStore _settingsStore;
    private readonly BackupService _backupService;
    private readonly ILaunchRunner _runner;
    private readonly IReadOnlyList<IHarnessAdapter> _adapters;

    public LaunchOrchestrator(
        SettingsStore settingsStore,
        BackupService backupService,
        ILaunchRunner runner,
        IReadOnlyList<IHarnessAdapter> adapters)
    {
        _settingsStore = settingsStore;
        _backupService = backupService;
        _runner = runner;
        _adapters = adapters;
    }

    public async Task<int> RunAsync(
        string agentName,
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

            Backend? backend = ResolveBackend(settings, endpoint, protocol);

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

            // T028: fail before any configuration action when the resolved backend's
            // protocol is not supported by the chosen harness.
            if (backend is not null
                && !adapter.Metadata.SupportedProtocols.Contains(backend.Protocol))
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Launch failed: {ex.Message}");
            return (int)LaunchExitCode.ConfigRestoreFailure;
        }
    }

    private Backend? ResolveBackend(LauncherSettings settings, string? endpoint, string? protocol)
    {
        if (endpoint is not null && protocol is not null)
        {
            ProtocolKind parsedProtocol = LaunchCommand.ParseProtocol(protocol);
            Uri parsedEndpoint = LaunchCommand.ParseEndpoint(endpoint);
            return new()
            {
                Name = "override",
                BaseUri = parsedEndpoint,
                Protocol = parsedProtocol
            };
        }

        if (!string.IsNullOrEmpty(settings.DefaultBackend))
        {
            return settings.Backends.FirstOrDefault(b =>
                string.Equals(b.Name, settings.DefaultBackend, StringComparison.OrdinalIgnoreCase));
        }

        return settings.Backends.FirstOrDefault();
    }

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

        Backend updatedBackend = backend with { DefaultModel = model };
        int index = settings.Backends.FindIndex(b =>
            string.Equals(b.Name, backend.Name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            settings.Backends[index] = updatedBackend;
        }
        else
        {
            settings.Backends.Add(updatedBackend);
        }

        _settingsStore.Save(settings);
    }
}
