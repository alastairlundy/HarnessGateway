/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using HarnessGateway.Launcher.Wizard.Abstractions;
using HarnessGateway.Launcher.Wizard.Models;
using Spectre.Console;

namespace HarnessGateway.Launcher.Wizard;

public sealed class SpectreConsoleAbstraction : IConsoleAbstraction
{
    public async Task<string> PromptChoiceAsync(string title,
        IReadOnlyList<ConsoleChoice> choices, CancellationToken cancellationToken = default)
    {
        SelectionPrompt<string> selection = new SelectionPrompt<string>()
            .Title(title)
            .PageSize(10);

        foreach (ConsoleChoice choice in choices)
        {
            selection.AddChoice(BuildDisplayText(choice));
        }

        string result = await AnsiConsole.PromptAsync(selection, cancellationToken)
            .ConfigureAwait(true);

        foreach (ConsoleChoice choice in choices)
        {
            string display = BuildDisplayText(choice);

            if (string.Equals(result, display, StringComparison.OrdinalIgnoreCase))
            {
                return await Task.FromResult(choice.Name).ConfigureAwait(true);
            }
        }

        return await Task.FromResult(result).ConfigureAwait(true);
    }

    // Choice names and annotations are user-facing text (provider names come
    // from settings) and may contain "[...]" that Spectre's markup parser
    // would read as a style tag - e.g. the "[add new provider]" choice throws
    // "Could not find color or style 'add'". Escape the text, but keep the
    // intentional [dim] wrapper around the annotation as real markup, the
    // whole-line [red] wrap for an unavailable choice as real markup, and the
    // [orange1] wrap around the incompatible segment as real markup. Both the
    // value added to the prompt and the value matched against its result must
    // come from here so the round-trip stays byte-identical.
    internal static string BuildDisplayText(ConsoleChoice choice)
    {
        string name = Markup.Escape(choice.Name);

        if (choice.Annotation is null)
        {
            return choice.Unavailable ? $"[red]{name}[/]" : name;
        }

        string annotation = Markup.Escape(choice.Annotation);

        // Not installed: the entire line (name plus annotation) renders red,
        // taking precedence over the incompatible indication - a harness that
        // is both unavailable and incompatible is red first.
        if (choice.Unavailable)
        {
            return $"[red]{name} ({annotation})[/]";
        }

        // The incompatible segment renders orange instead of dim: close the
        // dim wrapper before the segment and re-open it after, so only that
        // segment carries the orange colour.
        // The segment text is located inside the already-escaped annotation
        // rather than re-derived, so the dim base keeps exactly the raw
        // annotation the caller composed.
        if (choice.IncompatibleProtocols is not null)
        {
            string segment = Markup.Escape($"incompatible: {choice.IncompatibleProtocols}");
            int index = annotation.IndexOf(segment, StringComparison.Ordinal);

            if (index >= 0)
            {
                string before = annotation[..index];
                string after = annotation[(index + segment.Length)..];

                return $"{name} [dim]({before}[/][orange1]{segment}[/][dim]{after})[/]";
            }
        }

        return $"{name} [dim]({annotation})[/]";
    }

    public Task<string> PromptTextAsync(string prompt, string? defaultValue = null,
        bool secret = false, CancellationToken cancellationToken = default)
    {
        TextPrompt<string> textPrompt = new TextPrompt<string>(prompt)
            .AllowEmpty();

        if (defaultValue is not null)
        {
            textPrompt.DefaultValue(defaultValue);
        }

        if (secret)
        {
            textPrompt.Secret();
        }

        string result = AnsiConsole.Prompt(textPrompt);
        return Task.FromResult(result);
    }

    public void WriteMarkup(string message)
    {
        AnsiConsole.MarkupLine(message);
    }

    public void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]{message}[/]");
    }
}
