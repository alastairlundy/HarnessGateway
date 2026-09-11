using LmProxy.AgentLauncher.Core.Adapters;
using LmProxy.AgentLauncher.Core.Backends;
using LmProxy.AgentLauncher.Core.Configuration;
using LmProxy.AgentLauncher.Core.Execution;
using LmProxy.AgentLauncher.Core.Launch;
using LmProxy.AgentLauncher.Core.Settings;

namespace LmProxy.AgentLauncher.Commands;

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
            var settings = _settingsStore.Load();

            var backend = ResolveBackend(settings, endpoint, protocol);

            await _backupService.SelfHealAsync(cancellationToken);

            var adapter = FindAdapter(agentName);
            if (adapter is null)
            {
                Console.Error.WriteLine($"Unknown agent: {agentName}");
                return (int)LaunchExitCode.UnknownAgent;
            }

            var isAvailable = await adapter.IsAvailableAsync(cancellationToken);
            if (!isAvailable)
            {
                Console.Error.WriteLine(adapter.Metadata.InstallHint);
                return (int)LaunchExitCode.HarnessUnavailable;
            }

            var resolvedModel = ResolveModel(model, backend, adapter, persistValues);

            var request = new LaunchRequest(backend, resolvedModel, workingDirectory);
            var plan = await adapter.PlanLaunchAsync(request, cancellationToken);

            foreach (var action in plan.ConfigurationActions)
            {
                await ExecuteConfigurationActionAsync(action, cancellationToken);
            }

            var exitCode = await _runner.RunAsync(plan, cancellationToken);

            var persistConfig = persistValues.Contains("config");
            await _backupService.RestoreOnExitAsync(persistConfig, cancellationToken);

            if (persistValues.Contains("model") && resolvedModel is not null)
            {
                PersistDefaultModel(settings, backend, resolvedModel);
            }

            return exitCode;
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
            var parsedProtocol = LaunchCommand.ParseProtocol(protocol);
            var parsedEndpoint = LaunchCommand.ParseEndpoint(endpoint);
            return new Backend
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
        var resolvedModel = model;

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
                var directory = Path.GetDirectoryName(fileWrite.TargetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await _backupService.BackupAsync(fileWrite.TargetPath, cancellationToken);
                await File.WriteAllTextAsync(fileWrite.TargetPath, fileWrite.Content, cancellationToken);
                break;

            case EnvironmentVariableAction envVar:
                Environment.SetEnvironmentVariable(envVar.Name, envVar.Value);
                break;

            case CliArgumentAction:
                break;
        }
    }

    private void PersistDefaultModel(LauncherSettings settings, Backend? backend, string model)
    {
        if (backend is null) return;

        var updatedBackend = backend with { DefaultModel = model };
        var index = settings.Backends.FindIndex(b =>
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
