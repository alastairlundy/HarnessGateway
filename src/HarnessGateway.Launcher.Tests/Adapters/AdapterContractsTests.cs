using HarnessGateway.Launcher.Core;
using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Adapters.Helpers;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class HarnessMetadataTests
{
    [Test]
    public async Task HarnessMetadata_stores_all_fields()
    {
        HarnessMetadata metadata = new HarnessMetadata(
            Name: "opencode",
            CommandName: "opencode",
            ["oc"],
            Kind: HarnessKind.Cli,
            RequiresModel: true,
            InstallHint: "Install via: npm install -g @anthropic-ai/opencode",
            DetectionCommand: "opencode --version",
            [".opencode/config.json", "~/.config/opencode/config.json"],
            new HashSet<ProtocolKind>
            {
                ProtocolKind.OpenAiChat
            },
            new HashSet<ConfigurationSurface>(),
            []
        );

        await Assert.That(metadata.Name).IsEqualTo("opencode");
        await Assert.That(metadata.CommandName).IsEqualTo("opencode");
        await Assert.That(metadata.Aliases).Count().IsEqualTo(1);
        await Assert.That(metadata.AcceptedTokens).IsEquivalentTo(["opencode", "oc"]);
        await Assert.That(metadata.Kind).IsEqualTo(HarnessKind.Cli);
        await Assert.That(metadata.RequiresModel).IsTrue();
        await Assert.That(metadata.InstallHint).IsEqualTo("Install via: npm install -g @anthropic-ai/opencode");
        await Assert.That(metadata.DetectionCommand).IsEqualTo("opencode --version");
        await Assert.That(metadata.ConfigFileCandidates).Count().IsEqualTo(2);
        await Assert.That(metadata.SupportedProtocols)
            .IsEquivalentTo([ProtocolKind.OpenAiChat]);
    }

    [Test]
    public async Task HarnessMetadata_stores_detection_command_with_no_aliases()
    {
        HarnessMetadata metadata = new HarnessMetadata(
            Name: "kilo",
            CommandName: "kilo",
            [],
            Kind: HarnessKind.Cli,
            RequiresModel: false,
            InstallHint: "Install from kilo.dev",
            DetectionCommand: "kilo --version",
            [],
            new HashSet<ProtocolKind>(),
            new HashSet<ConfigurationSurface>(),
            []
        );

        await Assert.That(metadata.DetectionCommand).IsEqualTo("kilo --version");
        await Assert.That(metadata.ConfigFileCandidates).IsEmpty();
        await Assert.That(metadata.SupportedProtocols).IsEmpty();

        // An empty alias list still yields the command name as the sole
        // accepted token, so lookup never has to special-case "no aliases".
        await Assert.That(metadata.AcceptedTokens).IsEquivalentTo(["kilo"]);
    }

    [Test]
    public async Task HarnessMetadata_declares_only_the_protocols_it_is_given()
    {
        HarnessMetadata metadata = new HarnessMetadata(
            Name: "Claude Code",
            CommandName: "claude-code",
            [],
            Kind: HarnessKind.Cli,
            RequiresModel: false,
            InstallHint: "Install via: npm i -g @anthropic-ai/claude-code",
            DetectionCommand: "claude --version",
            [],
            new HashSet<ProtocolKind>
            {
                ProtocolKind.Anthropic
            },
            new HashSet<ConfigurationSurface>(),
            []
        );

        await Assert.That(metadata.SupportedProtocols).Contains(ProtocolKind.Anthropic);
        await Assert.That(metadata.SupportedProtocols)
            .DoesNotContain(ProtocolKind.OpenAiChat);
        await Assert.That(metadata.SupportedProtocols)
            .DoesNotContain(ProtocolKind.OpenAiResponses);
    }

    [Test]
    public async Task HarnessKind_has_cli_and_gui_values()
    {
        await Assert.That(HarnessKind.Cli.ToString()).IsEqualTo("Cli");
        await Assert.That(HarnessKind.Gui.ToString()).IsEqualTo("Gui");
    }
}

