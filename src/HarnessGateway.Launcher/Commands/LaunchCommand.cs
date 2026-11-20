/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using HarnessGateway.Launcher.Models;

namespace HarnessGateway.Launcher.Commands;

public sealed class LaunchCommand : Command
{
    public static readonly Argument<string?> HarnessArgument = new("harness")
    {
        // Optional: bare `launch` falls back to the interactive wizard, the
        // same pipeline the no-args root action runs. Options without a
        // harness are still rejected (see the validator below).
        //
        // The Description is set in the constructor from the adapter list, not
        // here: a handwritten name list would drift from Program.cs's registry.
        Arity = ArgumentArity.ZeroOrOne,
    };

    public static readonly Option<string?> ProviderOption = new("--provider")
    {
        Description = "Select a saved provider entry by name to launch against",
    };

    public static readonly Option<string?> EndpointOption = new("--endpoint")
    {
        Description = "Override the provider endpoint URL (requires --protocol)",
    };

    public static readonly Option<string?> ProtocolOption = new("--protocol")
    {
        Description = "Override the provider protocol (openai-chat, openai-responses, anthropic)",
    };

    public static readonly Option<string?> ModelOption = new("--model", "-m")
    {
        Description = "Override the model to use for this launch",
    };

    public static readonly Option<string[]> PersistOption = new("--persist")
    {
        Description = "Persist settings after launch: 'config' to suppress restore, 'model' to save default model",
        AllowMultipleArgumentsPerToken = true,
    };

    public static readonly Option<bool> ConfirmRunningOption = new("--confirm-running")
    {
        // A flag, not a valued option: it carries a yes/no the terminal would
        // otherwise collect, so the token is the whole spelling. Deliberately
        // no harness-kind gate here either - whether the flag is misused
        // against a Kind=Cli harness is the flow's refusal to make, not a
        // parse error.
        Description = "Confirm a running instance of the harness instead of being prompted (GUI harnesses)",
    };

    public static readonly Option<string?> PrintOption = new("-p", "--print")
    {
        // ZeroOrOne on purpose: the prompt may be the option's value
        // (-p "<prompt>") or come from stdin when the value is omitted
        // (-p alone with a pipe). Presence - not the value - is what makes
        // this a print launch, so the action reads the option's result
        // rather than only its value. Which harnesses can even run
        // one-shot, and whether a prompt arrived, are the flow's refusals
        // to make, not parse errors - the same split --confirm-running uses.
        Description = "Run the harness non-interactively with a prompt: "
            + "-p \"<prompt>\", or -p alone to read the prompt from stdin",
        Arity = ArgumentArity.ZeroOrOne,
    };

    public LaunchCommand() : this(orchestrator: null)
    {
    }

    public LaunchCommand(IEnumerable<IHarnessAdapter> adapters)
        : this(orchestrator: null, wizardAction: null, adapters)
    {
    }

