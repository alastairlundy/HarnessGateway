using System.CommandLine;
using HarnessGateway.Launcher.Commands;
using HarnessGateway.Launcher.Core.Backends;

namespace HarnessGateway.Launcher.Tests.Commands;

public class LaunchCommandFlagParsingTests
{
    [Test]
    public async Task Parse_agent_argument()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.HarnessArgument)).IsEqualTo("opencode");
    }

    [Test]
    public async Task Parse_bare_launch_is_valid_and_carries_no_agent()
    {
        // Bare `launch` is a wizard entry point, not a missing-argument error.
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.HarnessArgument)).IsNull();
    }

    [Test]
    public async Task Parse_options_without_agent_fail()
    {
        // The wizard fallback must never swallow a flag the user typed.
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch --model gpt-4");

        await Assert.That(result.Errors).IsNotEmpty();
        await Assert.That(result.Errors[0].Message).Contains("A harness identifier is required");
    }

    [Test]
    public async Task Parse_endpoint_and_protocol()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --endpoint https://api.example.com/v1 --protocol openai-chat");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.EndpointOption)).IsEqualTo("https://api.example.com/v1");
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsEqualTo("openai-chat");
    }

    [Test]
    public async Task Parse_model_option()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --model gpt-4");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.ModelOption)).IsEqualTo("gpt-4");
    }

    [Test]
    public async Task Parse_model_short_alias()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode -m gpt-4");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.ModelOption)).IsEqualTo("gpt-4");
    }

    [Test]
    public async Task Parse_model_short_alias_is_equivalent_to_long_form()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult shortForm = command.Parse("launch opencode -m claude-sonnet-5");
        ParseResult longForm = command.Parse("launch opencode --model claude-sonnet-5");

        await Assert.That(shortForm.Errors).IsEmpty();
        await Assert.That(shortForm.GetValue(LaunchCommand.ModelOption))
            .IsEqualTo(longForm.GetValue(LaunchCommand.ModelOption));
    }

    [Test]
    public async Task Parse_model_short_alias_via_root_command()
    {
        RootCommand root = new RootCommand { new LaunchCommand() };
        ParseResult shortForm = root.Parse("launch opencode -m claude-sonnet-5");
        ParseResult longForm = root.Parse("launch opencode --model claude-sonnet-5");

        await Assert.That(shortForm.Errors).IsEmpty();
        await Assert.That(shortForm.GetValue(LaunchCommand.ModelOption)).IsNotNull();
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
        await Assert.That(LaunchCommand.BackendOption.Aliases).IsEmpty();
        await Assert.That(LaunchCommand.EndpointOption.Aliases).IsEmpty();
        await Assert.That(LaunchCommand.ProtocolOption.Aliases).IsEmpty();
        await Assert.That(LaunchCommand.PersistOption.Aliases).IsEmpty();
        await Assert.That(LaunchCommand.ConfirmRunningOption.Aliases).IsEmpty();
    }

    [Test]
    public async Task Parse_confirm_running_is_a_bare_long_form_flag()
    {
        // The flag carries a yes/no, so the token is the whole spelling: it
        // parses as present/absent, never as a valued option.
        LaunchCommand command = new LaunchCommand();
        ParseResult absent = command.Parse("launch opencode");
        ParseResult present = command.Parse("launch opencode --confirm-running");

        await Assert.That(present.Errors).IsEmpty();
        await Assert.That(present.GetValue(LaunchCommand.ConfirmRunningOption)).IsTrue();
        await Assert.That(absent.GetValue(LaunchCommand.ConfirmRunningOption)).IsFalse();
    }

    [Test]
    public async Task Parse_confirm_running_without_agent_fails()
    {
        // Options without a harness stay a parse error for this flag too: the
        // wizard fallback must not swallow it, and the wizard could not answer
        // the running-instance question anyway.
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch --confirm-running");

        await Assert.That(result.Errors).IsNotEmpty();
        await Assert.That(result.Errors[0].Message).Contains("A harness identifier is required");
    }

    [Test]
    public async Task Parse_confirm_running_with_a_cli_harness_is_accepted()
    {
        // Kind=Cli misuse is the flow's refusal to make (the parse layer never
        // inspects the harness kind), so a CLI harness parses cleanly here.
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --confirm-running");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.ConfirmRunningOption)).IsTrue();
    }

    [Test]
    public async Task Parse_persist_single_value()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --persist config");

        await Assert.That(result.Errors).IsEmpty();
        string[]? persistValues = result.GetValue(LaunchCommand.PersistOption);
        await Assert.That(persistValues).Contains("config");
    }

    [Test]
    public async Task Parse_persist_multiple_values()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --persist config model");

        await Assert.That(result.Errors).IsEmpty();
        string[]? persistValues = result.GetValue(LaunchCommand.PersistOption);
        await Assert.That(persistValues).Contains("config");
        await Assert.That(persistValues).Contains("model");
    }

    [Test]
    public async Task Parse_persist_comma_list_sugar()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --persist config,model");

        await Assert.That(result.Errors).IsEmpty();
        string[]? persistValues = result.GetValue(LaunchCommand.PersistOption);
        await Assert.That(persistValues).IsNotNull();
        IReadOnlyList<string> parsed = LaunchCommand.ParsePersistValues(persistValues!);
        await Assert.That(parsed).Contains("config");
        await Assert.That(parsed).Contains("model");
    }

    [Test]
    public async Task Parse_backend_option()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --backend production");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.BackendOption)).IsEqualTo("production");
    }

    [Test]
    public async Task Parse_backend_without_value_fails()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --backend");

        await Assert.That(result.Errors).IsNotEmpty();
    }

    [Test]
    public async Task Parse_key_option_is_rejected()
    {
        // Keys are not overridable per launch; the stored key is the sole source.
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --key sk-secret");

        await Assert.That(result.Errors).IsNotEmpty();
    }

    [Test]
    public async Task Parse_persist_backend_value_fails()
    {
        // The persist family stays closed at config|model: a --backend launch
        // cannot persist its selection.
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --backend production --persist backend");

        await Assert.That(result.Errors).IsNotEmpty();
    }

    [Test]
    public async Task Parse_endpoint_without_protocol_fails()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --endpoint https://api.example.com/v1");

        await Assert.That(result.Errors).IsNotEmpty();
        await Assert.That(result.Errors[0].Message).Contains("--endpoint requires --protocol");
    }

    [Test]
    public async Task Parse_protocol_without_endpoint_fails()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --protocol openai-chat");

        await Assert.That(result.Errors).IsNotEmpty();
        await Assert.That(result.Errors[0].Message).Contains("--protocol requires --endpoint");
    }

    [Test]
    public async Task Parse_invalid_persist_value_fails()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --persist invalid");

        await Assert.That(result.Errors).IsNotEmpty();
    }

    [Test]
    public async Task Parse_all_options()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch kilo --endpoint https://api.example.com/v1 --protocol anthropic --model claude-sonnet-5 --persist config model");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.HarnessArgument)).IsEqualTo("kilo");
        await Assert.That(result.GetValue(LaunchCommand.EndpointOption)).IsEqualTo("https://api.example.com/v1");
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsEqualTo("anthropic");
        await Assert.That(result.GetValue(LaunchCommand.ModelOption)).IsEqualTo("claude-sonnet-5");
        string[]? persistValues = result.GetValue(LaunchCommand.PersistOption);
        await Assert.That(persistValues).Contains("config");
        await Assert.That(persistValues).Contains("model");
    }

    [Test]
    public async Task Parse_protocol_anthropic()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --endpoint https://api.anthropic.com --protocol anthropic");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsEqualTo("anthropic");
    }

    [Test]
    public async Task Parse_protocol_openai_responses()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode --endpoint https://api.openai.com/v1 --protocol openai-responses");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsEqualTo("openai-responses");
    }

    [Test]
    public async Task Parse_no_optional_flags()
    {
        LaunchCommand command = new LaunchCommand();
        ParseResult result = command.Parse("launch opencode");

        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.GetValue(LaunchCommand.HarnessArgument)).IsEqualTo("opencode");
        await Assert.That(result.GetValue(LaunchCommand.EndpointOption)).IsNull();
        await Assert.That(result.GetValue(LaunchCommand.ProtocolOption)).IsNull();
        await Assert.That(result.GetValue(LaunchCommand.ModelOption)).IsNull();
        string[]? persistValues = result.GetValue(LaunchCommand.PersistOption);
        await Assert.That(persistValues).IsEmpty();
    }

    [Test]
    public async Task ParseEndpoint_validates_absolute_uri()
    {
        Uri uri = LaunchCommand.ParseEndpoint("https://api.example.com/v1");
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
        IReadOnlyList<string> values = LaunchCommand.ParsePersistValues(["config,model"]);
        await Assert.That(values).Contains("config");
        await Assert.That(values).Contains("model");
    }

    [Test]
    public async Task ParsePersistValues_handles_space_separated()
    {
        IReadOnlyList<string> values = LaunchCommand.ParsePersistValues(["config", "model"]);
        await Assert.That(values).Contains("config");
        await Assert.That(values).Contains("model");
    }

    [Test]
    public async Task ParsePersistValues_ignores_invalid_values()
    {
        IReadOnlyList<string> values = LaunchCommand.ParsePersistValues(["config", "invalid", "model"]);
        await Assert.That(values).Contains("config");
        await Assert.That(values).Contains("model");
        await Assert.That(values.Count).IsEqualTo(2);
    }
}