public class ShippedAdapterContractTests
{
    // Mirrors the adapter registry inline at Program.cs (the same seven
    // parameterless constructions, in the same order). Program.cs has no
    // shared factory, so an eighth adapter added there will not fail this
    // test automatically - keep both lists in sync.
    private static IHarnessAdapter[] ShippedAdapters() =>
    [
        new OpenCodeAdapter(),
        new OpenCodeDesktopAdapter(),
        new KiloAdapter(),
        new CopilotCliAdapter(),
        new PiAdapter(),
        new CodexAdapter(),
        new ClaudeCodeAdapter()
    ];

    [Test]
    public async Task Shipped_adapters_declare_expected_model_requirements()
    {
        IHarnessAdapter[] shipped = ShippedAdapters();

        Dictionary<string, bool> expected = new()
        {
            ["OpenCode"] = true,
            ["OpenCode Desktop"] = true,
            ["Kilo"] = true,
            ["GitHub Copilot CLI"] = true,
            ["Pi"] = false,
            ["Codex CLI"] = true,
            ["Claude Code"] = false
        };

        string[] names = [.. shipped.Select(adapter => adapter.Metadata.Name)];

        await Assert.That(names.Length).IsEqualTo(expected.Count);
        foreach (string name in expected.Keys)
        {
            await Assert.That(names.Contains(name)).IsTrue();
        }

        // RequiresModel is a non-nullable bool, so "no declaration" is
        // compile-time unrepresentable; this pins the per-harness values so
        // an adapter flip fails the gate.
        foreach (IHarnessAdapter adapter in shipped)
        {
            await Assert.That(adapter.Metadata.RequiresModel)
                .IsEqualTo(expected[adapter.Metadata.Name]);
        }
    }

    [Test]
    public async Task Shipped_adapters_declare_expected_non_interactive_support()
    {
        IHarnessAdapter[] shipped = ShippedAdapters();

        // The -p/--print capability map: the six harnesses whose one-shot
        // spelling is researched and implemented (claude -p, opencode run,
        // kilo run, codex exec, copilot -p, pi -p), and the one that has
        // none - the GUI app has no CLI one-shot mode, so it must refuse
        // rather than guess at argv.
        Dictionary<string, bool> expected = new()
        {
            ["OpenCode"] = true,
            ["OpenCode Desktop"] = false,
            ["Kilo"] = true,
            ["GitHub Copilot CLI"] = true,
            ["Pi"] = true,
            ["Codex CLI"] = true,
            ["Claude Code"] = true
        };

        foreach (IHarnessAdapter adapter in shipped)
        {
            await Assert.That(adapter is INonInteractiveLaunchAdapter)
                .IsEqualTo(expected[adapter.Metadata.Name]);
        }
    }

