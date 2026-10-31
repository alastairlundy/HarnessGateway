namespace HarnessGateway.Launcher.Commands;

internal sealed class BackendCommands : Command
{
    private readonly SettingsStore _settingsStore;
    private readonly ISecureKeyStore _keyStore;
    private readonly IKeyPrompt _keyPrompt;

    internal BackendCommands(SettingsStore settingsStore, ISecureKeyStore keyStore,
        IKeyPrompt keyPrompt)
        : base("backend", "Manage saved backend profiles")
    {
        _settingsStore = settingsStore;
        _keyStore = keyStore;
        _keyPrompt = keyPrompt;

        Add(CreateAddCommand());
        Add(CreateListCommand());
        Add(CreateRemoveCommand());
        Add(CreateSetKeyCommand());
        Add(CreateClearKeyCommand());
    }

    private Command CreateAddCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The backend entry name; an existing entry with the same name is replaced",
        };
        Option<string?> endpointOption = new("--endpoint")
        {
            Description = "The backend endpoint URL (required)",
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

        Command cmd = new("add", "Create or replace a saved backend entry")
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
                await Console.Error.WriteLineAsync("config backend add requires --endpoint and --protocol")
                    .ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            BackendResolutionResult resolution = BackendResolution.Resolve(settings, name, endpoint, protocol, 
                model: null);

            if (resolution is BackendResolutionResult.Failed failed
                && failed.Failure != BackendResolutionFailure.UnknownBackendName)
            {
                await Console.Error.WriteLineAsync(failed.Message).ConfigureAwait(true);
                return ExitCodeFor(failed.Failure);
            }

            // Parse the flags once, before the create-vs-update branch: the
            // both-flags guard above and Resolve's pairing and format checks
            // have already accepted both values; this single pattern is what
            // proves their non-nullness to the compiler, replacing the two
            // suppressions that used to lean on two methods' failure orderings.
            (Uri BaseUri, ProtocolKind Protocol)? overridePair =
                endpoint is { } endpointValue && protocol is { } protocolValue
                    ? (LaunchCommand.ParseEndpoint(endpointValue), LaunchCommand.ParseProtocol(protocolValue))
                    : null;

            // Resolved updates the saved entry in place; an unknown name (or
            // the unreachable EmptySettings - both flags are required above,
            // so a complete override always resolves) creates a new entry.
            bool isCreate = resolution is not BackendResolutionResult.Resolved;
            Backend entry;
            if (resolution is BackendResolutionResult.Resolved resolved)
            {
                entry = resolved.Backend;
            }
            else if (overridePair is { } pair)
            {
                entry = new Backend
                {
                    Name = name,
                    BaseUri = pair.BaseUri,
                    Protocol = pair.Protocol,
                };
            }
            else
            {
                // Unreachable: the both-flags guard means the create path always
                // carries a parsed pair. Thrown instead of suppressed so the
                // compiler can verify the construction locally.
                throw new InvalidOperationException(
                    "config backend add reached the create path without --endpoint and --protocol.");
            }

            string? secret = key ??
                             _keyPrompt.PromptForNewKey(entry.Name, entry.ApiKey is not null);

            if (secret is not null)
            {
                string reference = KeyReference.ForBackend(entry.Name);
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

            ConfigCommand.UpdateBackend(settings, entry);
            _settingsStore.Save(settings);

            Console.WriteLine($"Backend '{entry.Name}' {(isCreate ? "created" : "updated")}.");
            if (isCreate)
            {
                Console.WriteLine(
                    $"Run 'harnessgateway config set defaultBackend {entry.Name}' to make it the default.");
            }

            return 0;
        });

        return cmd;
    }

    private Command CreateListCommand()
    {
        Command cmd = new("list", "List saved backend entries with the default marked");

        cmd.SetAction(async (result, cancellationToken) =>
        {
            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            if (settings.Backends.Count == 0)
            {
                Console.WriteLine(
                    "No saved backends. " +
                    "Run 'harnessgateway config backend add <name> --endpoint <url> --protocol <protocol>' to add one.");
                return 0;
            }

            foreach (Backend entry in settings.Backends)
            {
                bool isDefault = !string.IsNullOrEmpty(settings.DefaultBackend)
                                 && string.Equals(settings.DefaultBackend, entry.Name, StringComparison.OrdinalIgnoreCase);

                string marker = isDefault ? "* " : "  ";
                Console.WriteLine(
                    $"{marker}{entry.Name}  {entry.BaseUri}  {BackendResolution.ProtocolName(entry.Protocol)}");
            }

            if (string.IsNullOrEmpty(settings.DefaultBackend))
            {
                Console.WriteLine(
                    "(no default backend is set; " +
                    "run 'harnessgateway config set defaultBackend <name>' to choose one)");
            }
            else
            {
                Console.WriteLine("(* = default backend)");
            }

            return 0;
        });

        return cmd;
    }

    private Command CreateRemoveCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The saved backend entry to remove",
        };

        Command cmd = new("remove", "Delete a saved backend entry") { nameArg };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string name = result.GetRequiredValue(nameArg);

            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            BackendResolutionResult resolution = BackendResolution.Resolve(settings, name, endpoint: null, protocol: null, model: null);

            (Backend? entry, int resolutionExitCode) = await TryResolveEntryAsync(resolution)
                .ConfigureAwait(true);
            if (entry is null)
            {
                return resolutionExitCode;
            }

            if (settings.Backends.Count == 1)
            {
                await Console.Error.WriteLineAsync(
                    $"Cannot remove '{entry.Name}': it is the last saved backend; " +
                    "settings must always keep at least one backend.").ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            if (string.Equals(settings.DefaultBackend, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync(
                        $"Cannot remove '{entry.Name}': it is the current default backend. " +
                        "Run 'harnessgateway config set defaultBackend <name>' to choose a new default first.")
                    .ConfigureAwait(true);
                return (int)LaunchExitCode.UsageError;
            }

            if (KeyReference.IsReference(entry.ApiKey))
            {
                try
                {
                    await _keyStore.DeleteAsync(KeyReference.ForBackend(entry.Name), cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (SecureKeyStoreException ex)
                {
                    await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(true);
                    return (int)LaunchExitCode.ConfigRestoreFailure;
                }
            }

            settings.Backends.RemoveAll(b =>
                string.Equals(b.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            _settingsStore.Save(settings);
            Console.WriteLine($"Backend '{entry.Name}' removed.");
            return 0;
        });

        return cmd;
    }

    private Command CreateSetKeyCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The saved backend entry whose key to set or replace",
        };
        Option<string?> keyOption = new("--key")
        {
            Description =
                "API key to save in the platform credential store; skips the interactive prompt. " +
                "Warning: the value is visible in your shell history - prefer the interactive prompt",
        };

        Command cmd = new("set-key", "Set or replace the saved API key for a backend entry")
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

            BackendResolutionResult resolution = BackendResolution.Resolve(settings, name, endpoint: null, protocol: null, model: null);

            (Backend? entry, int resolutionExitCode) = await TryResolveEntryAsync(resolution)
                .ConfigureAwait(true);
            if (entry is null)
            {
                return resolutionExitCode;
            }

            string? secret = key ??
                             _keyPrompt.PromptForNewKey(entry.Name, entry.ApiKey is not null);

            if (secret is null)
            {
                Console.WriteLine($"No API key entered; backend '{entry.Name}' was left unchanged.");
                return 0;
            }

            string reference = KeyReference.ForBackend(entry.Name);
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
            Backend updated = entry with { ApiKey = reference };
            ConfigCommand.UpdateBackend(settings, updated);
            _settingsStore.Save(settings);

            Console.WriteLine($"API key for backend '{entry.Name}' saved.");
            return 0;
        });

        return cmd;
    }

    private Command CreateClearKeyCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The saved backend entry whose key to clear",
        };

        Command cmd = new("clear-key", "Remove the saved API key from a backend entry") { nameArg };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string name = result.GetRequiredValue(nameArg);

            (LauncherSettings settings, int loadExitCode) =
                await ConfigCommand.TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            BackendResolutionResult resolution = BackendResolution.Resolve(settings, name, endpoint: null, protocol: null, model: null);

            (Backend? entry, int resolutionExitCode) = await TryResolveEntryAsync(resolution)
                .ConfigureAwait(true);
            if (entry is null)
            {
                return resolutionExitCode;
            }

            if (entry.ApiKey is null)
            {
                // Idempotent on a keyless backend: no store entry, no settings write.
                Console.WriteLine($"Backend '{entry.Name}' has no saved key; nothing to clear.");
                return 0;
            }

            // A legacy plaintext key predates the store and has no entry there,
            // so only a reference is worth deleting from the credential store.
            if (KeyReference.IsReference(entry.ApiKey))
            {
                try
                {
                    await _keyStore.DeleteAsync(KeyReference.ForBackend(entry.Name), cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (SecureKeyStoreException ex)
                {
                    await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(true);
                    return (int)LaunchExitCode.ConfigRestoreFailure;
                }
            }

            Backend updated = entry with { ApiKey = null };
            ConfigCommand.UpdateBackend(settings, updated);
            _settingsStore.Save(settings);

            Console.WriteLine($"API key for backend '{entry.Name}' cleared.");
            return 0;
        });

        return cmd;
    }

    /// <summary>
    /// Binds a no-override resolution to the saved entry for the config verbs
    /// (remove, set-key, clear-key): the <c>Resolved</c> variant proves the
    /// backend non-null, <c>Failed</c> carries its own message and typed exit
    /// code, and <c>EmptySettings</c> is unreachable here - these verbs always
    /// pass the required name argument, so resolution either finds the entry
    /// or fails with the unknown-name class.
    /// </summary>
    private static async Task<(Backend? Entry, int ExitCode)> TryResolveEntryAsync(
        BackendResolutionResult resolution)
    {
        switch (resolution)
        {
            case BackendResolutionResult.Resolved resolved:
                return (resolved.Backend, 0);

            case BackendResolutionResult.Failed failed:
                await Console.Error.WriteLineAsync(failed.Message).ConfigureAwait(true);
                return (null, ExitCodeFor(failed.Failure));

            default:
                await Console.Error.WriteLineAsync("No backend is configured.").ConfigureAwait(true);
                return (null, (int)LaunchExitCode.UsageError);
        }
    }

    private static int ExitCodeFor(BackendResolutionFailure failure) =>
        failure is BackendResolutionFailure.UnknownBackendName
            or BackendResolutionFailure.DanglingDefaultBackend
            or BackendResolutionFailure.AbsentDefaultBackend
            ? (int)LaunchExitCode.UnknownBackendName
            : (int)LaunchExitCode.UsageError;
}
