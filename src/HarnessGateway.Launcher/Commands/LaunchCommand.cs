using System.CommandLine;
using HarnessGateway.Launcher.Core.Backends;

namespace HarnessGateway.Launcher.Commands;

public sealed class LaunchCommand : Command
{
    private readonly LaunchOrchestrator? _orchestrator;
    private readonly Func<CancellationToken, Task<int>>? _wizardAction;

    public static readonly Argument<string?> AgentArgument = new("agent")
    {
        // Optional: bare `launch` falls back to the interactive wizard, the
        // same pipeline the no-args root action runs. Options without an
        // agent are still rejected (see the validator below).
        Arity = ArgumentArity.ZeroOrOne,
        Description = "The agent harness to launch (opencode, kilo, copilot, pi, codex, claude-code); omit to run the interactive wizard"
    };

    public static readonly Option<string?> BackendOption = new("--backend")
    {
        Description = "Select a saved backend entry by name to launch against"
    };

    public static readonly Option<string?> EndpointOption = new("--endpoint")
    {
        Description = "Override the backend endpoint URL (requires --protocol)"
    };

    public static readonly Option<string?> ProtocolOption = new("--protocol")
    {
        Description = "Override the backend protocol (openai-chat, openai-responses, anthropic)"
    };

    public static readonly Option<string?> ModelOption = new("--model", "-m")
    {
        Description = "Override the model to use for this launch"
    };

    public static readonly Option<string[]> PersistOption = new("--persist")
    {
        Description = "Persist settings after launch: 'config' to suppress restore, 'model' to save default model",
        AllowMultipleArgumentsPerToken = true
    };

    public LaunchCommand() : this(null)
    {
    }

    internal LaunchCommand(
        LaunchOrchestrator? orchestrator,
        Func<CancellationToken, Task<int>>? wizardAction = null)
        : base("launch", "Launch an agent harness with the specified backend configuration")
    {
        _orchestrator = orchestrator;
        _wizardAction = wizardAction;

        Add(AgentArgument);
        Add(BackendOption);
        Add(EndpointOption);
        Add(ProtocolOption);
        Add(ModelOption);
        Add(PersistOption);

        Validators.Add(result =>
        {
            string? endpoint = result.GetValue(EndpointOption);
            string? protocol = result.GetValue(ProtocolOption);

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

            string[]? persistValues = result.GetValue(PersistOption);
            if (persistValues is not null)
            {
                foreach (string value in persistValues)
                {
                    foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = part.Trim();
                        if (trimmed is not ("config" or "model"))
                        {
                            result.AddError($"Invalid persist value '{trimmed}'. Must be one of: config, model");
                        }
                    }
                }
            }

            // Options without an agent stay a parse error, so the bare-`launch`
            // wizard fallback can never silently drop a flag the user typed.
            if (result.GetValue(AgentArgument) is null &&
                (result.GetValue(BackendOption) is not null
                 || result.GetValue(EndpointOption) is not null
                 || result.GetValue(ProtocolOption) is not null
                 || result.GetValue(ModelOption) is not null
                 || result.GetValue(PersistOption) is { Length: > 0 }))
            {
                result.AddError(
                    "An agent is required when launch options are specified: " +
                    "harnessgateway launch <agent> [options]. Run 'harnessgateway launch' " +
                    "with no options to use the interactive wizard.");
            }
        });

        TreatUnmatchedTokensAsErrors = true;

        // The launch path's invocation seam: when the `launch` subcommand
        // matches, this action runs with the subcommand's own parsed symbols
        // and drives the orchestrator. Without it, System.CommandLine's
        // no-action fallback silently succeeds with exit 0 - the root action
        // can never see a child command's tokens.
        SetAction(async (result, cancellationToken) =>
        {
            string? agent = result.GetValue(AgentArgument);

            // Bare `launch` is the second wizard entry point: no agent and no
            // options (options without an agent fail validation above), so
            // nothing the user typed can be dropped here. When no wizard is
            // wired - parse-only constructions - fail loudly instead of
            // falling through to the orchestrator with a null agent.
            if (agent is null)
            {
                if (_wizardAction is not null)
                {
                    return await _wizardAction(cancellationToken);
                }

                Console.Error.WriteLine(
                    "An agent is required: harnessgateway launch <agent>. " +
                    "Run 'harnessgateway launch' with no options to use the interactive wizard.");
                return (int)LaunchExitCode.UsageError;
            }

            string? backendName = result.GetValue(BackendOption);
            string? endpoint = result.GetValue(EndpointOption);
            string? protocol = result.GetValue(ProtocolOption);
            string? model = result.GetValue(ModelOption);
            string[]? persistValues = result.GetValue(PersistOption);
            IReadOnlyList<string> persist = persistValues is not null
                ? ParsePersistValues(persistValues)
                : new List<string>();

            return await _orchestrator!.RunAsync(
                agent,
                backendName,
                endpoint,
                protocol,
                model,
                persist,
                Directory.GetCurrentDirectory(),
                cancellationToken);
        });
    }

    public static bool IsValidProtocol(string? value) => BackendResolution.IsValidProtocol(value);

    public static ProtocolKind ParseProtocol(string value) =>
        BackendResolution.TryParseProtocol(value)
            ?? throw new ArgumentException($"Unknown protocol: {value}");

    public static string ProtocolName(ProtocolKind protocol) => BackendResolution.ProtocolName(protocol);

    public static Uri ParseEndpoint(string value) => new(value, UriKind.Absolute);

    public static IReadOnlyList<string> ParsePersistValues(string[] values)
    {
        List<string> result = new();
        foreach (string value in values)
        {
            foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                if (trimmed is "config" or "model")
                {
                    result.Add(trimmed);
                }
            }
        }
        return result;
    }
}