    [Test]
    public async Task Shipped_adapters_declare_unique_command_names()
    {
        IHarnessAdapter[] shipped = ShippedAdapters();

        // A duplicate command name would make `launch <token>` resolve to
        // whichever adapter happens to be first in Program.cs's list, which is
        // a silent wrong-harness launch rather than a visible failure.
        string[] commandNames = [.. shipped.Select(a => a.Metadata.CommandName)];

        await Assert.That(commandNames.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            .IsEqualTo(commandNames.Length);
    }

    [Test]
    public async Task Shipped_adapters_resolve_their_own_command_names()
    {
        IHarnessAdapter[] shipped = ShippedAdapters();

        // The property the help text depends on: every token the launcher
        // advertises must actually resolve. This is the check that would have
        // caught the original defect, where help advertised "copilot" and
        // lookup only matched "GitHub Copilot CLI".
        foreach (IHarnessAdapter adapter in shipped)
        {
            Result<IHarnessAdapter> resolved = HarnessLookup.TryResolve(shipped, adapter.Metadata.CommandName);
            await Assert.That(resolved.Success).IsTrue();
            await Assert.That(resolved.Value).IsSameReferenceAs(adapter);
        }
    }

    [Test]
    public async Task Copilot_resolves_its_command_name_but_not_its_display_name()
    {
        IHarnessAdapter[] shipped = ShippedAdapters();

        // Compare against the instance inside `shipped`, not a fresh
        // construction: TryResolve hands back the registered adapter, so
        // reference identity is the thing under test.
        CopilotCliAdapter copilot = shipped.OfType<CopilotCliAdapter>().Single();

        // Display name is a label, not a token. `launch "GitHub Copilot CLI"`
        // failing is the intended contract, not an oversight.
        await Assert.That(HarnessLookup.TryResolve(shipped, "copilot").Value).IsSameReferenceAs(copilot);
        await Assert.That(HarnessLookup.TryResolve(shipped, "COPILOT").Value).IsSameReferenceAs(copilot);
        await Assert.That(HarnessLookup.TryResolve(shipped, "github-copilot-cli").Value)
            .IsSameReferenceAs(copilot);
        await Assert.That(HarnessLookup.TryResolve(shipped, "GitHub Copilot CLI").Success).IsFalse();
    }
}

/// <summary>
/// The GUI members of <c>HarnessMetadata</c>, per Kind: Kind=Cli adapters
/// pass both GUI members empty - the CLI path configures the way it always
/// has - and the shipped Kind=Gui adapter declares the researched values the
/// flow's discovery, detection, surface gating, and refusal prose depend on.
/// </summary>
public class GuiAdapterContractTests
{
    private static IHarnessAdapter[] ShippedAdapters() =>
    [
        new OpenCodeAdapter(),
        new OpenCodeDesktopAdapter(),
        new KiloAdapter(),
        new CopilotCliAdapter(),
        new PiAdapter(),
        new CodexAdapter(),
        new ClaudeCodeAdapter()
    ];

    [Test]
    public async Task Cli_adapters_declare_no_gui_members()
    {
        foreach (IHarnessAdapter adapter in ShippedAdapters().Where(a => a.Metadata.Kind == HarnessKind.Cli))
        {
            // Empty, not absent: discovery and detection read these members
            // unconditionally, so a CLI adapter passing empties keeps it
            // out of both mechanisms rather than a null special
            // case somewhere in the flow.
            await Assert.That(adapter.Metadata.SupportedSurfaces).IsEmpty();
            await Assert.That(adapter.Metadata.InstanceProcessNames).IsEmpty();
        }
    }

    [Test]
    public async Task Exactly_one_gui_adapter_ships_today()
    {
        // GUI support shipped with OpenCode Desktop as its first entry; a
        // second GUI harness is a deliberate registry change, and this pin
        // makes adding one a conscious edit rather than an accident.
        IHarnessAdapter[] guiAdapters = [.. ShippedAdapters().Where(a => a.Metadata.Kind == HarnessKind.Gui)];

        await Assert.That(guiAdapters.Length).IsEqualTo(1);
        await Assert.That(guiAdapters[0].Metadata.CommandName).IsEqualTo("opencode-desktop");
    }

    [Test]
    public async Task Opencode_desktop_declares_its_researched_gui_members()
    {
        IHarnessAdapter adapter = ShippedAdapters().OfType<OpenCodeDesktopAdapter>().Single();
        HarnessMetadata metadata = adapter.Metadata;

        await Assert.That(metadata.Kind).IsEqualTo(HarnessKind.Gui);
        await Assert.That(metadata.CommandName).IsEqualTo("opencode-desktop");

        // Environment and Files are the surfaces the desktop app's sidecar
        // actually reads; Flags is deliberately absent - the embedded server
        // parses no command line.
        await Assert.That(metadata.SupportedSurfaces)
            .IsEquivalentTo([ConfigurationSurface.Environment, ConfigurationSurface.Files]);

        // Declared-name order is discovery's first-match order.
        await Assert.That(metadata.InstanceProcessNames).IsEquivalentTo(
            ["OpenCode.exe", "OpenCode.app", "OpenCode", "ai.opencode.desktop"]);

        // The unavailable refusal quotes the install hint verbatim, so it
        // must carry the real install story, not a placeholder.
        await Assert.That(metadata.InstallHint).Contains("brew install --cask opencode-desktop");
        await Assert.That(metadata.InstallHint).Contains("https://opencode.ai/download");

        // The protocol vocabulary mirrors the CLI sibling - the two speak
        // the same backends through different surfaces.
        await Assert.That(metadata.SupportedProtocols)
            .IsEquivalentTo([ProtocolKind.OpenAiChat, ProtocolKind.OpenAiResponses, ProtocolKind.Anthropic]);
    }
}

public class ConfigurationActionTests
{
    [Test]
    public async Task CliArgumentAction_stores_argument_and_value()
    {
        CliArgumentAction action = new CliArgumentAction("--model", "gpt-4");

        await Assert.That(action.Argument).IsEqualTo("--model");
        await Assert.That(action.Value).IsEqualTo("gpt-4");
        await Assert.That(action).IsTypeOf<CliArgumentAction>();
    }

