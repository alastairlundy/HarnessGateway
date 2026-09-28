using HarnessGateway.Launcher.Core.Adapters;
using HarnessGateway.Launcher.Core.Configuration;
using HarnessGateway.Launcher.Core.Launch;

namespace HarnessGateway.Launcher.Tests.Adapters;

public class HarnessMetadataTests
{
    [Test]
    public async Task HarnessMetadata_stores_all_fields()
    {
        var metadata = new HarnessMetadata(
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
            .IsEquivalentTo(new[] { Core.Backends.ProtocolKind.OpenAiChat });
    }

    [Test]
    public async Task HarnessMetadata_allows_null_detection_command()
    {
        var metadata = new HarnessMetadata(
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
        var metadata = new HarnessMetadata(
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

public class ConfigurationActionTests
{
    [Test]
    public async Task CliArgumentAction_stores_argument_and_value()
    {
        var action = new CliArgumentAction("--model", "gpt-4");

        await Assert.That(action.Argument).IsEqualTo("--model");
        await Assert.That(action.Value).IsEqualTo("gpt-4");
        await Assert.That(action).IsTypeOf<CliArgumentAction>();
    }

    [Test]
    public async Task CliArgumentAction_allows_null_value()
    {
        var action = new CliArgumentAction("--verbose", null);

        await Assert.That(action.Argument).IsEqualTo("--verbose");
        await Assert.That(action.Value).IsNull();
    }

    [Test]
    public async Task ConfigFileWriteAction_stores_path_and_content()
    {
        var action = new ConfigFileWriteAction("/path/to/config.json", "{ \"key\": \"value\" }");

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
        var backend = new Core.Backends.Backend
        {
            Name = "test",
            BaseUri = new Uri("https://api.example.com"),
            Protocol = Core.Backends.ProtocolKind.OpenAiChat
        };

        var request = new LaunchRequest(backend, "gpt-4", "/home/user/project");

        await Assert.That(request.Backend).IsEqualTo(backend);
        await Assert.That(request.Model).IsEqualTo("gpt-4");
        await Assert.That(request.WorkingDirectory).IsEqualTo("/home/user/project");
    }

    [Test]
    public async Task LaunchRequest_allows_null_model()
    {
        var backend = new Core.Backends.Backend
        {
            Name = "test",
            BaseUri = new Uri("https://api.example.com"),
            Protocol = Core.Backends.ProtocolKind.OpenAiChat
        };

        var request = new LaunchRequest(backend, null, "/tmp");

        await Assert.That(request.Model).IsNull();
    }
}
