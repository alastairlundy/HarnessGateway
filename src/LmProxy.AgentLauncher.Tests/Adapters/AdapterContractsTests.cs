using LmProxy.AgentLauncher.Core.Adapters;
using LmProxy.AgentLauncher.Core.Configuration;
using LmProxy.AgentLauncher.Core.Launch;

namespace LmProxy.AgentLauncher.Tests.Adapters;

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
            [".opencode/config.json", "~/.config/opencode/config.json"]
        );

        await Assert.That(metadata.Name).IsEqualTo("opencode");
        await Assert.That(metadata.Kind).IsEqualTo(HarnessKind.Cli);
        await Assert.That(metadata.RequiresModel).IsTrue();
        await Assert.That(metadata.InstallHint).IsEqualTo("Install via: npm install -g @anthropic-ai/opencode");
        await Assert.That(metadata.DetectionCommand).IsEqualTo("opencode --version");
        await Assert.That(metadata.ConfigFileCandidates).Count().IsEqualTo(2);
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
            []
        );

        await Assert.That(metadata.DetectionCommand).IsNull();
        await Assert.That(metadata.ConfigFileCandidates).IsEmpty();
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
    public async Task EnvironmentVariableAction_stores_name_and_value()
    {
        var action = new EnvironmentVariableAction("OPENAI_API_KEY", "sk-test");

        await Assert.That(action.Name).IsEqualTo("OPENAI_API_KEY");
        await Assert.That(action.Value).IsEqualTo("sk-test");
        await Assert.That(action).IsTypeOf<EnvironmentVariableAction>();
    }

    [Test]
    public async Task ConfigurationAction_is_abstract_base()
    {
        ConfigurationAction cli = new CliArgumentAction("--flag", null);
        ConfigurationAction file = new ConfigFileWriteAction("/tmp", "content");
        ConfigurationAction env = new EnvironmentVariableAction("KEY", "value");

        await Assert.That(cli).IsTypeOf<CliArgumentAction>();
        await Assert.That(file).IsTypeOf<ConfigFileWriteAction>();
        await Assert.That(env).IsTypeOf<EnvironmentVariableAction>();
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
