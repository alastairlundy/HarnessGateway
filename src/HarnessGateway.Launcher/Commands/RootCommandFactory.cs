/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.CommandLine.Invocation;
using System.Reflection;
using HarnessGateway.Launcher.Input;

namespace HarnessGateway.Launcher.Commands;

/// <summary>
/// Builds the CLI root command: the standard <c>--version</c> flag wired to
/// this build's identity, the <c>launch</c>, <c>config</c>, <c>provider</c>,
/// and <c>update</c> subcommands, and the root action (the interactive
/// wizard). Root wiring lives here rather
/// than inline in Program.cs so tests can exercise the real parse-and-invoke
/// path: System.CommandLine fails silently when wiring is missing, which is
/// exactly the bug LaunchCommandInvocationTests exists to catch, and a flag
/// that only ever ran untested would re-expose it.
/// </summary>
public static class RootCommandFactory
{
    /// <summary>
    /// The Launcher assembly's informational version: the csproj pin
    /// (<c>0.1.0</c>) and nothing more. The csproj sets
    /// <c>IncludeSourceRevisionInInformationalVersion=false</c> so SourceLink's
    /// "+&lt;sha&gt;" commit suffix is never stamped on - the flag's output is
    /// exactly the pin, bare Major.Minor.Patch: the build's identity in the field.
    /// </summary>
    public static string InformationalVersion { get; } = ReadInformationalVersion();

    /// <summary>
    /// The production root command. <paramref name="wizardAction"/> backs both
    /// the no-args root action and a bare <c>launch</c>, so tests can pass a
    /// stub to observe which entry point ran.
    /// </summary>
    public static RootCommand Create(
        LaunchOrchestrator orchestrator,
        Func<CancellationToken, Task<int>> wizardAction,
        IEnumerable<IHarnessAdapter> adapters,
        SettingsStore settingsStore,
        ISecureKeyStore keyStore)
    {
        RootCommand rootCommand = new("harnessgateway - Configure and launch agent harnesses")
        {
            new LaunchCommand(orchestrator, wizardAction, adapters),
            new ConfigCommand(settingsStore),
            // The provider surface is top-level - `harnessgateway
            // provider ...` - with no deprecated backend alias anywhere.
            new ProviderCommands(settingsStore, keyStore, new TerminalKeyPrompt()),
            // `update` is check-only and refuses when invoked
            // bare; the production instance is built here so the tests drive
            // the same registration path the entry point uses.
            new UpdateCommand(),
        };

        // RootCommand ships a standard --version option: its help entry, its
        // "cannot be combined with other arguments" validator, and a default
        // action that prints the ENTRY assembly's informational version. In
        // production the entry assembly is the launcher, but not in every
        // process that parses this command - a test host would report its own
        // version. Re-point the action at the Launcher assembly so the flag
        // always reports this build's identity (the csproj pin), whatever the
        // host is.
        VersionOption versionOption = rootCommand.Options.OfType<VersionOption>().Single();
        versionOption.Action = new PrintInformationalVersionAction();

        // The wizard is the no-args root action and, via LaunchCommand's own
        // action, also what a bare `launch` runs - one implementation, two
        // entry points, so the two can never drift in exception handling or
        // exit codes. `--version` short-circuits before this action via the
        // option above: a bug report can name its build even when the
        // configuration behind the wizard is broken.
        rootCommand.SetAction(async (result, cancellationToken) =>
            await wizardAction(cancellationToken).ConfigureAwait(true));

        return rootCommand;
    }

    /// <summary>
    /// Prints <see cref="InformationalVersion"/> and exits 0.
    /// </summary>
    private sealed class PrintInformationalVersionAction : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            Console.WriteLine(InformationalVersion);
            return 0;
        }
    }

    private static string ReadInformationalVersion()
    {
        // Read from the Launcher assembly (not GetEntryAssembly): the entry
        // assembly is the test host when the suite parses this command.
        Assembly assembly = typeof(RootCommandFactory).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }
}
