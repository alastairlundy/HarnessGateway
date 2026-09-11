using System.CommandLine;
using LmProxy.AgentLauncher.Commands;
using LmProxy.AgentLauncher.Core.Adapters;
using LmProxy.AgentLauncher.Core.Backends.Models;
using LmProxy.AgentLauncher.Core.Configuration;
using LmProxy.AgentLauncher.Core.Execution;
using LmProxy.AgentLauncher.Core.Settings;
using LmProxy.AgentLauncher.Wizard;

var adapters = new List<IHarnessAdapter>
{
    new OpenCodeAdapter(),
    new KiloAdapter(),
    new CopilotCliAdapter(),
    new PiAdapter()
};

var settingsStore = new SettingsStore();
var backupsDir = Path.Combine(
    Path.GetDirectoryName(settingsStore.SettingsFilePath)!,
    "backups");
Directory.CreateDirectory(backupsDir);
var backupService = new BackupService(backupsDir);
var runner = new LaunchRunner();

var rootCommand = new RootCommand("lmlaunch - Configure and launch agent harnesses")
{
    new LaunchCommand(),
    new ConfigCommand()
};

rootCommand.SetAction(async (result, cancellationToken) =>
{
    var agent = result.GetValue(LaunchCommand.AgentArgument);
    var endpoint = result.GetValue(LaunchCommand.EndpointOption);
    var protocol = result.GetValue(LaunchCommand.ProtocolOption);
    var model = result.GetValue(LaunchCommand.ModelOption);
    var persistValues = result.GetValue(LaunchCommand.PersistOption);
    var persist = persistValues is not null
        ? LaunchCommand.ParsePersistValues(persistValues)
        : new List<string>();

    if (agent is not null)
    {
        var orchestrator = new LaunchOrchestrator(settingsStore, backupService, runner, adapters);
        return await orchestrator.RunAsync(
            agent,
            endpoint,
            protocol,
            model,
            persist,
            Directory.GetCurrentDirectory(),
            cancellationToken);
    }

    var console = new SpectreConsoleAbstraction();
    var catalogClient = new ModelCatalogClient(new HttpClient());
    var wizard = new WizardPipeline(console, adapters, settingsStore, catalogClient);
    var wizardResult = await wizard.RunAsync(cancellationToken);

    if (wizardResult is null)
    {
        return 0;
    }

    var orchestratorForWizard = new LaunchOrchestrator(settingsStore, backupService, runner, adapters);
    return await orchestratorForWizard.RunAsync(
        wizardResult.AgentName,
        wizardResult.Endpoint,
        wizardResult.Protocol.ToString().ToLowerInvariant(),
        wizardResult.Model,
        wizardResult.ApiKey is not null ? ["config"] : [],
        Directory.GetCurrentDirectory(),
        cancellationToken);
});

return rootCommand.Parse(args).Invoke();
