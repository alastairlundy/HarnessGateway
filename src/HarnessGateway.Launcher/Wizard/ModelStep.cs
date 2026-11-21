/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using HarnessGateway.Launcher.Core.Providers.Models;
using HarnessGateway.Launcher.Wizard.Abstractions;
using HarnessGateway.Launcher.Wizard.Models;

namespace HarnessGateway.Launcher.Wizard;

public sealed class ModelStep
{
    private readonly IConsoleAbstraction _console;
    private readonly Func<Provider, CancellationToken, Task<ModelCatalog>>? _fetchModels;
    private readonly Provider? _provider;

    public ModelStep(IConsoleAbstraction console, Func<Provider, CancellationToken, Task<ModelCatalog>>? fetchModels, Provider? backend)
    {
        _console = console;
        _fetchModels = fetchModels;
        _provider = backend;
    }

    public ModelStep(IConsoleAbstraction console, ModelCatalogClient? catalogClient, Provider? backend)
    {
        _console = console;
        _fetchModels = catalogClient is not null && backend is not null
            ? (b, ct) => catalogClient.FetchModelsAsync(b, ct)
            : null;
        _provider = backend;
    }

    public async Task<ModelSelection> RunAsync(
        bool requiresModel,
        string? savedDefaultModel,
        IReadOnlySet<ProtocolKind>? harnessProtocols = null,
        CancellationToken cancellationToken = default)
    {
        // One catalogue request, made with the provider's own wire protocol so
        // the authentication header matches the endpoint it is sent to. Chat
        // and responses share the same /v1/models request, so both kinds are
        // tagged from that single result; Anthropic gets its own request shape.
        Dictionary<string, HashSet<ProtocolKind>> found = new(StringComparer.OrdinalIgnoreCase);

        if (_fetchModels is not null && _provider is not null)
        {
            IReadOnlyList<ProtocolKind> tagged = _provider.DefaultProtocol switch
            {
                ProtocolKind.OpenAiChat or ProtocolKind.OpenAiResponses => [ProtocolKind.OpenAiChat, ProtocolKind.OpenAiResponses],
                ProtocolKind.Anthropic => [ProtocolKind.Anthropic],
                _ => [_provider.DefaultProtocol],
            };

            try
            {
                ModelCatalog catalog = await _fetchModels(_provider, cancellationToken).ConfigureAwait(true);

                foreach (ModelCatalogEntry model in catalog.Models)
                {
                    if (!found.TryGetValue(model.Id, out HashSet<ProtocolKind>? set))
                    {
                        set = new HashSet<ProtocolKind>();
                        found[model.Id] = set;
                    }

                    foreach (ProtocolKind kind in tagged)
                    {
                        set.Add(kind);
                    }
                }
            }
            catch
            {
                await Task.Run(() => _console.WriteError(
                        "Could not fetch model catalogue. Endpoint may be unreachable."),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        bool hasCatalog = found.Count > 0;

        if (!hasCatalog || _provider is null)
        {
            return await RunDegradedAsync(requiresModel, savedDefaultModel, cancellationToken).ConfigureAwait(true);
        }

        return await RunWithCatalogAsync(found, requiresModel, savedDefaultModel, harnessProtocols, cancellationToken).ConfigureAwait(true);
    }

    private async Task<ModelSelection> RunWithCatalogAsync(
        Dictionary<string, HashSet<ProtocolKind>> found,
        bool requiresModel, string? savedDefaultModel,
        IReadOnlySet<ProtocolKind>? harnessProtocols,
        CancellationToken cancellationToken)
    {
        List<ConsoleChoice> choices = [];

        if (!requiresModel)
        {
            choices.Add(new ConsoleChoice("[skip]", "do not set a model"));
        }

        foreach (KeyValuePair<string, HashSet<ProtocolKind>> entry in found.OrderBy(e => e.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            List<string> annotationParts = [];

            // Protocols found under, in stable kind order.
            string protocolsText = string.Join(
                ", ",
                entry.Value.Order().Select(ProviderResolution.ProtocolName));
            annotationParts.Add(protocolsText);

            if (string.Equals(entry.Key, savedDefaultModel, StringComparison.OrdinalIgnoreCase))
            {
                annotationParts.Add("saved default");
            }

            // Indication, never filtering (D012): a model whose every listed
            // protocol tag the harness cannot speak gets the harness list's
            // "incompatible: <protocols>" vocabulary appended to the
            // annotation, and the structured state rides along so the
            // presenter can colour it. A model with at least one speakable
            // tag stays unmarked - the partial-match blind spot is accepted -
            // and no choice is ever dropped from the list.
            string? incompatible = null;
            if (harnessProtocols is not null && !entry.Value.Overlaps(harnessProtocols))
            {
                incompatible = protocolsText;
                annotationParts.Add($"incompatible: {protocolsText}");
            }

            choices.Add(new ConsoleChoice(
                entry.Key,
                string.Join("; ", annotationParts))
            {
                IncompatibleProtocols = incompatible,
            });
        }

        string title = requiresModel ? "Select a model (required):" : "Select a model (or skip):";

        string selected = await _console.PromptChoiceAsync(title, choices, cancellationToken).ConfigureAwait(true);

        if (string.Equals(selected, "[skip]", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelSelection(Model: null, Protocol: null);
        }

        if (!found.TryGetValue(selected, out HashSet<ProtocolKind>? selectedProtocols))
        {
            return new ModelSelection(selected, Protocol: null);
        }

        // Determine the effective protocol: prompt only when more than one
        // protocol was found, preselecting the provider default when present.
        ProtocolKind effectiveProtocol;
        if (selectedProtocols.Count > 1)
        {
            List<ProtocolKind> ordered =
            [
                .. selectedProtocols
                    .OrderBy(p => p == _provider?.DefaultProtocol ? 0 : 1)
                    .ThenBy(p => p)
            ];

            List<ConsoleChoice> protocolChoices =
            [
                .. ordered
                    .Select(p => new ConsoleChoice(ProviderResolution.ProtocolName(p)))
            ];

            string protocolStr = await _console.PromptChoiceAsync(
                "Protocol:", protocolChoices, cancellationToken).ConfigureAwait(true);

            effectiveProtocol = ParseProtocol(protocolStr) ?? selectedProtocols.First();
        }
        else
        {
            effectiveProtocol = selectedProtocols.First();
        }

        // The effective protocol is the model's own protocol; the wizard
        // records it on the result and the launch path applies it. No
        // ModelEntry is persisted anywhere - the wizard is write-free.
        return new ModelSelection(selected, effectiveProtocol);
    }

    private async Task<ModelSelection> RunDegradedAsync(
        bool requiresModel,
        string? savedDefaultModel,
        CancellationToken cancellationToken)
    {
        if (savedDefaultModel is not null)
        {
            await Task.Run(() => _console.WriteMarkup(
                $"Using saved default model: [green]{savedDefaultModel}[/]"), cancellationToken).ConfigureAwait(false);

            return new ModelSelection(
                savedDefaultModel,
                Protocol: null);
        }

        if (requiresModel)
        {
            string model = await _console.PromptTextAsync(
                "Enter model name (required):", cancellationToken: cancellationToken).ConfigureAwait(true);
            string? trimmed = string.IsNullOrWhiteSpace(model) ? null : model.Trim();

            return new ModelSelection(
                trimmed,
                Protocol: null);
        }

        string skip = await _console.PromptChoiceAsync("Model catalogue unavailable:", [
            new ConsoleChoice("[skip]", "do not set a model"),
            new ConsoleChoice("[manual]", "enter a model name manually"),
        ], cancellationToken).ConfigureAwait(true);

        if (string.Equals(skip, "[skip]", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelSelection(Model: null, Protocol: null);
        }

        string manualModel = await _console.PromptTextAsync("Enter model name:", cancellationToken: cancellationToken).ConfigureAwait(true);
        string? manual = string.IsNullOrWhiteSpace(manualModel) ? null : manualModel.Trim();

        return new ModelSelection(
            manual,
            Protocol: null);
    }

    private static ProtocolKind? ParseProtocol(string value) => value switch
    {
        "openai-chat" => ProtocolKind.OpenAiChat,
        "openai-responses" => ProtocolKind.OpenAiResponses,
        "anthropic" => ProtocolKind.Anthropic,
        _ => null,
    };
}