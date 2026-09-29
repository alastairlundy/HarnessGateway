using System.CommandLine;
using HarnessGateway.Launcher.Core.Backends;
using HarnessGateway.Launcher.Core.Settings;
using HarnessGateway.Launcher.Core.Settings.Security;

namespace HarnessGateway.Launcher.Commands;

internal sealed class BackendCommands : Command
{
    private readonly SettingsStore _settingsStore;
    private readonly ISecureKeyStore _keyStore;
    private readonly IKeyPrompt _keyPrompt;

    internal BackendCommands(SettingsStore settingsStore, ISecureKeyStore keyStore, IKeyPrompt keyPrompt)
        : base("backend", "Manage saved backend profiles")
    {
        _settingsStore = settingsStore;
        _keyStore = keyStore;
        _keyPrompt = keyPrompt;

        Add(CreateAddCommand());
        Add(CreateSwitchCommand());
        Add(CreateListCommand());
        Add(CreateRemoveCommand());
    }

    private Command CreateAddCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The backend entry name; an existing entry with the same name is replaced"
        };
        Option<string?> endpointOption = new("--endpoint")
        {
            Description = "The backend endpoint URL (required)"
        };
        Option<string?> protocolOption = new("--protocol")
        {
            Description = "The protocol (openai-chat, openai-responses, anthropic) (required)"
        };
        Option<string?> keyOption = new("--key")
        {
            Description =
                "API key to save in the platform credential store; skips the interactive prompt. " +
                "Warning: the value is visible in your shell history - prefer the interactive prompt"
        };

        Command cmd = new("add", "Create or replace a saved backend entry");
        cmd.Add(nameArg);
        cmd.Add(endpointOption);
        cmd.Add(protocolOption);
        cmd.Add(keyOption);

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string name = result.GetRequiredValue(nameArg);
            string? endpoint = result.GetValue(endpointOption);
            string? protocol = result.GetValue(protocolOption);
            string? key = result.GetValue(keyOption);

            if (!ConfigCommand.TryLoadSettings(_settingsStore, out LauncherSettings settings, out int loadExitCode))
            {
                return loadExitCode;
            }

            if (endpoint is null && protocol is null)
            {
                Console.Error.WriteLine("config backend add requires --endpoint and --protocol");
                return (int)LaunchExitCode.UsageError;
            }

            BackendResolutionResult resolution = BackendResolution.Resolve(settings, name, endpoint, protocol, null);
            if (!resolution.IsSuccess && resolution.Failure != BackendResolutionFailure.UnknownBackendName)
            {
                Console.Error.WriteLine(resolution.Message);
                return ExitCodeFor(resolution.Failure);
            }

            bool isCreate = !resolution.IsSuccess;
            Backend entry = isCreate
                ? new Backend
                {
                    Name = name,
                    BaseUri = LaunchCommand.ParseEndpoint(endpoint!),
                    Protocol = LaunchCommand.ParseProtocol(protocol!)
                }
                : resolution.Backend!;

            string? secret;
            if (key is not null)
            {
                secret = key;
            }
            else
            {
                secret = _keyPrompt.PromptForNewKey(entry.Name, entry.ApiKey is not null);
            }

            if (secret is not null)
            {
                string reference = KeyReference.ForBackend(entry.Name);
                try
                {
                    await _keyStore.StoreAsync(reference, secret, cancellationToken);
                }
                catch (SecureKeyStoreException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return (int)LaunchExitCode.ConfigRestoreFailure;
                }

                entry = entry with { ApiKey = reference };
            }

            ConfigCommand.UpdateBackend(settings, entry);
            _settingsStore.Save(settings);

            Console.WriteLine($"Backend '{entry.Name}' {(isCreate ? "created" : "updated")}.");
            if (isCreate)
            {
                Console.WriteLine($"Run 'harnessgateway config backend switch {entry.Name}' to make it the default.");
            }

            return 0;
        });

        return cmd;
    }

    private Command CreateSwitchCommand()
    {
        Argument<string> nameArg = new("name")
        {
            Description = "The saved backend entry to make the default"
        };

        Command cmd = new("switch", "Make a saved backend entry the default");
        cmd.Add(nameArg);

        cmd.SetAction((result) =>
        {
            string name = result.GetRequiredValue(nameArg);

            if (!ConfigCommand.TryLoadSettings(_settingsStore, out LauncherSettings settings, out int loadExitCode))
            {
                return loadExitCode;
            }

            BackendResolutionResult resolution = BackendResolution.Resolve(settings, name, null, null, null);
            if (!resolution.IsSuccess)
            {
                Console.Error.WriteLine(resolution.Message);
                return ExitCodeFor(resolution.Failure);
            }

            Backend entry = resolution.Backend!;

            if (!string.Equals(settings.DefaultBackend, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                settings = settings with { DefaultBackend = entry.Name };
                _settingsStore.Save(settings);
            }

            Console.WriteLine($"backend '{entry.Name}' is now the default");
            return 0;
        });

        return cmd;
    }

    private Command CreateListCommand()
    {
        Command cmd = new("list", "List saved backend entries with the default marked");

        cmd.SetAction((result) =>
        {
            if (!ConfigCommand.TryLoadSettings(_settingsStore, out LauncherSettings settings, out int loadExitCode))
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
                    "run 'harnessgateway config backend switch <name>' to choose one)");
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
            Description = "The saved backend entry to remove"
        };

        Command cmd = new("remove", "Delete a saved backend entry");
        cmd.Add(nameArg);

        cmd.SetAction(async (result, cancellationToken) =>
        {
            string name = result.GetRequiredValue(nameArg);

            if (!ConfigCommand.TryLoadSettings(_settingsStore, out LauncherSettings settings, out int loadExitCode))
            {
                return loadExitCode;
            }

            BackendResolutionResult resolution = BackendResolution.Resolve(settings, name, null, null, null);
            if (!resolution.IsSuccess)
            {
                Console.Error.WriteLine(resolution.Message);
                return ExitCodeFor(resolution.Failure);
            }

            Backend entry = resolution.Backend!;

            if (settings.Backends.Count == 1)
            {
                Console.Error.WriteLine(
                    $"Cannot remove '{entry.Name}': it is the last saved backend; " +
                    "settings must always keep at least one backend.");
                return (int)LaunchExitCode.UsageError;
            }

            if (string.Equals(settings.DefaultBackend, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"Cannot remove '{entry.Name}': it is the current default backend. " +
                    "Run 'harnessgateway config backend switch <name>' to choose a new default first.");
                return (int)LaunchExitCode.UsageError;
            }

            if (KeyReference.IsReference(entry.ApiKey))
            {
                try
                {
                    await _keyStore.DeleteAsync(KeyReference.ForBackend(entry.Name), cancellationToken);
                }
                catch (SecureKeyStoreException ex)
                {
                    Console.Error.WriteLine(ex.Message);
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

    private static int ExitCodeFor(BackendResolutionFailure failure) =>
        failure is BackendResolutionFailure.UnknownBackendName
            or BackendResolutionFailure.DanglingDefaultBackend
            or BackendResolutionFailure.AbsentDefaultBackend
            ? (int)LaunchExitCode.UnknownBackendName
            : (int)LaunchExitCode.UsageError;
}

internal interface IKeyPrompt
{
    string? PromptForNewKey(string backendName, bool entryHasKey);
}

internal sealed class TerminalKeyPrompt : IKeyPrompt
{
    public string? PromptForNewKey(string backendName, bool entryHasKey)
    {
        if (Console.IsInputRedirected)
        {
            return null;
        }

        string verb = entryHasKey ? "Replace" : "Set";
        Console.Write($"{verb} an API key for '{backendName}'? [y/N] ");
        string? answer = Console.ReadLine();

        if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Console.Write("API key (input hidden, Enter to confirm): ");
        string secret = string.Empty;

        while (true)
        {
            ConsoleKeyInfo info = Console.ReadKey(intercept: true);

            if (info.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (info.Key == ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                {
                    secret = secret[..^1];
                }

                continue;
            }

            if (info.KeyChar != '\0')
            {
                secret += info.KeyChar;
            }
        }

        return secret.Length == 0 ? null : secret;
    }
}
