using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Tests.Fakes;
using ModelsDotDevSharp;

namespace HarnessGateway.Launcher.Tests.Launch;

/// <summary>
/// End-to-end handoff: a real child process spawned by the real
/// <see cref="LaunchRunner"/> through <see cref="LaunchFlow"/>. The child is a
/// local file-based app that dumps the environment and argv it actually
/// received; catalog access goes through the fake repository, so no real
/// harness and no network are involved. These assertions land on the process
/// the feature is for, not on a plan object.
/// </summary>
public class HandoffEndToEndTests
{
    private static readonly HashSet<ProtocolKind> AllProtocols =
    [
        ProtocolKind.OpenAiChat,
        ProtocolKind.OpenAiResponses,
        ProtocolKind.Anthropic,
    ];

    // Case-insensitive on Windows, where the process environment is too.
    private static readonly StringComparer EnvironmentNameComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string AmbientName(string name) =>
        OperatingSystem.IsWindows() ? name.ToLowerInvariant() : name;

    private static AICatalog Catalog(params (string ProviderId, string[] ModelIds)[] providers)
    {
        AICatalog catalog = new();
        foreach ((string providerId, string[] modelIds) in providers)
        {
            catalog.Providers[providerId] = new AIProviderInfo
            {
                Id = providerId,
                Name = providerId,
                Models = [.. modelIds.Select(id => new AIModelInfo { Id = id, Name = id })],
            };
        }
        return catalog;
    }

