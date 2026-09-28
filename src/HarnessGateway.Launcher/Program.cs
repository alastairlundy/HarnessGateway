using System.CommandLine;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Backends.Models;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Settings.Security;
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

SettingsStore settingsStore = new();
string settingsDir = Path.GetDirectoryName(settingsStore.SettingsFilePath)!;
string backupsDir = Path.Combine(settingsDir, "backups");
Directory.CreateDirectory(backupsDir);
BackupService backupService = new(backupsDir);
LaunchRunner runner = new();
ISecureKeyStore keyStore = SecureKeyStoreFactory.Create(settingsDir);

RootCommand rootCommand = new("harnessgateway - Configure and launch agent harnesses")
{
    new LaunchCommand(),
    new ConfigCommand(settingsStore, keyStore)
};

rootCommand.SetAction(async (result, cancellationToken) =>
{
    string? agent = result.GetValue(LaunchCommand.AgentArgument);
    string? backendName = result.GetValue(LaunchCommand.BackendOption);
    string? endpoint = result.GetValue(LaunchCommand.EndpointOption);
    string? protocol = result.GetValue(LaunchCommand.ProtocolOption);
    string? model = result.GetValue(LaunchCommand.ModelOption);
    string[]? persistValues = result.GetValue(LaunchCommand.PersistOption);
    IReadOnlyList<string> persist = persistValues is not null
        ? LaunchCommand.ParsePersistValues(persistValues)
        : new List<string>();

    if (agent is not null)
    {
        LaunchOrchestrator orchestrator = new(settingsStore, backupService, runner, adapters, keyStore);
        return await orchestrator.RunAsync(
            agent,
            backendName,
            endpoint,
            protocol,
            model,
            persist,
            Directory.GetCurrentDirectory(),
            cancellationToken);
    }

    SpectreConsoleAbstraction console = new();
    ModelCatalogClient catalogClient = new(new HttpClient());
    WizardPipeline wizard = new(console, adapters, settingsStore, keyStore, catalogClient);
    WizardResult? wizardResult = await wizard.RunAsync(cancellationToken);

    if (wizardResult is null)
    {
        return 0;
    }

    LaunchOrchestrator orchestratorForWizard = new(settingsStore, backupService, runner, adapters, keyStore);
    return await orchestratorForWizard.RunAsync(
        wizardResult.AgentName,
        null,
        wizardResult.Endpoint,
        wizardResult.Protocol.ToString().ToLowerInvariant(),
        wizardResult.Model,
        wizardResult.ApiKey is not null ? ["config"] : [],
        Directory.GetCurrentDirectory(),
        cancellationToken);
});

return rootCommand.Parse(args).Invoke();
