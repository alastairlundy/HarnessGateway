using System.CommandLine;
using LmProxy.AgentLauncher.Core.Backends;

namespace LmProxy.AgentLauncher.Commands;

public sealed class LaunchCommand : Command
{
    public static readonly Argument<string> AgentArgument = new("agent")
    {
        Description = "The agent harness to launch (opencode, kilo, copilot, pi)"
    };

    public static readonly Option<string?> EndpointOption = new("--endpoint")
    {
        Description = "Override the backend endpoint URL (requires --protocol)"
    };

    public static readonly Option<string?> ProtocolOption = new("--protocol")
    {
        Description = "Override the backend protocol (openai-chat, openai-responses, anthropic)"
    };

    public static readonly Option<string?> ModelOption = new("--model")
    {
        Description = "Override the model to use for this launch"
    };

    public static readonly Option<string[]> PersistOption = new("--persist")
    {
        Description = "Persist settings after launch: 'config' to suppress restore, 'model' to save default model",
        AllowMultipleArgumentsPerToken = true
    };

    public LaunchCommand() : base("launch", "Launch an agent harness with the specified backend configuration")
    {
        Add(AgentArgument);
        Add(EndpointOption);
        Add(ProtocolOption);
        Add(ModelOption);
        Add(PersistOption);

        Validators.Add(result =>
        {
            var endpoint = result.GetValue(EndpointOption);
            var protocol = result.GetValue(ProtocolOption);

            if (endpoint is not null && protocol is null)
            {
                result.AddError("--endpoint requires --protocol to be specified");
            }

            if (protocol is not null && endpoint is null)
            {
                result.AddError("--protocol requires --endpoint to be specified");
            }

            if (protocol is not null && !IsValidProtocol(protocol))
            {
                result.AddError($"Invalid protocol '{protocol}'. Must be one of: openai-chat, openai-responses, anthropic");
            }

            var persistValues = result.GetValue(PersistOption);
            if (persistValues is not null)
            {
                foreach (var value in persistValues)
                {
                    foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = part.Trim();
                        if (trimmed is not ("config" or "model"))
                        {
                            result.AddError($"Invalid persist value '{trimmed}'. Must be one of: config, model");
                        }
                    }
                }
            }
        });

        TreatUnmatchedTokensAsErrors = true;
    }

    public static bool IsValidProtocol(string? value) => value is "openai-chat" or "openai-responses" or "anthropic";

    public static ProtocolKind ParseProtocol(string value) => value switch
    {
        "openai-chat" => ProtocolKind.OpenAiChat,
        "openai-responses" => ProtocolKind.OpenAiResponses,
        "anthropic" => ProtocolKind.Anthropic,
        _ => throw new ArgumentException($"Unknown protocol: {value}")
    };

    public static Uri ParseEndpoint(string value) => new(value, UriKind.Absolute);

    public static IReadOnlyList<string> ParsePersistValues(string[] values)
    {
        var result = new List<string>();
        foreach (var value in values)
        {
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed is "config" or "model")
                {
                    result.Add(trimmed);
                }
            }
        }
        return result;
    }
}