    private static (LaunchFlow Flow, string TempDir, string DumpPath) CreateFlow(
        AICatalog? catalog = null)
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(), $"HarnessGateway-handoff-{Guid.NewGuid():N}");
        string settingsDir = Path.Combine(tempDir, "settings");
        string backupsDir = Path.Combine(tempDir, "backups");
        Directory.CreateDirectory(settingsDir);
        Directory.CreateDirectory(backupsDir);

        FakeSecureKeyStore keyStore = new();
        SettingsStore settingsStore = new(settingsDir, keyStore);
        settingsStore.Save(new LauncherSettings
        {
            Providers =
            [
                new Provider
                {
                    Name = "stub-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    DefaultModel = "gpt-4o",
                },
            ],
            DefaultProvider = "stub-backend",
        });

        string dumpPath = Path.Combine(tempDir, "stub-dump.txt");
        StubHarnessAdapter adapter = new(dumpPath);
        LaunchFlow flow = new(
            settingsStore,
            new BackupService(backupsDir),
            new LaunchRunner(),
            [adapter],
            keyStore,
            new FakeGuiInstallDiscovery(),
            new FakeRunningInstanceDetector(),
            new FakeConfirmationPrompt(),
            new ProtocolEmissionPlanner(),
            new FakeTerminalProbe(),
            QualificationDeps.Cache(tempDir, catalog),
            QualificationDeps.Qualifier,
            new ScriptedModelChoicePrompt(),
            new ApiKeyScrubList());
        return (flow, tempDir, dumpPath);
    }

    private static LaunchRequest Request(string model, bool cleanEnv = false, bool nonInteractive = false) =>
        new(
            "stub",
            ProviderName: null,
            Endpoint: null,
            Protocol: null,
            Model: model,
            Persist: [],
            SessionKey: null,
            Print: true,
            PrintPrompt: "hello from the handoff test",
            NonInteractive: nonInteractive,
            CleanEnv: cleanEnv);

    // ZAI_API_KEY is on the scrub list but is not one of the protocol keys the
    // wizard reads from this process's environment, so seeding it cannot race
    // with the wizard's key-step tests. The other listed names are seeded on
    // the plan instead (see the adapter), so they exercise the preservation
    // side of the scrub without touching process-global state they depend on.
    private const string AmbientScrubbedName = "ZAI_API_KEY";

    /// <summary>
    /// Seeds one ambient variable for the child to inherit. On Windows the
    /// seed uses the lowercase spelling: the scrub must match it even though
    /// the list is uppercase.
    /// </summary>
    private static string? SeedAmbient()
    {
        string seeded = AmbientName(AmbientScrubbedName);
        string? previous = Environment.GetEnvironmentVariable(seeded);
        Environment.SetEnvironmentVariable(seeded, "ambient-secret");
        return previous;
    }

    private static void RestoreAmbient(string? previous) =>
        Environment.SetEnvironmentVariable(AmbientName(AmbientScrubbedName), previous);

    [Test]
    // The ambient environment is process-global: share this key with the
    // other tests that mutate it so they never run concurrently.
    [NotInParallel("process-environment")]
    public async Task Clean_env_removes_ambient_listed_variables_but_keeps_values_the_plan_supplies()
    {
        IReadOnlyCollection<string> names = new ApiKeyScrubList().VariableNames;
        string? previous = SeedAmbient();
        string tempDir = string.Empty;
        try
        {
            (LaunchFlow flow, tempDir, string dumpPath) = CreateFlow(
                Catalog(("openai", ["gpt-4o"])));

            LaunchOutcome outcome = await flow.ExecuteAsync(Request("gpt-4o", cleanEnv: true));

            await Assert.That(outcome is LaunchCompleted).IsTrue();
            ChildDump dump = ChildDump.Read(dumpPath);

            // The ambient listed variable (ZAI_API_KEY) is removed from the
            // child, while the values the plan itself supplied - the launch's
            // own protocol keys among them - survive, so the chosen provider
            // keeps the credential this launch resolved for it.
            await Assert.That(dump.HasEnvironmentVariable(AmbientScrubbedName)).IsFalse();
            await Assert.That(dump.ValueOf("OPENAI_API_KEY")).IsEqualTo("from-plan");
            await Assert.That(dump.ValueOf("ANTHROPIC_API_KEY")).IsEqualTo("from-plan");
            await Assert.That(names).Contains(AmbientScrubbedName);
            await Assert.That(names).Contains("OPENAI_API_KEY");
            await Assert.That(names).Contains("ANTHROPIC_API_KEY");

            // Unlisted variables, including one put there by the plan itself,
            // survive: the scrub is a closed list, not a wholesale clear.
            await Assert.That(dump.ValueOf("HGW_STUB_SENTINEL")).IsEqualTo("from-plan");
            await Assert.That(dump.ValueOf("OPENAI_BASE_URL")).IsEqualTo("https://api.example.com/v1");

            // The scrub is scoped to the child: this process still has the
            // ambient value it had before the launch.
            await Assert.That(Environment.GetEnvironmentVariable(AmbientName(AmbientScrubbedName)))
                .IsEqualTo("ambient-secret");
        }
        finally
        {
            RestoreAmbient(previous);
            if (tempDir.Length > 0)
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    [NotInParallel("process-environment")]
    public async Task Without_clean_env_the_child_inherits_everything()
    {
        IReadOnlyCollection<string> names = new ApiKeyScrubList().VariableNames;
        string? previous = SeedAmbient();
        string tempDir = string.Empty;
        try
        {
            (LaunchFlow flow, tempDir, string dumpPath) = CreateFlow(
                Catalog(("openai", ["gpt-4o"])));

            LaunchOutcome outcome = await flow.ExecuteAsync(Request("gpt-4o"));

            await Assert.That(outcome is LaunchCompleted).IsTrue();
            ChildDump dump = ChildDump.Read(dumpPath);
            foreach (string name in names)
            {
                await Assert.That(dump.HasEnvironmentVariable(name)).IsTrue();
            }
            await Assert.That(dump.ValueOf(AmbientScrubbedName)).IsEqualTo("ambient-secret");
            await Assert.That(dump.ValueOf("HGW_STUB_SENTINEL")).IsEqualTo("from-plan");
        }
        finally
        {
            RestoreAmbient(previous);
            if (tempDir.Length > 0)
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    // Every test here builds the same file-based app into one cache directory,
    // so the spawns (like the ambient environment) must be serialized.
    [NotInParallel("process-environment")]
    public async Task A_qualified_model_reaches_the_child_as_provider_model()
    {
        (LaunchFlow flow, string tempDir, string dumpPath) = CreateFlow(
            Catalog(("openai", ["gpt-4o"])));
        try
        {
            LaunchOutcome outcome = await flow.ExecuteAsync(Request("gpt-4o"));

            await Assert.That(outcome is LaunchCompleted).IsTrue();
            ChildDump dump = ChildDump.Read(dumpPath);
            await Assert.That(dump.Arguments).Contains("--model");
            await Assert.That(dump.Arguments).Contains("openai/gpt-4o");
            await Assert.That(dump.Arguments).DoesNotContain("gpt-4o");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [NotInParallel("process-environment")]
    public async Task A_zero_match_warning_passes_the_bare_name_through()
    {
        (LaunchFlow flow, string tempDir, string dumpPath) = CreateFlow(
            Catalog(("openai", ["gpt-4o"])));
        try
        {
            (LaunchOutcome outcome, string error) = await ConsoleErrorCapture.RunAsync(
                () => flow.ExecuteAsync(Request("local-model")));

            await Assert.That(outcome is LaunchCompleted).IsTrue();
            await Assert.That(error).Contains("not in the models.dev catalog");
            ChildDump dump = ChildDump.Read(dumpPath);
            await Assert.That(dump.Arguments).Contains("--model");
            await Assert.That(dump.Arguments).Contains("local-model");
            await Assert.That(dump.Arguments).DoesNotContain("openai/local-model");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>The stub's dump, parsed with the platform's name-casing rule.</summary>
    private sealed class ChildDump
    {
        private readonly Dictionary<string, string> _environment;
        private readonly List<string> _arguments;

        private ChildDump(List<string> arguments, Dictionary<string, string> environment)
        {
            _arguments = arguments;
            _environment = environment;
        }

        public IReadOnlyList<string> Arguments => _arguments;

        public bool HasEnvironmentVariable(string name) => _environment.ContainsKey(name);

        public string? ValueOf(string name) =>
            _environment.TryGetValue(name, out string? value) ? value : null;

        public static ChildDump Read(string path)
        {
            List<string> arguments = [];
            Dictionary<string, string> environment = new(EnvironmentNameComparer);
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("ARG:", StringComparison.Ordinal))
                {
                    arguments.Add(line[4..]);
                }
                else if (line.StartsWith("ENV:", StringComparison.Ordinal))
                {
                    int separator = line.IndexOf('=');
                    if (separator > 4)
                    {
                        environment[line[4..separator]] = line[(separator + 1)..];
                    }
                }
            }
            return new ChildDump(arguments, environment);
        }
    }

    /// <summary>
    /// A local stand-in for a CLI harness with pi's own-catalog capability:
    /// it plans the real runner's child process and nothing else.
    /// </summary>
    private sealed class StubHarnessAdapter(string dumpPath) : IHarnessAdapter, INonInteractiveLaunchAdapter
    {
        private static readonly string StubPath = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "StubHarness.cs");

        public HarnessMetadata Metadata { get; } = new(
            Name: "Stub Harness",
            CommandName: "stub",
            [],
            HarnessKind.Cli,
            RequiresModel: false,
            InstallHint: "<install hint>",
            DetectionCommand: "stub --version",
            [],
            AllProtocols,
            new HashSet<ConfigurationSurface>(),
            [],
            ResolvesModelsInOwnCatalog: true);

        public Task<AvailabilityResult> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AvailabilityResult.Found);

        public Task<LaunchPlan> PlanLaunchAsync(
            ResolvedLaunch request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plan(request, printPrompt: null));

        public Task<LaunchPlan> PlanNonInteractiveAsync(
            ResolvedLaunch request, string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plan(request, prompt));

        private LaunchPlan Plan(ResolvedLaunch request, string? printPrompt)
        {
            (ProtocolEmission emission, string? model) = request.Launch switch
            {
                LaunchResolution.ProviderCase stored =>
                    (request.EmissionPlanner.Plan(stored.Protocol, stored.BaseUri), stored.Model),
                LaunchResolution.AdHocCase adHoc =>
                    (request.EmissionPlanner.Plan(adHoc.Protocol, adHoc.BaseUri), adHoc.Model),
                _ => throw new InvalidOperationException(
                    $"unexpected launch resolution case '{request.Launch.GetType().Name}'"),
            };

            Dictionary<string, string?> environment = new(StringComparer.Ordinal);
            EmissionMerge.MergeInto(environment, emission, request.ApiKey);
            // Values the plan itself supplies for two more listed names, so
            // the scrub's preservation side is proved at the child as well as
            // the ambient removal. ZAI_API_KEY is deliberately absent here:
            // the test seeds it in the launcher process so the child inherits
            // it instead, and that is the name the scrub removes.
            environment["OPENAI_API_KEY"] = "from-plan";
            environment["ANTHROPIC_API_KEY"] = "from-plan";
            environment["HGW_STUB_SENTINEL"] = "from-plan";

            List<string> arguments =
            [
                "run", "--file", StubPath, "--", dumpPath,
            ];
            if (model is { Length: > 0 })
            {
                arguments.Add("--model");
                arguments.Add(model);
            }
            if (printPrompt is not null)
            {
                arguments.Add(printPrompt);
            }

            ProcessConfiguration process = new("dotnet")
            {
                ArgumentList = arguments,
                WorkingDirectoryPath = request.WorkingDirectory,
                OutputRedirection = true,
                EnvironmentVariables = environment,
            };
            return new LaunchPlan(process, []);
        }
    }
}