    internal LaunchCommand(
        LaunchOrchestrator? orchestrator,
        Func<CancellationToken, Task<int>>? wizardAction = null,
        IEnumerable<IHarnessAdapter>? adapters = null)
        : base("launch", "Launch an agent harness with the specified provider configuration")
    {
        var wizardAction1 = wizardAction;

        // Built from the registry so the help text cannot advertise an
        // identifier the lookup rejects, or omit one it accepts.
        HarnessArgument.Description = BuildHarnessDescription(adapters);

        Add(HarnessArgument);
        Add(ProviderOption);
        Add(EndpointOption);
        Add(ProtocolOption);
        Add(ModelOption);
        Add(PersistOption);
        Add(ConfirmRunningOption);
        Add(PrintOption);

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

            // Options without a harness stay a parse error, so the bare-`launch`
            // wizard fallback can never silently drop a flag the user typed.
            // Presence, not value, detects -p/--print: the prompt is optional
            // at parse time (it may come from stdin), so a valueless -p must
            // still count as an option the wizard path cannot honor.
            if (result.GetValue(HarnessArgument) is null &&
                (result.GetValue(ProviderOption) is not null
                 || result.GetValue(EndpointOption) is not null
                 || result.GetValue(ProtocolOption) is not null
                 || result.GetValue(ModelOption) is not null
                 || result.GetValue(PersistOption) is { Length: > 0 }
                 || result.GetValue(ConfirmRunningOption)
                 || result.GetResult(PrintOption) is not null))
            {
                result.AddError(
                    "A harness identifier is required when launch options are specified: " +
                    "harnessgateway launch <harness> [options]. Run 'harnessgateway launch' " +
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
            string? harness = result.GetValue(HarnessArgument);

            // Bare `launch` is the second wizard entry point: no harness
            // identifier and no options (options without a harness fail
            // validation above), so nothing the user typed can be dropped
            // here. When no wizard is wired - parse-only constructions - fail
            // loudly instead of falling through to the orchestrator with a
            // null identifier.
            if (harness is null)
            {
                if (wizardAction1 is not null)
                {
                    return await wizardAction1(cancellationToken).ConfigureAwait(true);
                }

                await Console.Error.WriteLineAsync(
                    "A harness identifier is required: harnessgateway launch <harness>. " +
                    "Run 'harnessgateway launch' with no options to use the interactive wizard.").ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            string? providerName = result.GetValue(ProviderOption);
            string? endpoint = result.GetValue(EndpointOption);
            string? protocol = result.GetValue(ProtocolOption);
            string? model = result.GetValue(ModelOption);
            bool confirmRunning = result.GetValue(ConfirmRunningOption);
            string[]? persistValues = result.GetValue(PersistOption);
            IReadOnlyList<string> persist = persistValues is not null
                ? ParsePersistValues(persistValues)
                : new List<string>();

            // -p/--print: presence decides, the value may be absent. A
            // valueless -p reads the prompt from stdin here, at the edge,
            // so the flow only ever sees a prompt value (or null, which it
            // refuses) and never has to decide whether the console's stdin
            // is worth reading.
            bool print = result.GetResult(PrintOption) is not null;
            string? printPrompt = result.GetValue(PrintOption);
            if (print && printPrompt is null && Console.IsInputRedirected)
            {
                printPrompt = await Console.In.ReadToEndAsync(cancellationToken)
                    .ConfigureAwait(true);
            }

            if (orchestrator is null)
            {
                // Check the constructor parameter, then invoke it: flow analysis
                // clears the nullable dereference, and the guard delivers the
                // loud failure the seam contract promises instead of a bare NRE.
                throw new InvalidOperationException(
                    "LaunchCommand was constructed without an orchestrator; " +
                    "wire one via the internal constructor before invoking 'launch <harness>'.");
            }

            return await orchestrator.RunAsync(
                harness,
                providerName,
                endpoint,
                protocol,
                model,
                persist,
                confirmRunning,
                print,
                printPrompt,
                cancellationToken).ConfigureAwait(true);
        });
    }

    /// <summary>
    /// The harness argument's help text, listing the command-name identifier
    /// of every registered harness. Display names and aliases are omitted on
    /// purpose: the identifiers are the only spelling the lookup accepts, so
    /// they are the only ones worth printing.
    /// </summary>
    internal static string BuildHarnessDescription(IEnumerable<IHarnessAdapter>? adapters)
    {
        string identifiers = adapters is null
            ? string.Empty
            : string.Join(", ", adapters.Select(a => a.Metadata.CommandName));

        return identifiers.Length == 0
            ? "The agent harness to launch; omit to run the interactive wizard"
            : $"The agent harness to launch ({identifiers}); omit to run the interactive wizard";
    }

    public static bool IsValidProtocol(string? value) => ProviderResolution.IsValidProtocol(value);

    public static ProtocolKind ParseProtocol(string value) =>
        ProviderResolution.TryParseProtocol(value)
        ?? throw new ArgumentException($"Unknown protocol: {value}", paramName: value);

    public static string ProtocolName(ProtocolKind protocol) => ProviderResolution.ProtocolName(protocol);

    public static Uri ParseEndpoint(string value) => new(value, UriKind.Absolute);

    public static IReadOnlyList<string> ParsePersistValues(string[] values)
    {
        List<string> result = [];
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