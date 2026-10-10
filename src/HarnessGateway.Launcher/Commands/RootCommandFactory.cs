using System.CommandLine.Invocation;
using System.Reflection;

namespace HarnessGateway.Launcher.Commands;

/// <summary>
/// Builds the CLI root command: the standard <c>--version</c> flag wired to
/// this build's identity, the <c>launch</c> and <c>config</c> subcommands, and
/// the root action (the interactive wizard). Root wiring lives here rather
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
    /// exactly the pin, bare Major.Minor.Patch. This is the build's identity
    /// in the field. It is deliberately not the
    /// settings <c>schemaVersion</c>, which describes the settings file's
    /// shape and is reported only by <c>config get schemaVersion</c> - the
    /// two never share a surface.
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
            new ConfigCommand(settingsStore, keyStore),
        };

        // RootCommand ships a standard --version option: its help entry, its
        // "cannot be combined with other arguments" validator, and a default
        // action that prints the ENTRY assembly's informational version. In
        // production the entry assembly is the launcher, but not in every
        // process that parses this command - a test host would report its own
        // version. Re-point the action at the Launcher assembly so the flag
        // always reports this build's identity (the csproj pin), whatever the
        // host is. Adding a second, duplicate `--version` option instead is
        // not an option: the built-in symbol wins the token and the duplicate
        // silently never runs.
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
    /// Prints the Launcher assembly's informational version and exits 0.
    /// Nothing else is ever reported: not settings <c>schemaVersion</c>, not
    /// the entry assembly, not help text.
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
