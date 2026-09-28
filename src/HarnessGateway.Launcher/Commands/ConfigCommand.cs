using System.CommandLine;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Settings.Security;

namespace HarnessGateway.Launcher.Commands;

public sealed class ConfigCommand : Command
{
    private readonly SettingsStore _settingsStore;
    private readonly ISecureKeyStore _keyStore;

    public ConfigCommand()
        : this(new SettingsStore())
    {
    }

    internal ConfigCommand(SettingsStore settingsStore)
        : this(
            settingsStore,
            SecureKeyStoreFactory.Create(Path.GetDirectoryName(settingsStore.SettingsFilePath)!))
    {
    }

    internal ConfigCommand(SettingsStore settingsStore, ISecureKeyStore keyStore)
        : base("config", "Show and manage saved default configuration")
    {
        _settingsStore = settingsStore;
        _keyStore = keyStore;

        Command showVerb = new("show", "Display the current default backend configuration");
        Command setEndpointVerb = CreateSetDefaultEndpointCommand();
        Command setKeyVerb = CreateSetSavedKeyCommand();
        Command clearKeyVerb = new("clear-saved-key", "Remove the saved API key from the default backend");
        Command setModelVerb = CreateSetDefaultModelCommand();

        Add(showVerb);
        Add(setEndpointVerb);
        Add(setKeyVerb);
        Add(clearKeyVerb);
        Add(setModelVerb);

        showVerb.SetAction((result) =>
        {
            if (!TryLoadSettings(out LauncherSettings settings, out int loadExitCode))
            {
                return loadExitCode;
            }

            Backend? backend = ResolveDefaultBackend(settings);

            if (backend is null)
            {
                Console.WriteLine("No default backend configured.");
                Console.WriteLine("Run 'harnessgateway config set-default-endpoint' to configure one.");
                return 0;
            }

            Console.WriteLine($"Endpoint: {backend.BaseUri}");
            Console.WriteLine($"Protocol: {FormatProtocol(backend.Protocol)}");
            Console.WriteLine($"Saved key: {(string.IsNullOrEmpty(backend.ApiKey) ? "(not set)" : "(set)")}");
            Console.WriteLine($"Default model: {backend.DefaultModel ?? "(not set)"}");
            return 0;
        });

        clearKeyVerb.SetAction(async (result, cancellationToken) =>
        {
            if (!TryLoadSettings(out LauncherSettings settings, out int loadExitCode))
            {
                return loadExitCode;
            }

            Backend? backend = ResolveDefaultBackend(settings);

            if (backend is null)
            {
                Console.Error.WriteLine("No default backend configured.");
                return (int)LaunchExitCode.UsageError;
            }

            // A legacy plaintext key predates the store and has no entry there,
            // so only a reference is worth deleting from the credential store.
            if (KeyReference.IsReference(backend.ApiKey))
            {
                try
                {
                    await _keyStore.DeleteAsync(
                        KeyReference.ForBackend(backend.Name),
                        cancellationToken);
                }
                catch (SecureKeyStoreException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return (int)LaunchExitCode.ConfigRestoreFailure;
                }
            }

            Backend updated = backend with { ApiKey = null };
            UpdateBackend(settings, updated);
            _settingsStore.Save(settings);
            Console.WriteLine("Saved key cleared.");
            return 0;
        });
    }

    private bool TryLoadSettings(out LauncherSettings settings, out int exitCode)
    {
        try
        {
            settings = _settingsStore.Load();
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

    private Command CreateSetDefaultEndpointCommand()
    {
        Argument<string> endpointArg = new("endpoint")
        {
            Description = "The backend endpoint URL"
        };
        Argument<string> protocolArg = new("protocol")
        {
            Description = "The protocol (openai-chat, openai-responses, anthropic)"
        };

        Command cmd = new("set-default-endpoint", "Set the default backend endpoint and protocol");
        cmd.Add(endpointArg);
        cmd.Add(protocolArg);

        cmd.SetAction((result) =>
        {
            string endpoint = result.GetRequiredValue(endpointArg);
            string protocol = result.GetRequiredValue(protocolArg);

            if (!LaunchCommand.IsValidProtocol(protocol))
            {
                Console.Error.WriteLine($"Invalid protocol '{protocol}'. Must be one of: openai-chat, openai-responses, anthropic");
                return (int)LaunchExitCode.UsageError;
            }

            Uri parsedEndpoint = LaunchCommand.ParseEndpoint(endpoint);
            ProtocolKind parsedProtocol = LaunchCommand.ParseProtocol(protocol);

            if (!TryLoadSettings(out LauncherSettings settings, out int loadExitCode))
            {
                return loadExitCode;
            }

            Backend? existingBackend = ResolveDefaultBackend(settings);
            string name = existingBackend?.Name ?? "default";

            Backend updated = new()
            {
                Name = name,
                BaseUri = parsedEndpoint,
                Protocol = parsedProtocol,
                ApiKey = existingBackend?.ApiKey,
                DefaultModel = existingBackend?.DefaultModel
            };

            UpdateBackend(settings, updated);
            if (string.IsNullOrEmpty(settings.DefaultBackend))
            {
                settings = settings with { DefaultBackend = name };
            }

            _settingsStore.Save(settings);
            Console.WriteLine($"Default endpoint set to {endpoint} ({protocol}).");
            return 0;
        });

        return cmd;
    }

    private Command CreateSetSavedKeyCommand()
    {
        Argument<string> keyArg = new("key")
        {
            Description = "The API key to save"
        };

        Command cmd = new("set-saved-key", "Save an API key for the default backend");
        cmd.Add(keyArg);

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string key = result.GetRequiredValue(keyArg);
            if (!TryLoadSettings(out LauncherSettings settings, out int loadExitCode))
            {
                return loadExitCode;
            }

            Backend? backend = ResolveDefaultBackend(settings);

            if (backend is null)
            {
                Console.Error.WriteLine("No default backend configured. Run 'harnessgateway config set-default-endpoint' first.");
                return (int)LaunchExitCode.UsageError;
            }

            string reference = KeyReference.ForBackend(backend.Name);
            try
            {
                await _keyStore.StoreAsync(reference, key, cancellationToken);
            }
            catch (SecureKeyStoreException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return (int)LaunchExitCode.ConfigRestoreFailure;
            }

            Backend updated = backend with { ApiKey = reference };
            UpdateBackend(settings, updated);
            _settingsStore.Save(settings);
            Console.WriteLine("Saved key updated.");
            return 0;
        });

        return cmd;
    }

    private Command CreateSetDefaultModelCommand()
    {
        Argument<string> modelArg = new("model")
        {
            Description = "The default model name"
        };

        Command cmd = new("set-default-model", "Set the default model for the default backend");
        cmd.Add(modelArg);

        cmd.SetAction((result) =>
        {
            string model = result.GetRequiredValue(modelArg);
            if (!TryLoadSettings(out LauncherSettings settings, out int loadExitCode))
            {
                return loadExitCode;
            }

            Backend? backend = ResolveDefaultBackend(settings);

            if (backend is null)
            {
                Console.Error.WriteLine("No default backend configured. Run 'harnessgateway config set-default-endpoint' first.");
                return (int)LaunchExitCode.UsageError;
            }

            Backend updated = backend with { DefaultModel = model };
            UpdateBackend(settings, updated);
            _settingsStore.Save(settings);
            Console.WriteLine($"Default model set to {model}.");
            return 0;
        });

        return cmd;
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
        _ => protocol.ToString()
    };
}
