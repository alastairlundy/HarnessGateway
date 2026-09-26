using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Backends;

namespace HarnessGateway.Launcher.Tests.Commands;

public class LaunchCommandFlagParsingTests
{
    [Test]
    public async Task Parse_agent_argument()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.AgentArgument)).IsEqualTo("opencode");
    }

    [Test]
    public async Task Parse_endpoint_and_protocol()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --endpoint https://api.example.com/v1 --protocol openai-chat");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.EndpointOption)).IsEqualTo("https://api.example.com/v1");
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsEqualTo("openai-chat");
    }

    [Test]
    public async Task Parse_model_option()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --model gpt-4");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.ModelOption)).IsEqualTo("gpt-4");
    }

    [Test]
    public async Task Parse_model_short_alias()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode -m gpt-4");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.ModelOption)).IsEqualTo("gpt-4");
    }

    [Test]
    public async Task Parse_model_short_alias_is_equivalent_to_long_form()
    {
        var command = new LaunchCommand();
        var shortForm = command.Parse("launch opencode -m claude-sonnet-5");
        var longForm = command.Parse("launch opencode --model claude-sonnet-5");

        await Assert.That(shortForm.Errors).IsEmpty();
        await Assert.That(shortForm.GetValue(LaunchCommand.ModelOption))
            .IsEqualTo(longForm.GetValue(LaunchCommand.ModelOption));
    }

    [Test]
    public async Task Model_option_has_m_as_its_only_alias()
    {
        await Assert.That(LaunchCommand.ModelOption.Name).IsEqualTo("--model");
        await Assert.That(LaunchCommand.ModelOption.Aliases.Count).IsEqualTo(1);
        await Assert.That(LaunchCommand.ModelOption.Aliases).Contains("-m");
    }

    [Test]
    public async Task Other_launch_options_have_no_short_aliases()
    {
        await Assert.That(LaunchCommand.EndpointOption.Aliases).IsEmpty();
        await Assert.That(LaunchCommand.ProtocolOption.Aliases).IsEmpty();
        await Assert.That(LaunchCommand.PersistOption.Aliases).IsEmpty();
    }

    [Test]
    public async Task Parse_persist_single_value()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --persist config");

        await Assert.That(result.Errors).IsEmpty();
        var persistValues = result.GetValue(LaunchCommand.PersistOption);
        await Assert.That(persistValues).Contains("config");
    }

    [Test]
    public async Task Parse_persist_multiple_values()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --persist config model");

        await Assert.That(result.Errors).IsEmpty();
        var persistValues = result.GetValue(LaunchCommand.PersistOption);
        await Assert.That(persistValues).Contains("config");
        await Assert.That(persistValues).Contains("model");
    }

    [Test]
    public async Task Parse_persist_comma_list_sugar()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --persist config,model");

        await Assert.That(result.Errors).IsEmpty();
        var persistValues = result.GetValue(LaunchCommand.PersistOption);
        var parsed = LaunchCommand.ParsePersistValues(persistValues);
        await Assert.That(parsed).Contains("config");
        await Assert.That(parsed).Contains("model");
    }

    [Test]
    public async Task Parse_endpoint_without_protocol_fails()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --endpoint https://api.example.com/v1");

        await Assert.That(result.Errors).IsNotEmpty();
        await Assert.That(result.Errors[0].Message).Contains("--endpoint requires --protocol");
    }

    [Test]
    public async Task Parse_protocol_without_endpoint_fails()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --protocol openai-chat");

        await Assert.That(result.Errors).IsNotEmpty();
        await Assert.That(result.Errors[0].Message).Contains("--protocol requires --endpoint");
    }

    [Test]
    public async Task Parse_invalid_persist_value_fails()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --persist invalid");

        await Assert.That(result.Errors).IsNotEmpty();
    }

    [Test]
    public async Task Parse_all_options()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch kilo --endpoint https://api.example.com/v1 --protocol anthropic --model claude-sonnet-5 --persist config model");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.AgentArgument)).IsEqualTo("kilo");
        await Assert.That(result.GetValue(LaunchCommand.EndpointOption)).IsEqualTo("https://api.example.com/v1");
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsEqualTo("anthropic");
        await Assert.That(result.GetValue(LaunchCommand.ModelOption)).IsEqualTo("claude-sonnet-5");
        var persistValues = result.GetValue(LaunchCommand.PersistOption);
        await Assert.That(persistValues).Contains("config");
        await Assert.That(persistValues).Contains("model");
    }

    [Test]
    public async Task Parse_protocol_anthropic()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --endpoint https://api.anthropic.com --protocol anthropic");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsEqualTo("anthropic");
    }

    [Test]
    public async Task Parse_protocol_openai_responses()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode --endpoint https://api.openai.com/v1 --protocol openai-responses");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsEqualTo("openai-responses");
    }

    [Test]
    public async Task Parse_no_optional_flags()
    {
        var command = new LaunchCommand();
        var result = command.Parse("launch opencode");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.AgentArgument)).IsEqualTo("opencode");
        await Assert.That(result.GetValue(LaunchCommand.EndpointOption)).IsNull();
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsNull();
        await Assert.That(result.GetValue(LaunchCommand.ModelOption)).IsNull();
        var persistValues = result.GetValue(LaunchCommand.PersistOption);
        await Assert.That(persistValues).IsEmpty();
    }

    [Test]
    public async Task ParseEndpoint_validates_absolute_uri()
    {
        var uri = LaunchCommand.ParseEndpoint("https://api.example.com/v1");
        await Assert.That(uri).IsEqualTo(new Uri("https://api.example.com/v1"));
    }

    [Test]
    public async Task ParseProtocol_maps_correctly()
    {
        await Assert.That(LaunchCommand.ParseProtocol("openai-chat")).IsEqualTo(ProtocolKind.OpenAiChat);
        await Assert.That(LaunchCommand.ParseProtocol("openai-responses")).IsEqualTo(ProtocolKind.OpenAiResponses);
        await Assert.That(LaunchCommand.ParseProtocol("anthropic")).IsEqualTo(ProtocolKind.Anthropic);
    }

    [Test]
    public async Task ParsePersistValues_handles_comma_separated()
    {
        var values = LaunchCommand.ParsePersistValues(["config,model"]);
        await Assert.That(values).Contains("config");
        await Assert.That(values).Contains("model");
    }

    [Test]
    public async Task ParsePersistValues_handles_space_separated()
    {
        var values = LaunchCommand.ParsePersistValues(["config", "model"]);
        await Assert.That(values).Contains("config");
        await Assert.That(values).Contains("model");
    }

    [Test]
    public async Task ParsePersistValues_ignores_invalid_values()
    {
        var values = LaunchCommand.ParsePersistValues(["config", "invalid", "model"]);
        await Assert.That(values).Contains("config");
        await Assert.That(values).Contains("model");
        await Assert.That(values.Count).IsEqualTo(2);
    }
}
