using System.CommandLine;
using LmProxy.AgentLauncher.Core.Backends;
using LmProxy.AgentLauncher.Core.Settings;

namespace LmProxy.AgentLauncher.Commands;

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

        var showVerb = new Command("show", "Display the current default backend configuration");
        var setEndpointVerb = CreateSetDefaultEndpointCommand();
        var setKeyVerb = CreateSetSavedKeyCommand();
        var clearKeyVerb = new Command("clear-saved-key", "Remove the saved API key from the default backend");
        var setModelVerb = CreateSetDefaultModelCommand();

        Add(showVerb);
        Add(setEndpointVerb);
        Add(setKeyVerb);
        Add(clearKeyVerb);
        Add(setModelVerb);

        showVerb.SetAction((result) =>
        {
            var settings = _settingsStore.Load();
            var backend = ResolveDefaultBackend(settings);

            if (backend is null)
            {
                Console.WriteLine("No default backend configured.");
                Console.WriteLine("Run 'lmlaunch config set-default-endpoint' to configure one.");
                return 0;
            }

            Console.WriteLine($"Endpoint: {backend.BaseUri}");
            Console.WriteLine($"Protocol: {FormatProtocol(backend.Protocol)}");
            Console.WriteLine($"Saved key: {(string.IsNullOrEmpty(backend.ApiKey) ? "(not set)" : "(set)")}");
            Console.WriteLine($"Default model: {backend.DefaultModel ?? "(not set)"}");
            return 0;
        });

        clearKeyVerb.SetAction((result) =>
        {
            var settings = _settingsStore.Load();
            var backend = ResolveDefaultBackend(settings);

            if (backend is null)
            {
                Console.Error.WriteLine("No default backend configured.");
                return (int)LaunchExitCode.UsageError;
            }

            var updated = backend with { ApiKey = null };
            UpdateBackend(settings, updated);
            _settingsStore.Save(settings);
            Console.WriteLine("Saved key cleared.");
            return 0;
        });
    }

    private Command CreateSetDefaultEndpointCommand()
    {
        var endpointArg = new Argument<string>("endpoint")
        {
            Description = "The backend endpoint URL"
        };
        var protocolArg = new Argument<string>("protocol")
        {
            Description = "The protocol (openai-chat, openai-responses, anthropic)"
        };

        var cmd = new Command("set-default-endpoint", "Set the default backend endpoint and protocol");
        cmd.Add(endpointArg);
        cmd.Add(protocolArg);

        cmd.SetAction((result) =>
        {
            var endpoint = result.GetRequiredValue(endpointArg);
            var protocol = result.GetRequiredValue(protocolArg);

            if (!LaunchCommand.IsValidProtocol(protocol))
            {
                Console.Error.WriteLine($"Invalid protocol '{protocol}'. Must be one of: openai-chat, openai-responses, anthropic");
                return (int)LaunchExitCode.UsageError;
            }

            var parsedEndpoint = LaunchCommand.ParseEndpoint(endpoint);
            var parsedProtocol = LaunchCommand.ParseProtocol(protocol);

            var settings = _settingsStore.Load();

            var existingBackend = ResolveDefaultBackend(settings);
            var name = existingBackend?.Name ?? "default";

            var updated = new Backend
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
        var keyArg = new Argument<string>("key")
        {
            Description = "The API key to save"
        };

        var cmd = new Command("set-saved-key", "Save an API key for the default backend");
        cmd.Add(keyArg);

        cmd.SetAction((result) =>
        {
            var key = result.GetRequiredValue(keyArg);
            var settings = _settingsStore.Load();
            var backend = ResolveDefaultBackend(settings);

            if (backend is null)
            {
                Console.Error.WriteLine("No default backend configured. Run 'lmlaunch config set-default-endpoint' first.");
                return (int)LaunchExitCode.UsageError;
            }

            var updated = backend with { ApiKey = key };
            UpdateBackend(settings, updated);
            _settingsStore.Save(settings);
            Console.WriteLine("Saved key updated.");
            return 0;
        });

        return cmd;
    }

    private Command CreateSetDefaultModelCommand()
    {
        var modelArg = new Argument<string>("model")
        {
            Description = "The default model name"
        };

        var cmd = new Command("set-default-model", "Set the default model for the default backend");
        cmd.Add(modelArg);

        cmd.SetAction((result) =>
        {
            var model = result.GetRequiredValue(modelArg);
            var settings = _settingsStore.Load();
            var backend = ResolveDefaultBackend(settings);

            if (backend is null)
            {
                Console.Error.WriteLine("No default backend configured. Run 'lmlaunch config set-default-endpoint' first.");
                return (int)LaunchExitCode.UsageError;
            }

            var updated = backend with { DefaultModel = model };
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
        var index = settings.Backends.FindIndex(b =>
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
