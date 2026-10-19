namespace HarnessGateway.Launcher.Commands;

public sealed class ConfigCommand : Command
{
    private readonly SettingsStore _settingsStore;

    public ConfigCommand()
        : this(new SettingsStore())
    {
    }

    internal ConfigCommand(SettingsStore settingsStore)
        : this(
            settingsStore,
            SecureKeyStoreFactory.Create(settingsStore.SettingsDirectory))
    {
    }

    internal ConfigCommand(SettingsStore settingsStore, ISecureKeyStore keyStore)
        : this(settingsStore, keyStore, new TerminalKeyPrompt())
    {
    }

    internal ConfigCommand(SettingsStore settingsStore, ISecureKeyStore keyStore, IKeyPrompt keyPrompt)
        : base("config", "Show and manage saved default configuration")
    {
        _settingsStore = settingsStore;

        Command showVerb = new("show", "Display the current default backend configuration");
        Command getVerb = CreateGetCommand();
        Command setVerb = CreateSetCommand();

        Add(showVerb);
        Add(getVerb);
        Add(setVerb);
        Add(new BackendCommands(settingsStore, keyStore, keyPrompt));

        showVerb.SetAction((result) =>
        {
            if (!TryLoadSettings(_settingsStore, out LauncherSettings settings, out int loadExitCode))
            {
                return loadExitCode;
            }

            Backend? backend = ResolveDefaultBackend(settings);

            if (backend is null)
            {
                Console.WriteLine("No default backend configured.");
                if (settings.Backends.Count > 0)
                {
                    Console.WriteLine(
                        "Run 'harnessgateway config set defaultBackend <name>' to select one of the saved entries.");
                }
                else
                {
                    Console.WriteLine(
                        "Run 'harnessgateway config backend add <name> --endpoint <url> --protocol <protocol>' " +
                        "to create one.");
                }

                return 0;
            }

            Console.WriteLine($"Endpoint: {backend.BaseUri}");
            Console.WriteLine($"Protocol: {FormatProtocol(backend.Protocol)}");
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
                "The typed settings key: defaultBackend, schemaVersion, backends, " +
                "backends.<name>.endpoint, backends.<name>.protocol, or backends.<name>.defaultModel",
        };

        Command cmd = new("get", "Read a non-secret setting by its typed key") { keyArg };

        cmd.SetAction((result) =>
        {
            string key = result.GetRequiredValue(keyArg);

            if (!TryLoadSettings(_settingsStore, out LauncherSettings settings, out int loadExitCode))
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
                "The typed settings key: defaultBackend, " +
                "backends.<name>.endpoint, backends.<name>.protocol, or backends.<name>.defaultModel",
        };
        Argument<string> valueArg = new("value")
        {
            Description = "The new value for the key",
        };

        Command cmd = new("set", "Write a non-secret setting by its typed key") { keyArg, valueArg };

        cmd.SetAction((result) =>
        {
            string key = result.GetRequiredValue(keyArg);
            string value = result.GetRequiredValue(valueArg);

            if (!TryLoadSettings(_settingsStore, out LauncherSettings settings, out int loadExitCode))
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
    /// default-pointer write has its own class (unknown backend name = 8);
    /// every other addressing failure is a usage error (2).
    /// </summary>
    internal static int ExitCodeFor(ConfigKeyFailure failure) =>
        failure == ConfigKeyFailure.UnknownBackendName
            ? (int)LaunchExitCode.UnknownBackendName
            : (int)LaunchExitCode.UsageError;

    internal static bool TryLoadSettings(SettingsStore settingsStore, out LauncherSettings settings, out int exitCode)
    {
        try
        {
            settings = settingsStore.Load();
            exitCode = 0;
            return true;
        }
        catch (UnsupportedSettingsSchemaException ex)
        {
            Console.Error.WriteLine(ex.Message);
            settings = new LauncherSettings();
            exitCode = (int)LaunchExitCode.UnsupportedSettingsSchema;
            return false;
        }
        catch (SecureKeyStoreException ex)
        {
            Console.Error.WriteLine(ex.Message);
            settings = new LauncherSettings();
            exitCode = (int)LaunchExitCode.ConfigRestoreFailure;
            return false;
        }
    }

    internal static Backend? ResolveDefaultBackend(LauncherSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.DefaultBackend))
        {
            return settings.Backends.FirstOrDefault(b =>
                string.Equals(b.Name, settings.DefaultBackend, StringComparison.OrdinalIgnoreCase));
        }

        return settings.Backends.FirstOrDefault();
    }

    internal static void UpdateBackend(LauncherSettings settings, Backend updated)
    {
        int index = settings.Backends.FindIndex(b =>
            string.Equals(b.Name, updated.Name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            settings.Backends[index] = updated;
        }
        else
        {
            settings.Backends.Add(updated);
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
