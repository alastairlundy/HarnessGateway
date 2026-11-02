namespace HarnessGateway.Launcher.Commands;

public sealed class ConfigCommand : Command
{
    private readonly SettingsStore _settingsStore;

    public ConfigCommand()
        : this(new SettingsStore())
    {
    }

    internal ConfigCommand(SettingsStore settingsStore)
        : base("config", "Show and manage saved default configuration")
    {
        _settingsStore = settingsStore;

        Command showVerb = new("show", "Display the current default provider configuration");
        Command getVerb = CreateGetCommand();
        Command setVerb = CreateSetCommand();

        Add(showVerb);
        Add(getVerb);
        Add(setVerb);

        showVerb.SetAction(async (result, cancellationToken) =>
        {
            (LauncherSettings settings, int loadExitCode) =
                await TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            Provider? backend = ResolveDefaultProvider(settings);

            if (backend is null)
            {
                Console.WriteLine("No default provider configured.");
                if (settings.Providers.Count > 0)
                {
                    Console.WriteLine(
                        "Run 'harnessgateway config set defaultProvider <name>' to select one of the saved entries.");
                }
                else
                {
                    Console.WriteLine(
                        "Run 'harnessgateway provider add <name> --endpoint <url> --protocol <protocol>' " +
                        "to create one.");
                }

                return 0;
            }

            Console.WriteLine($"Endpoint: {backend.BaseUri}");
            Console.WriteLine($"Protocol: {FormatProtocol(backend.DefaultProtocol)}");
            Console.WriteLine($"Saved key: {(string.IsNullOrEmpty(backend.ApiKey) ? "(not set)" : "(set)")}");
            Console.WriteLine($"Default model: {backend.DefaultModel ?? "(not set)"}");
            return 0;
        });
    }

    private Command CreateGetCommand()
    {
        Argument<string> keyArg = new("key")
        {
            Description =
                "The typed settings key: defaultProvider, schemaVersion, providers, " +
                "providers.<name>.endpoint, providers.<name>.protocol, or providers.<name>.defaultModel",
        };

        Command cmd = new("get", "Read a non-secret setting by its typed key") { keyArg };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string key = result.GetRequiredValue(keyArg);

            (LauncherSettings settings, int loadExitCode) =
                await TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            ConfigKeyResult read = ConfigKeyResolver.Get(settings, key);
            if (!read.IsSuccess)
            {
                Console.Error.WriteLine(read.Message);
                return ExitCodeFor(read.Failure);
            }

            Console.WriteLine(read.Value);
            return 0;
        });

        return cmd;
    }

    private Command CreateSetCommand()
    {
        Argument<string> keyArg = new("key")
        {
            Description =
                "The typed settings key: defaultProvider, " +
                "providers.<name>.endpoint, providers.<name>.protocol, or providers.<name>.defaultModel",
        };
        Argument<string> valueArg = new("value")
        {
            Description = "The new value for the key",
        };

        Command cmd = new("set", "Write a non-secret setting by its typed key") { keyArg, valueArg };

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string key = result.GetRequiredValue(keyArg);
            string value = result.GetRequiredValue(valueArg);

            (LauncherSettings settings, int loadExitCode) =
                await TryLoadSettingsAsync(_settingsStore, cancellationToken).ConfigureAwait(true);
            if (loadExitCode != 0)
            {
                return loadExitCode;
            }

            ConfigKeyResult write = ConfigKeyResolver.Set(settings, key, value);
            if (!write.IsSuccess)
            {
                Console.Error.WriteLine(write.Message);
                return ExitCodeFor(write.Failure);
            }

            // The default-pointer write is idempotent: when the pointer already
            // names this entry the file is left untouched.
            if (!write.AlreadySatisfied)
            {
                if (write.Settings is not { } toSave)
                {
                    throw new InvalidOperationException(
                        "A successful config set must carry the settings snapshot to persist.");
                }

                _settingsStore.Save(toSave);
            }

            Console.WriteLine(write.Message);
            return 0;
        });

        return cmd;
    }

    /// <summary>
    /// Maps typed key failures to the documented exit-code contract: only the
    /// default-pointer write has its own class (unknown provider name = 8);
    /// every other addressing failure is a usage error (2).
    /// </summary>
    internal static int ExitCodeFor(ConfigKeyFailure failure) =>
        failure == ConfigKeyFailure.UnknownProviderName
            ? (int)LaunchExitCode.UnknownProviderName
            : (int)LaunchExitCode.UsageError;

    internal static async Task<(LauncherSettings Settings, int ExitCode)> TryLoadSettingsAsync(
        SettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        try
        {
            LauncherSettings settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            return (settings, 0);
        }
        catch (UnsupportedSettingsSchemaException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (new LauncherSettings(), (int)LaunchExitCode.UnsupportedSettingsSchema);
        }
        catch (SecureKeyStoreException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (new LauncherSettings(), (int)LaunchExitCode.ConfigRestoreFailure);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The load-time migration writes (backup marker, temp file,
            // atomic move); an operational fault there is surfaced as a
            // message - no stack trace - and mapped to the documented
            // ConfigRestoreFailure rather than escaping as an unhandled
            // exception with an exit code outside the contract.
            Console.Error.WriteLine($"Settings load failed: {ex.Message}");
            return (new LauncherSettings(), (int)LaunchExitCode.ConfigRestoreFailure);
        }
    }

    internal static Provider? ResolveDefaultProvider(LauncherSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.DefaultProvider))
        {
            return settings.Providers.FirstOrDefault(b =>
                string.Equals(b.Name, settings.DefaultProvider, StringComparison.OrdinalIgnoreCase));
        }

        return settings.Providers.FirstOrDefault();
    }

    internal static void UpdateProvider(LauncherSettings settings, Provider updated)
    {
        int index = settings.Providers.FindIndex(b =>
            string.Equals(b.Name, updated.Name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            settings.Providers[index] = updated;
        }
        else
        {
            settings.Providers.Add(updated);
        }
    }

    private static string FormatProtocol(ProtocolKind protocol) => protocol switch
    {
        ProtocolKind.OpenAiChat => "openai-chat",
        ProtocolKind.OpenAiResponses => "openai-responses",
        ProtocolKind.Anthropic => "anthropic",
        _ => protocol.ToString(),
    };
}
