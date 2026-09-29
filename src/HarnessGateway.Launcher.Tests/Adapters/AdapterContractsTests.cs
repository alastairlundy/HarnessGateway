using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Adapters.Abstractions;
using HarnessGateway.Launcher.Core.Backends;
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
            Kind: HarnessKind.Cli,
            RequiresModel: true,
            InstallHint: "Install via: npm install -g @anthropic-ai/opencode",
            DetectionCommand: "opencode --version",
            [".opencode/config.json", "~/.config/opencode/config.json"],
            SupportedProtocols: new HashSet<Core.Backends.ProtocolKind>
            {
                Core.Backends.ProtocolKind.OpenAiChat
            }
        );

        await Assert.That(metadata.Name).IsEqualTo("opencode");
        await Assert.That(metadata.Kind).IsEqualTo(HarnessKind.Cli);
        await Assert.That(metadata.RequiresModel).IsTrue();
        await Assert.That(metadata.InstallHint).IsEqualTo("Install via: npm install -g @anthropic-ai/opencode");
        await Assert.That(metadata.DetectionCommand).IsEqualTo("opencode --version");
        await Assert.That(metadata.ConfigFileCandidates).Count().IsEqualTo(2);
        await Assert.That(metadata.SupportedProtocols)
            .IsEquivalentTo([Core.Backends.ProtocolKind.OpenAiChat]);
    }

    [Test]
    public async Task HarnessMetadata_allows_null_detection_command()
    {
        HarnessMetadata metadata = new HarnessMetadata(
            Name: "kilo",
            Kind: HarnessKind.Cli,
            RequiresModel: false,
            InstallHint: "Install from kilo.dev",
            DetectionCommand: null,
            [],
            new HashSet<Core.Backends.ProtocolKind>()
        );

        await Assert.That(metadata.DetectionCommand).IsNull();
        await Assert.That(metadata.ConfigFileCandidates).IsEmpty();
        await Assert.That(metadata.SupportedProtocols).IsEmpty();
    }

    [Test]
    public async Task HarnessMetadata_declares_only_the_protocols_it_is_given()
    {
        HarnessMetadata metadata = new HarnessMetadata(
            Name: "claude-code",
            Kind: HarnessKind.Cli,
            RequiresModel: false,
            InstallHint: "Install via: npm i -g @anthropic-ai/claude-code",
            DetectionCommand: "claude --version",
            [],
            new HashSet<Core.Backends.ProtocolKind>
            {
                Core.Backends.ProtocolKind.Anthropic
            }
        );

        await Assert.That(metadata.SupportedProtocols).Contains(Core.Backends.ProtocolKind.Anthropic);
        await Assert.That(metadata.SupportedProtocols)
            .DoesNotContain(Core.Backends.ProtocolKind.OpenAiChat);
        await Assert.That(metadata.SupportedProtocols)
            .DoesNotContain(Core.Backends.ProtocolKind.OpenAiResponses);
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
    // Mirrors the adapter registry inline at Program.cs (the same six
    // parameterless constructions). Program.cs has no shared factory, so a
    // seventh adapter added there will not fail this test automatically -
    // keep both lists in sync.
    [Test]
    public async Task Shipped_adapters_declare_expected_model_requirements()
    {
        IHarnessAdapter[] shipped =
        [
            new OpenCodeAdapter(),
            new KiloAdapter(),
            new CopilotCliAdapter(),
            new PiAdapter(),
            new CodexAdapter(),
            new ClaudeCodeAdapter()
        ];

        Dictionary<string, bool> expected = new()
        {
            ["OpenCode"] = true,
            ["Kilo"] = true,
            ["GitHub Copilot CLI"] = true,
            ["Pi"] = false,
            ["codex"] = true,
            ["claude-code"] = false
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
    public async Task CliArgumentAction_allows_null_value()
    {
        CliArgumentAction action = new CliArgumentAction("--verbose", null);

        await Assert.That(action.Argument).IsEqualTo("--verbose");
        await Assert.That(action.Value).IsNull();
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
        // env vars ride on LaunchPlan.Process.EnvironmentVariables.
        ConfigurationAction cli = new CliArgumentAction("--flag", null);
        ConfigurationAction file = new ConfigFileWriteAction("/tmp", "content");

        await Assert.That(cli).IsTypeOf<CliArgumentAction>();
        await Assert.That(file).IsTypeOf<ConfigFileWriteAction>();
    }
}

public class LaunchRequestTests
{
    [Test]
    public async Task LaunchRequest_stores_backend_model_and_working_directory()
    {
        Backend backend = new Core.Backends.Backend
        {
            Name = "test",
            BaseUri = new Uri("https://api.example.com"),
            Protocol = Core.Backends.ProtocolKind.OpenAiChat
        };

        LaunchRequest request = new LaunchRequest(backend, "gpt-4", "/home/user/project");

        await Assert.That(request.Backend).IsEqualTo(backend);
        await Assert.That(request.Model).IsEqualTo("gpt-4");
        await Assert.That(request.WorkingDirectory).IsEqualTo("/home/user/project");
    }

    [Test]
    public async Task LaunchRequest_allows_null_model()
    {
        Backend backend = new Core.Backends.Backend
        {
            Name = "test",
            BaseUri = new Uri("https://api.example.com"),
            Protocol = Core.Backends.ProtocolKind.OpenAiChat
        };

        LaunchRequest request = new LaunchRequest(backend, null, "/tmp");

        await Assert.That(request.Model).IsNull();
    }
}
