using System.CommandLine;
using System.Reflection;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Commands;

/// <summary>
/// `--version` is the field-identification seam: a bug report names its build
/// with it and the release smoke test executes it, so it must report the
/// pinned assembly informational version, exit 0, and never the
/// settings schemaVersion - different concepts, different surfaces. The flag
/// is driven through the real root command so the wiring itself is under
/// test, not a lookalike constructed by this suite.
/// </summary>
public class VersionFlagTests
{
    /// <summary>Exit code the stub wizard returns, so a wizard run is visible.</summary>
    private const int WizardExitCode = 42;

    private static (RootCommand Root, string TempDir, Func<bool> WizardCalled) CreateRoot()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-version-test-{Guid.NewGuid():N}");
        string settingsDir = Path.Combine(tempDir, "settings");
        Directory.CreateDirectory(settingsDir);
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(backupsDir);

        SettingsStore settingsStore = new SettingsStore(settingsDir, new FakeSecureKeyStore());
        BackupService backupService = new BackupService(backupsDir);
        LaunchOrchestrator orchestrator = new LaunchOrchestrator(
            new LaunchFlow(
                settingsStore,
                backupService,
                new RefuseToLaunchRunner(),
                [],
                new FakeSecureKeyStore(),
                new FakeGuiInstallDiscovery(),
                new FakeRunningInstanceDetector(),
                new FakeConfirmationPrompt(),
                new ProtocolEmissionPlanner()));

        bool wizardCalled = false;
        RootCommand root = RootCommandFactory.Create(
            orchestrator,
            _ =>
            {
                wizardCalled = true;
                return Task.FromResult(WizardExitCode);
            },
            [],
            settingsStore,
            new FakeSecureKeyStore());

        return (root, tempDir, () => wizardCalled);
    }

    [Test]
    public async Task Version_flag_prints_the_assembly_informational_version_and_exits_zero()
    {
        (RootCommand root, string tempDir, Func<bool> wizardCalled) = CreateRoot();
        try
        {
            (int exitCode, string output) = await ConsoleOutputCapture.RunAsync(
                () => Task.FromResult(root.Parse("--version").Invoke()));

            // Independent oracle: the attribute the build stamped on the
            // assembly from the csproj <Version>, not the value the factory
            // exposes. Exact equality, not Contains - if the output carried
            // anything else (settings schemaVersion, wizard text), this fails.
            string expected = typeof(RootCommandFactory).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output.Trim()).IsEqualTo(expected);
            await Assert.That(wizardCalled()).IsFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task Informational_version_carries_the_pinned_version()
    {
        // The csproj pin is 0.1.0 and it is the whole string: SourceLink's
        // "+<sha>" suffix is switched off by
        // IncludeSourceRevisionInInformationalVersion=false, so exact equality
        // (not StartsWith) is the contract - any build metadata creeping back
        // onto the flag's output fails here.
        await Assert.That(RootCommandFactory.InformationalVersion).IsEqualTo("0.1.0");
    }

    [Test]
    public async Task Version_flag_reports_not_the_settings_schema_version()
    {
        // Non-conflation guard: schemaVersion describes the settings
        // file's shape and belongs to `config get`; the assembly version is
        // the build's identity. They must never converge.
        string schemaVersion = LauncherSettings.CurrentSchemaVersion.ToString();
        await Assert.That(RootCommandFactory.InformationalVersion).IsNotEqualTo(schemaVersion);
    }

    [Test]
    public async Task Bare_invocation_still_runs_the_wizard()
    {
        // Regression guard for the wiring this suite now owns: moving the root
        // action into RootCommandFactory must not lose the no-args wizard
        // entry point that `--version` short-circuits.
        (RootCommand root, string tempDir, Func<bool> wizardCalled) = CreateRoot();
        try
        {
            int previous = ConsoleErrorCapture.Depth.Value;
            bool ownsGate = previous == 0;
            ConsoleErrorCapture.Depth.Value = previous + 1;
            if (ownsGate)
            {
                ConsoleErrorCapture.Gate.Wait();
            }
            int exitCode;
            try
            {
                exitCode = root.Parse(Array.Empty<string>()).Invoke();
            }
            finally
            {
                if (ownsGate)
                {
                    ConsoleErrorCapture.Gate.Release();
                }
                ConsoleErrorCapture.Depth.Value = previous;
            }

            await Assert.That(exitCode).IsEqualTo(WizardExitCode);
            await Assert.That(wizardCalled()).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private sealed class RefuseToLaunchRunner : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The version flag must never reach the launch runner.");
        }

        public Task<int> RunWithoutTimeoutAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
        {
            return RunAsync(plan, cancellationToken);
        }

        public int Handoff(LaunchPlan plan)
        {
            throw new InvalidOperationException("The version flag must never reach the launch runner.");
        }
    }
}
