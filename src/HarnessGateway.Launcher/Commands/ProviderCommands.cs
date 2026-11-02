namespace HarnessGateway.Launcher.Commands;

internal sealed class ProviderCommands : Command
{
    private readonly SettingsStore _settingsStore;
    private readonly ISecureKeyStore _keyStore;
    private readonly IKeyPrompt _keyPrompt;

    internal ProviderCommands(SettingsStore settingsStore, ISecureKeyStore keyStore,
        IKeyPrompt keyPrompt)
        : base("provider", "Manage saved providers")
    {
        _settingsStore = settingsStore;
        _keyStore = keyStore;
        _keyPrompt = keyPrompt;

        Add(CreateAddCommand());
        Add(CreateListCommand());
        Add(CreateRemoveCommand());
        Add(CreateSetKeyCommand());
        Add(CreateClearKeyCommand());
        Add(CreateModelCommand());
    }

    private Command CreateAddCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The provider entry name; an existing entry with the same name is replaced",
        };
        Option<string?> endpointOption = new("--endpoint")
        {
            Description = "The provider endpoint URL (required)",
        };
        Option<string?> protocolOption = new("--protocol")
        {
            Description = "The protocol (openai-chat, openai-responses, anthropic) (required)",
        };
        Option<string?> keyOption = new("--key")
        {
            Description =
                "API key to save in the platform credential store; skips the interactive prompt. " +
                "Warning: the value is visible in your shell history - prefer the interactive prompt",
        };

        Command cmd = new("add", "Create or replace a saved provider entry")
        {
            nameArg,
            endpointOption,
            protocolOption,
            keyOption,
        };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string name = result.GetRequiredValue(nameArg);
            string? endpoint = result.GetValue(endpointOption);
            string? protocol = result.GetValue(protocolOption);
            string? key = result.GetValue(keyOption);

            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            if (endpoint is null && protocol is null)
            {
                await Console.Error.WriteLineAsync("provider add requires --endpoint and --protocol")
                    .ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            ProviderResolutionResult resolution = ProviderResolution.Resolve(settings, name, endpoint, protocol,
                model: null);

            if (resolution is ProviderResolutionResult.Failed failed
                && failed.Failure != ProviderResolutionFailure.UnknownProviderName)
            {
                await Console.Error.WriteLineAsync(failed.Message).ConfigureAwait(true);
                return ExitCodeFor(failed.Failure);
            }

            // Parse the flags once, before the create-vs-update branch: the
            // both-flags guard above and Resolve's pairing and format checks
            // have already accepted both values; this single pattern is what
            // proves their non-nullness to the compiler.
            (Uri BaseUri, ProtocolKind DefaultProtocol)? overridePair =
                endpoint is { } endpointValue && protocol is { } protocolValue
                    ? (LaunchCommand.ParseEndpoint(endpointValue), LaunchCommand.ParseProtocol(protocolValue))
                    : null;

            // Resolved updates the saved entry in place; an unknown name (or
            // the unreachable EmptySettings - both flags are required above,
            // so a complete override always resolves) creates a new entry.
            bool isCreate = resolution is not ProviderResolutionResult.Resolved;
            Provider entry;
            if (resolution is ProviderResolutionResult.Resolved resolved)
            {
                // Selection by name only ever yields the provider case here:
                // an unknown name failed above, so no ad-hoc case can reach
                // this branch. Thrown instead of cast so the impossibility
                // stays locally verifiable.
                entry = resolved.Launch switch
                {
                    LaunchResolution.ProviderCase providerCase => providerCase.Provider,
                    _ => throw new InvalidOperationException(
                        "provider add resolved an ad-hoc launch for a named entry."),
                };
            }
            else if (overridePair is { } pair)
            {
                entry = new Provider
                {
                    Name = name,
                    BaseUri = pair.BaseUri,
                    DefaultProtocol = pair.DefaultProtocol,
                };
            }
            else
            {
                // Unreachable: the both-flags guard means the create path always
                // carries a parsed pair. Thrown instead of suppressed so the
                // compiler can verify the construction locally.
                throw new InvalidOperationException(
                    "provider add reached the create path without --endpoint and --protocol.");
            }

            string? secret = key ??
                             _keyPrompt.PromptForNewKey(entry.Name, entry.ApiKey is not null);

            if (secret is not null)
            {
                string reference = KeyReference.ForProvider(entry.Name);
                try
                {
                    await _keyStore.StoreAsync(reference, secret, cancellationToken).ConfigureAwait(true);
                }
                catch (SecureKeyStoreException ex)
                {
                    await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(true);
                    return (int)LaunchExitCode.ConfigRestoreFailure;
                }

                entry = entry with { ApiKey = reference };
            }

            ConfigCommand.UpdateProvider(settings, entry);
            _settingsStore.Save(settings);

            Console.WriteLine($"Provider '{entry.Name}' {(isCreate ? "created" : "updated")}.");
            if (isCreate)
            {
                Console.WriteLine(
                    $"Run 'harnessgateway config set defaultProvider {entry.Name}' to make it the default.");
            }

            return 0;
        });

        return cmd;
    }

    private Command CreateListCommand()
    {
        Command cmd = new("list", "List saved provider entries with the default marked");

        cmd.SetAction(async (result, cancellationToken) =>
        {
            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            if (settings.Providers.Count == 0)
            {
                Console.WriteLine(
                    "No saved providers. " +
                    "Run 'harnessgateway provider add <name> --endpoint <url> --protocol <protocol>' to add one.");
                return 0;
            }

            foreach (Provider entry in settings.Providers)
            {
                bool isDefault = !string.IsNullOrEmpty(settings.DefaultProvider)
                                 && string.Equals(settings.DefaultProvider, entry.Name, StringComparison.OrdinalIgnoreCase);

                string marker = isDefault ? "* " : "  ";
                Console.WriteLine(
                    $"{marker}{entry.Name}  {entry.BaseUri}  {ProviderResolution.ProtocolName(entry.DefaultProtocol)}");
            }

            if (string.IsNullOrEmpty(settings.DefaultProvider))
            {
                Console.WriteLine(
                    "(no default provider is set; " +
                    "run 'harnessgateway config set defaultProvider <name>' to choose one)");
            }
            else
            {
                Console.WriteLine("(* = default provider)");
            }

            return 0;
        });

        return cmd;
    }

    private Command CreateRemoveCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The saved provider entry to remove",
        };

        Command cmd = new("remove", "Delete a saved provider entry and its stored key") { nameArg };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string name = result.GetRequiredValue(nameArg);

            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            ProviderResolutionResult resolution = ProviderResolution.ResolveEntry(settings, name);

            (Provider? entry, int resolutionExitCode) = await TryResolveEntryAsync(resolution)
                .ConfigureAwait(true);
            if (entry is null)
            {
                return resolutionExitCode;
            }

            if (settings.Providers.Count == 1)
            {
                await Console.Error.WriteLineAsync(
                    $"Cannot remove '{entry.Name}': it is the last saved provider; " +
                    "settings must always keep at least one provider.").ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            if (string.Equals(settings.DefaultProvider, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync(
                        $"Cannot remove '{entry.Name}': it is the current default provider. " +
                        "Run 'harnessgateway config set defaultProvider <name>' to choose a new default first.")
                    .ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            // T005: the key-deletion failure is reported but must never block
            // entry removal - a removed identity leaves the list either way,
            // with the orphaned store record called out for the user.
            string? keyFailure = null;
            if (KeyReference.IsReference(entry.ApiKey))
            {
                try
                {
                    await _keyStore.DeleteAsync(KeyReference.ForProvider(entry.Name), cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (SecureKeyStoreException ex)
                {
                    keyFailure = ex.Message;
                }
            }

            settings.Providers.RemoveAll(b =>
                string.Equals(b.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            _settingsStore.Save(settings);

            if (keyFailure is not null)
            {
                await Console.Error.WriteLineAsync(keyFailure).ConfigureAwait(true);
                Console.WriteLine($"Provider '{entry.Name}' removed.");
                return (int)LaunchExitCode.ConfigRestoreFailure;
            }

            Console.WriteLine($"Provider '{entry.Name}' removed.");
            return 0;
        });

        return cmd;
    }

    private Command CreateSetKeyCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The saved provider entry whose key to set or replace",
        };
        Option<string?> keyOption = new("--key")
        {
            Description =
                "API key to save in the platform credential store; skips the interactive prompt. " +
                "Warning: the value is visible in your shell history - prefer the interactive prompt",
        };

        Command cmd = new("set-key", "Set or replace the saved API key for a provider entry")
        {
            nameArg,
            keyOption,
        };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string name = result.GetRequiredValue(nameArg);
            string? key = result.GetValue(keyOption);

            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            ProviderResolutionResult resolution = ProviderResolution.ResolveEntry(settings, name);

            (Provider? entry, int resolutionExitCode) = await TryResolveEntryAsync(resolution)
                .ConfigureAwait(true);
            if (entry is null)
            {
                return resolutionExitCode;
            }

            string? secret = key ??
                             _keyPrompt.PromptForNewKey(entry.Name, entry.ApiKey is not null);

            if (secret is null)
            {
                Console.WriteLine($"No API key entered; provider '{entry.Name}' was left unchanged.");
                return 0;
            }

            string reference = KeyReference.ForProvider(entry.Name);
            try
            {
                await _keyStore.StoreAsync(reference, secret, cancellationToken).ConfigureAwait(true);
            }
            catch (SecureKeyStoreException ex)
            {
                await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(true);
                return (int)LaunchExitCode.ConfigRestoreFailure;
            }

            // Only the credential record changes here: endpoint, protocol, and
            // default model are never touched by set-key.
            Provider updated = entry with { ApiKey = reference };
            ConfigCommand.UpdateProvider(settings, updated);
            _settingsStore.Save(settings);

            Console.WriteLine($"API key for provider '{entry.Name}' saved.");
            return 0;
        });

        return cmd;
    }

    private Command CreateClearKeyCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The saved provider entry whose key to clear",
        };

        Command cmd = new("clear-key", "Remove the saved API key from a provider entry") { nameArg };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string name = result.GetRequiredValue(nameArg);

            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            ProviderResolutionResult resolution = ProviderResolution.ResolveEntry(settings, name);

            (Provider? entry, int resolutionExitCode) = await TryResolveEntryAsync(resolution)
                .ConfigureAwait(true);
            if (entry is null)
            {
                return resolutionExitCode;
            }

            if (entry.ApiKey is null)
            {
                // Idempotent on a keyless provider: no store entry, no settings write.
                Console.WriteLine($"Provider '{entry.Name}' has no saved key; nothing to clear.");
                return 0;
            }

            // A legacy plaintext key predates the store and has no entry there,
            // so only a reference is worth deleting from the credential store.
            if (KeyReference.IsReference(entry.ApiKey))
            {
                try
                {
                    await _keyStore.DeleteAsync(KeyReference.ForProvider(entry.Name), cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (SecureKeyStoreException ex)
                {
                    await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(true);
                    return (int)LaunchExitCode.ConfigRestoreFailure;
                }
            }

            Provider updated = entry with { ApiKey = null };
            ConfigCommand.UpdateProvider(settings, updated);
            _settingsStore.Save(settings);

            Console.WriteLine($"API key for provider '{entry.Name}' cleared.");
            return 0;
        });

        return cmd;
    }

    private Command CreateModelCommand()
    {
        Command cmd = new("model", "Manage a provider's model catalog");
        cmd.Add(CreateModelAddCommand());
        cmd.Add(CreateModelRemoveCommand());
        cmd.Add(CreateModelListCommand());
        return cmd;
    }

    private Command CreateModelAddCommand()
    {
        Argument<string> providerArg = new("provider")
        {
            Description = "The provider whose model catalog to edit",
        };
        Argument<string> modelArg = new("model")
        {
            Description = "The model entry name to add",
        };
        Option<string?> protocolOption = new("--protocol")
        {
            Description =
                "Protocol override for this model (openai-chat, openai-responses, anthropic); " +
                "omit to inherit the provider default",
        };

        Command cmd = new("add", "Add a model entry to a provider's catalog")
        {
            providerArg,
            modelArg,
            protocolOption,
        };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string providerName = result.GetRequiredValue(providerArg);
            string modelName = result.GetRequiredValue(modelArg);
            string? protocol = result.GetValue(protocolOption);

            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            ProtocolKind? protocolOverride = null;
            if (protocol is not null)
            {
                ProtocolKind? kind = ProviderResolution.TryParseProtocol(protocol);
                if (kind is null)
                {
                    await Console.Error.WriteLineAsync(
                            $"Invalid protocol '{protocol}'. Must be one of: openai-chat, openai-responses, anthropic")
                        .ConfigureAwait(true);
                    return (int)LaunchExitCode.UsageError;
                }

                protocolOverride = kind;
            }

            ProviderResolutionResult resolution = ProviderResolution.ResolveEntry(settings, providerName);

            (Provider? entry, int resolutionExitCode) = await TryResolveEntryAsync(resolution)
                .ConfigureAwait(true);
            if (entry is null)
            {
                return resolutionExitCode;
            }

            // T002: entry names are unique within a provider, matched with the
            // same case-insensitive rule launch resolution uses.
            if (entry.Models.Any(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)))
            {
                await Console.Error.WriteLineAsync(
                        $"model '{modelName}' is already listed for provider '{entry.Name}'.")
                    .ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            Provider updated = entry with
            {
                Models = [.. entry.Models, new ModelEntry { Name = modelName, Protocol = protocolOverride }],
            };
            ConfigCommand.UpdateProvider(settings, updated);
            _settingsStore.Save(settings);

            Console.WriteLine($"Model '{modelName}' added to provider '{entry.Name}'.");
            return 0;
        });

        return cmd;
    }

    private Command CreateModelRemoveCommand()
    {
        Argument<string> providerArg = new("provider")
        {
            Description = "The provider whose model catalog to edit",
        };
        Argument<string> modelArg = new("model")
        {
            Description = "The model entry name to remove",
        };

        Command cmd = new("remove", "Remove a model entry from a provider's catalog")
        {
            providerArg,
            modelArg,
        };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string providerName = result.GetRequiredValue(providerArg);
            string modelName = result.GetRequiredValue(modelArg);

            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            ProviderResolutionResult resolution = ProviderResolution.ResolveEntry(settings, providerName);

            (Provider? entry, int resolutionExitCode) = await TryResolveEntryAsync(resolution)
                .ConfigureAwait(true);
            if (entry is null)
            {
                return resolutionExitCode;
            }

            if (!entry.Models.Any(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)))
            {
                await Console.Error.WriteLineAsync(
                        $"model '{modelName}' is not listed for provider '{entry.Name}'.")
                    .ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            Provider updated = entry with
            {
                Models = entry.Models
                    .Where(m => !string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
            };
            ConfigCommand.UpdateProvider(settings, updated);
            _settingsStore.Save(settings);

            Console.WriteLine($"Model '{modelName}' removed from provider '{entry.Name}'.");
            return 0;
        });

        return cmd;
    }

    private Command CreateModelListCommand()
    {
        Argument<string> providerArg = new("provider")
        {
            Description = "The provider whose model catalog to list",
        };

        Command cmd = new("list", "List a provider's model catalog") { providerArg };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string providerName = result.GetRequiredValue(providerArg);

            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            ProviderResolutionResult resolution = ProviderResolution.ResolveEntry(settings, providerName);

            (Provider? entry, int resolutionExitCode) = await TryResolveEntryAsync(resolution)
                .ConfigureAwait(true);
            if (entry is null)
            {
                return resolutionExitCode;
            }

            if (entry.Models.Count == 0)
            {
                Console.WriteLine(
                    $"No models listed for provider '{entry.Name}'; any model launches on " +
                    $"{ProviderResolution.ProtocolName(entry.DefaultProtocol)}.");
                return 0;
            }

            Console.WriteLine($"Models for provider '{entry.Name}':");
            foreach (ModelEntry model in entry.Models)
            {
                string protocol = model.Protocol is { } overrideProtocol
                    ? ProviderResolution.ProtocolName(overrideProtocol)
                    : "(provider default)";
                Console.WriteLine($"  {model.Name}  {protocol}");
            }

            return 0;
        });

        return cmd;
    }

    /// <summary>
    /// Binds a no-override resolution to the saved entry for the config verbs
    /// (remove, set-key, clear-key) and the model sub-verbs: the <c>Resolved</c>
    /// variant proves the provider case non-null, <c>Failed</c> carries its own
    /// message and typed exit code, and <c>EmptySettings</c> is unreachable here
    /// - these verbs always pass the required name argument, so resolution
    /// either finds the entry or fails with the unknown-name class.
    /// </summary>
    private static async Task<(Provider? Entry, int ExitCode)> TryResolveEntryAsync(
        ProviderResolutionResult resolution)
    {
        switch (resolution)
        {
            case ProviderResolutionResult.Resolved { Launch: LaunchResolution.ProviderCase providerCase }:
                return (providerCase.Provider, 0);

            case ProviderResolutionResult.Failed failed:
                await Console.Error.WriteLineAsync(failed.Message).ConfigureAwait(true);
                return (null, ExitCodeFor(failed.Failure));

            default:
                await Console.Error.WriteLineAsync("No provider is configured.").ConfigureAwait(true);
                return (null, (int)LaunchExitCode.UsageError);
        }
    }

    private static int ExitCodeFor(ProviderResolutionFailure failure) =>
        failure is ProviderResolutionFailure.UnknownProviderName
            or ProviderResolutionFailure.DanglingDefaultProvider
            or ProviderResolutionFailure.AbsentDefaultProvider
            ? (int)LaunchExitCode.UnknownProviderName
            : (int)LaunchExitCode.UsageError;
}
