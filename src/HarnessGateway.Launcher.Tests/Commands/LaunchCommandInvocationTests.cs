using System.CommandLine;
using CliInvoke.Core;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Commands;

/// <summary>
/// Regression tests for the launch wiring: `harnessgateway launch <harness>`
/// must reach the orchestrator through the real parse-and-invoke pipeline.
/// The launch subcommand previously had no action of its own, and
/// System.CommandLine's no-action fallback silently succeeded with exit 0 -
/// while every orchestrator-level test kept passing, because they call
/// RunAsync directly instead of parsing.
/// </summary>
public class LaunchCommandInvocationTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-launchinvocation-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (RootCommand Root, FakeHarnessAdapter Adapter, string TempDir) CreateRoot(
        FakeHarnessAdapter adapter,
        string? settingsJson = null,
        int runnerExitCode = 0,
        Func<CancellationToken, Task<int>>? wizardAction = null)
    {
        string tempDir = CreateTempDir();
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        SettingsStore settingsStore = new SettingsStore(settingsDir, new FakeSecureKeyStore());
        if (settingsJson is not null)
        {
            File.WriteAllText(settingsStore.SettingsFilePath, settingsJson);
        }

        BackupService backupService = new BackupService(backupsDir);
        LaunchOrchestrator orchestrator = new LaunchOrchestrator(
            settingsStore, backupService, new StubLaunchRunner(runnerExitCode), [adapter], new FakeSecureKeyStore());

        LaunchCommand launchCommand = new LaunchCommand(orchestrator, wizardAction);
        return (new RootCommand { launchCommand }, adapter, tempDir);
    }

    [Test]
    public async Task Invoke_bare_launch_runs_the_wizard_not_the_orchestrator()
    {
        FakeHarnessAdapter adapter = new FakeHarnessAdapter("TestAgent");
        bool wizardCalled = false;
        (RootCommand root, _, string tempDir) = CreateRoot(
            adapter,
            wizardAction: _ =>
            {
                wizardCalled = true;
                return Task.FromResult(17);
            });
        try
        {
            int exitCode = await root.Parse("launch").InvokeAsync();

            await Assert.That(wizardCalled).IsTrue();
            await Assert.That(exitCode).IsEqualTo(17);
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_bare_launch_without_a_wizard_fails_loudly_not_silently()
    {
        FakeHarnessAdapter adapter = new FakeHarnessAdapter("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                root.Parse("launch").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UsageError);
            await Assert.That(errorOutput).Contains("A harness identifier is required");
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_launch_subcommand_runs_the_orchestrator()
    {
        FakeHarnessAdapter adapter = new FakeHarnessAdapter("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter);
        try
        {
            // Errors surface through exit codes here; nothing is asserted on
            // stderr because Console.Error is a process-global redirect target
            // that parallel tests write into while captures are held.
            int exitCode = await root
                .Parse("launch TestAgent --endpoint https://api.example.com/v1 --protocol openai-chat")
                .InvokeAsync();

            await Assert.That(exitCode).IsEqualTo(0);
            // The orchestrator actually ran: the parsed flags reached the adapter
            // as a launch request. Any of these failing would re-expose the
            // silent no-op this suite guards against.
            await Assert.That(adapter.LastRequest).IsNotNull();
            await Assert.That(adapter.LastRequest!.Backend.BaseUri)
                .IsEqualTo(new Uri("https://api.example.com/v1"));
            await Assert.That(adapter.LastRequest!.Backend.Protocol).IsEqualTo(ProtocolKind.OpenAiChat);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_unknown_agent_surfaces_exit_3_instead_of_silent_success()
    {
        FakeHarnessAdapter adapter = new FakeHarnessAdapter("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                root.Parse("launch NoSuchAgent").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownAgent);
            await Assert.That(errorOutput).Contains("Unknown harness: NoSuchAgent");
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Invoke_unknown_backend_name_surfaces_exit_8_through_the_cli()
    {
        FakeHarnessAdapter adapter = new FakeHarnessAdapter("TestAgent");
        (RootCommand root, _, string tempDir) = CreateRoot(adapter, settingsJson: """
            {
              "backends": [
                {
                  "name": "primary",
                  "baseUri": "https://primary.example/v1",
                  "protocol": 0
                }
              ],
              "defaultBackend": "primary",
              "schemaVersion": 2
            }
            """);
        try
        {
            (int exitCode, string errorOutput) = await ConsoleErrorCapture.RunAsync(() =>
                root.Parse("launch TestAgent --backend nope").InvokeAsync());

            await Assert.That(exitCode).IsEqualTo((int)LaunchExitCode.UnknownBackendName);
            await Assert.That(errorOutput).Contains("unknown backend name 'nope'");
            await Assert.That(adapter.LastRequest).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private sealed class FakeHarnessAdapter : IHarnessAdapter
    {
        public HarnessMetadata Metadata { get; }
        public LaunchRequest? LastRequest { get; private set; }

        public FakeHarnessAdapter(string name)
        {
            Metadata = new HarnessMetadata(
                name,
                name,
                [],
                HarnessKind.Cli,
                RequiresModel: false,
                InstallHint: "Install via: test-install",
                DetectionCommand: null,
                [],
                new HashSet<ProtocolKind>
                {
                    ProtocolKind.OpenAiChat,
                    ProtocolKind.OpenAiResponses,
                    ProtocolKind.Anthropic
                });
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<LaunchPlan> PlanLaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            ProcessConfiguration process = new ProcessConfiguration("test")
            {
                Arguments = "run",
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true
            };
            return Task.FromResult(new LaunchPlan(process, []));
        }
    }

    private sealed class StubLaunchRunner(int exitCode) : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(exitCode);
        }
    }
}
