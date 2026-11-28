using CliInvoke.Core;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Execution;
using HarnessGateway.Launcher.Core.Launch;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Settings.Security;
using HarnessGateway.Launcher.Tests.Fakes;
using ModelsDotDevSharp;

namespace HarnessGateway.Launcher.Tests.Launch;

/// <summary>
/// Flow-level coverage for the qualification handoff: a unique match reaches
/// the adapter as a qualified reference while the bare name is what gets
/// persisted, an unlisted name passes through with the catalog warning, and
/// ambiguity either picks in the terminal or refuses with matches and a docs
/// pointer. The harness here opts into the own-catalog capability like pi;
/// nothing in this suite reaches the network or the real catalog cache.
/// </summary>
public class LaunchFlowQualificationTests
{
    private static readonly HashSet<ProtocolKind> AllProtocols =
    [
        ProtocolKind.OpenAiChat,
        ProtocolKind.OpenAiResponses,
        ProtocolKind.Anthropic,
    ];

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

    private static (LaunchFlow Flow, string TempDir, QualifyingAdapter Adapter) CreateFlow(
        AICatalog catalog,
        IModelChoicePrompt picker,
        ITerminalProbe? terminalProbe = null,
        IReadOnlyList<ModelEntry>? listedModels = null,
        bool capability = true)
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(), $"HarnessGateway-qualification-{Guid.NewGuid():N}");
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
                    Name = "catalog-backend",
                    BaseUri = new Uri("https://api.example.com/v1"),
                    DefaultProtocol = ProtocolKind.OpenAiChat,
                    DefaultModel = "gpt-4o",
                    Models = (listedModels ?? []).ToList(),
                },
            ],
            DefaultProvider = "catalog-backend",
        });

        QualifyingAdapter adapter = new(capability);
        LaunchFlow flow = new(
            settingsStore,
            new BackupService(backupsDir),
            new TestLaunchRunner(),
            [adapter],
            keyStore,
            new FakeGuiInstallDiscovery(),
            new FakeRunningInstanceDetector(),
            new FakeConfirmationPrompt(),
            new ProtocolEmissionPlanner(),
            terminalProbe ?? new FakeTerminalProbe(),
            QualificationDeps.Cache(tempDir, catalog),
            QualificationDeps.Qualifier,
            picker,
            new ApiKeyScrubList());
        return (flow, tempDir, adapter);
    }

    private static LaunchRequest Request(string model, bool nonInteractive = false, bool print = false) =>
        new(
            "pi",
            ProviderName: null,
            Endpoint: null,
            Protocol: null,
            Model: model,
            Persist: print ? [] : ["model"],
            SessionKey: null,
            Print: print,
            PrintPrompt: print ? "hello" : null,
            NonInteractive: nonInteractive);

    [Test]
    public async Task Unique_match_reaches_the_adapter_qualified_while_the_bare_name_is_persisted()
    {
        AICatalog catalog = Catalog(("openai", ["gpt-4o"]));
        ScriptedModelChoicePrompt picker = new();
        (LaunchFlow flow, string tempDir, QualifyingAdapter adapter) =
            CreateFlow(catalog, picker);

        LaunchOutcome outcome = await flow.ExecuteAsync(Request("gpt-4o"));

        await Assert.That(outcome is LaunchCompleted).IsTrue();
        await Assert.That(ModelOf(adapter.LastLaunch!.Launch)).IsEqualTo("openai/gpt-4o");
        await Assert.That(picker.CallCount).IsEqualTo(0);

        // Persisting the qualified reference would poison the unchanged
        // unlisted-model guard on the next launch; the bare name is saved.
        SettingsStore settingsStore = new(
            Path.Combine(tempDir, "settings"), new FakeSecureKeyStore());
        LauncherSettings saved = await settingsStore.LoadAsync();
        await Assert.That(saved.Providers[0].DefaultModel).IsEqualTo("gpt-4o");
    }

    [Test]
    public async Task Zero_match_passes_the_bare_name_through_with_a_catalog_warning()
    {
        AICatalog catalog = Catalog(("openai", ["gpt-4o"]));
        ScriptedModelChoicePrompt picker = new();
        (LaunchFlow flow, _, QualifyingAdapter adapter) = CreateFlow(catalog, picker);

        (LaunchOutcome outcome, string error) = await ConsoleErrorCapture.RunAsync(
            () => flow.ExecuteAsync(Request("local-model")));

        await Assert.That(outcome is LaunchCompleted).IsTrue();
        await Assert.That(ModelOf(adapter.LastLaunch!.Launch)).IsEqualTo("local-model");
        await Assert.That(error).Contains("not in the models.dev catalog");
        await Assert.That(error).Contains("local models do not appear");
        await Assert.That(picker.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Ambiguous_match_picks_interactively_and_hands_over_the_chosen_reference()
    {
        // Ordinal order: anthropic before openai.
        AICatalog catalog = Catalog(
            ("openai", ["gpt-4o"]),
            ("anthropic", ["gpt-4o"]));
        ScriptedModelChoicePrompt picker = new("anthropic/gpt-4o");
        (LaunchFlow flow, _, QualifyingAdapter adapter) = CreateFlow(catalog, picker);

        LaunchOutcome outcome = await flow.ExecuteAsync(Request("gpt-4o"));

        await Assert.That(outcome is LaunchCompleted).IsTrue();
        await Assert.That(picker.CallCount).IsEqualTo(1);
        await Assert.That(picker.Title).Contains("gpt-4o");
        await Assert.That(picker.Candidates).IsEquivalentTo(["anthropic/gpt-4o", "openai/gpt-4o"]);
        await Assert.That(ModelOf(adapter.LastLaunch!.Launch)).IsEqualTo("anthropic/gpt-4o");
    }

    [Test]
    public async Task Ambiguous_match_refuses_with_matches_and_a_docs_pointer_when_non_interactive()
    {
        AICatalog catalog = Catalog(
            ("openai", ["gpt-4o"]),
            ("anthropic", ["gpt-4o"]));
        // No queued answer: an accidental picker call is a loud test failure.
        ScriptedModelChoicePrompt picker = new();
        (LaunchFlow flow, _, QualifyingAdapter adapter) =
            CreateFlow(catalog, picker, terminalProbe: new FakeTerminalProbe());

        LaunchOutcome outcome = await flow.ExecuteAsync(Request("gpt-4o", nonInteractive: true));

        LaunchRefused refusal = (LaunchRefused)outcome;
        await Assert.That(refusal.Kind).IsEqualTo(LaunchRefusalKind.AmbiguousModel);
        // The flow composes the message; the orchestrator prints it and picks
        // exit 14 (pinned separately in LaunchOrchestratorTests).
        await Assert.That(refusal.Message).Contains("anthropic/gpt-4o");
        await Assert.That(refusal.Message).Contains("openai/gpt-4o");
        await Assert.That(refusal.Message).Contains(
            "https://github.com/alastairlundy/HarnessGateway#models-and-provider-disambiguation");
        await Assert.That(picker.CallCount).IsEqualTo(0);
        await Assert.That(adapter.LastLaunch).IsNull();
    }

    [Test]
    public async Task Ambiguous_match_refuses_when_no_terminal_is_attached_without_the_flag()
    {
        // Print mode needs no terminal, so it reaches qualification with the
        // flag absent: the terminal state alone must force the refusal.
        AICatalog catalog = Catalog(
            ("openai", ["gpt-4o"]),
            ("anthropic", ["gpt-4o"]));
        ScriptedModelChoicePrompt picker = new();
        (LaunchFlow flow, _, QualifyingAdapter adapter) =
            CreateFlow(catalog, picker, terminalProbe: new FakeTerminalProbe(false));

        LaunchOutcome outcome = await flow.ExecuteAsync(Request("gpt-4o", print: true));

        LaunchRefused refusal = (LaunchRefused)outcome;
        await Assert.That(refusal.Kind).IsEqualTo(LaunchRefusalKind.AmbiguousModel);
        await Assert.That(picker.CallCount).IsEqualTo(0);
        await Assert.That(adapter.LastLaunch).IsNull();
    }

    [Test]
    public async Task Adapter_without_the_capability_keeps_its_own_reference()
    {
        // OpenCode and Kilo synthesize providerId/model themselves; the flag
        // defaults off for them, so a catalog match must not rewrite it.
        AICatalog catalog = Catalog(("openai", ["gpt-4o"]));
        ScriptedModelChoicePrompt picker = new();
        (LaunchFlow flow, _, QualifyingAdapter plain) =
            CreateFlow(catalog, picker, capability: false);

        LaunchOutcome outcome = await flow.ExecuteAsync(Request("openai/gpt-4o"));

        await Assert.That(outcome is LaunchCompleted).IsTrue();
        await Assert.That(ModelOf(plain.LastLaunch!.Launch)).IsEqualTo("openai/gpt-4o");
        await Assert.That(picker.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Only_pi_opts_into_own_catalog_resolution()
    {
        // OpenCode and Kilo synthesize providerId/model themselves, so
        // qualifying their reference would rewrite it twice.
        await Assert.That(new PiAdapter().Metadata.ResolvesModelsInOwnCatalog).IsTrue();
        await Assert.That(new OpenCodeAdapter().Metadata.ResolvesModelsInOwnCatalog).IsFalse();
        await Assert.That(new KiloAdapter().Metadata.ResolvesModelsInOwnCatalog).IsFalse();
    }

    [Test]
    public async Task Unlisted_model_guard_still_refuses_before_qualification()
    {
        AICatalog catalog = Catalog(("openai", ["unlisted-model"]));
        ScriptedModelChoicePrompt picker = new();
        ModelEntry listed = new() { Name = "gpt-4o" };
        (LaunchFlow flow, _, QualifyingAdapter adapter) =
            CreateFlow(catalog, picker, listedModels: [listed]);

        LaunchOutcome outcome = await flow.ExecuteAsync(Request("unlisted-model"));

        LaunchRefused refusal = (LaunchRefused)outcome;
        await Assert.That(refusal.Kind).IsEqualTo(LaunchRefusalKind.UnlistedModel);
        await Assert.That(adapter.LastLaunch).IsNull();
        await Assert.That(picker.CallCount).IsEqualTo(0);
    }

    private static string? ModelOf(LaunchResolution launch) => launch switch
    {
        LaunchResolution.ProviderCase stored => stored.Model,
        LaunchResolution.AdHocCase adHoc => adHoc.Model,
        _ => throw new InvalidOperationException($"unexpected case '{launch.GetType().Name}'"),
    };

    /// <summary>An own-catalog adapter that records the launch it planned.</summary>
    private sealed class QualifyingAdapter(bool capability) : IHarnessAdapter, INonInteractiveLaunchAdapter
    {
        public HarnessMetadata Metadata { get; } = new(
            "pi",
            "pi",
            [],
            HarnessKind.Cli,
            RequiresModel: false,
            InstallHint: "<install hint>",
            DetectionCommand: "pi --version",
            [],
            AllProtocols,
            new HashSet<ConfigurationSurface>(),
            [],
            ResolvesModelsInOwnCatalog: capability);

        public ResolvedLaunch? LastLaunch { get; private set; }

        public Task<AvailabilityResult> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AvailabilityResult.Found);

        public Task<LaunchPlan> PlanLaunchAsync(
            ResolvedLaunch request, CancellationToken cancellationToken = default)
        {
            LastLaunch = request;
            return Task.FromResult(Plan(request));
        }

        public Task<LaunchPlan> PlanNonInteractiveAsync(
            ResolvedLaunch request, string prompt, CancellationToken cancellationToken = default)
        {
            LastLaunch = request;
            return Task.FromResult(Plan(request));
        }

        private static LaunchPlan Plan(ResolvedLaunch request) =>
            new(
                new ProcessConfiguration("test")
                {
                    WorkingDirectoryPath = request.WorkingDirectory,
                    OutputRedirection = true,
                },
                []);
    }

    private sealed class TestLaunchRunner : ILaunchRunner
    {
        public Task<int> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> RunWithoutTimeoutAsync(
            LaunchPlan plan, CancellationToken cancellationToken = default) => RunAsync(plan, cancellationToken);

        public Task<int> RunInteractiveAsync(
            LaunchPlan plan, CancellationToken cancellationToken = default) => RunAsync(plan, cancellationToken);

        public int Handoff(LaunchPlan plan) => 0;
    }
}