    [Test]
    public async Task CliArgumentAction_allows_empty_value()
    {
        CliArgumentAction action = new CliArgumentAction("--verbose", string.Empty);

        await Assert.That(action.Argument).IsEqualTo("--verbose");
        await Assert.That(action.Value).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ConfigFileWriteAction_stores_path_and_content()
    {
        ConfigFileWriteAction action = new ConfigFileWriteAction("/path/to/config.json", "{ \"key\": \"value\" }");

        await Assert.That(action.TargetPath).IsEqualTo("/path/to/config.json");
        await Assert.That(action.Content).IsEqualTo("{ \"key\": \"value\" }");
        await Assert.That(action).IsTypeOf<ConfigFileWriteAction>();
    }

    [Test]
    public async Task ConfigurationAction_is_abstract_base()
    {
        // The closed family is exactly CliArgumentAction and ConfigFileWriteAction;
        // env vars go on LaunchPlan.Process.EnvironmentVariables.
        ConfigurationAction cli = new CliArgumentAction("--flag", string.Empty);
        ConfigurationAction file = new ConfigFileWriteAction("/tmp", "content");

        await Assert.That(cli).IsTypeOf<CliArgumentAction>();
        await Assert.That(file).IsTypeOf<ConfigFileWriteAction>();
    }
}

public class ResolvedLaunchTests
{
    [Test]
    public async Task ResolvedLaunch_carries_the_case_key_working_directory_and_planner()
    {
        Provider provider = new Provider
        {
            Name = "test",
            BaseUri = new Uri("https://api.example.com"),
            DefaultProtocol = ProtocolKind.OpenAiChat
        };
        LaunchResolution.ProviderCase launch = new(
            provider, "gpt-4", ProtocolKind.OpenAiChat);
        IProtocolEmissionPlanner planner = new ProtocolEmissionPlanner();

        ResolvedLaunch request = new ResolvedLaunch(
            launch, "sk-test", "/home/user/project", planner);

        // The family passes straight through - no synthesized stored-looking provider
        // between the resolution and the adapter.
        await Assert.That(request.Launch).IsSameReferenceAs(launch);
        // The launch-scoped secret, never the at-rest key reference.
        await Assert.That(request.ApiKey).IsEqualTo("sk-test");
        await Assert.That(request.WorkingDirectory).IsEqualTo("/home/user/project");
        // The planner arrives through the payload, wired at the composition
        // root - never reached through statics.
        await Assert.That(request.EmissionPlanner).IsSameReferenceAs(planner);
    }

    [Test]
    public async Task ResolvedLaunch_allows_a_keyless_ad_hoc_launch()
    {
        LaunchResolution.AdHocCase launch = new(
            new Uri("https://api.example.com"),
            ProtocolKind.OpenAiChat,
            model: null);

        ResolvedLaunch request = new ResolvedLaunch(
            launch, ApiKey: null, "/tmp", new ProtocolEmissionPlanner());

        await Assert.That(request.Launch).IsEqualTo(launch);
        await Assert.That(request.ApiKey).IsNull();
    }
}
