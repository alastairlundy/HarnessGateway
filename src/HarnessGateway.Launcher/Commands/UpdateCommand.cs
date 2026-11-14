using HarnessGateway.Launcher.Core.Update;

namespace HarnessGateway.Launcher.Commands;

/// <summary>
/// The <c>update</c> top-level command and its <c>check</c> subcommand
/// (T003). The check compares this build against the latest GitHub release
/// and maps the outcome onto the documented exit codes (T014); it never
/// downloads, installs, or applies anything (D005). Bare <c>update</c>
/// refuses with a suggestion naming <c>update check</c> instead of
/// defaulting to a behavior.
/// </summary>
public sealed class UpdateCommand : Command
{
    /// <summary>
    /// Force-check flag: bypasses the 10-minute cooldown window (T017). The
    /// name is implementation-chosen here and documented for users with the
    /// rest of the command surface in TK004.
    /// </summary>
    public static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Bypass the 10-minute cooldown and check immediately",
    };

    private readonly UpdateChecker _checker;

    /// <summary>
    /// Production wiring: the system clock, the per-OS config directory the
    /// settings file lives in, and this build's pinned version. Constructed
    /// for every CLI run, so it touches no file and no network here.
    /// </summary>
    public UpdateCommand()
        : this(new UpdateChecker(
            new HttpClient(),
            TimeProvider.System,
            new UpdateCooldownStore(SettingsStore.GetPlatformConfigDirectory()),
            RootCommandFactory.InformationalVersion))
    {
    }

    internal UpdateCommand(UpdateChecker checker)
        : base("update", "Check for a newer HarnessGateway release")
    {
        _checker = checker;

        Command checkCommand = new(
            "check",
            "Compare this build's version against the latest published GitHub release")
        {
            ForceOption,
        };

        checkCommand.SetAction(async (result, cancellationToken) =>
        {
            bool force = result.GetValue(ForceOption);
            UpdateCheckResult check = await _checker.CheckAsync(force, cancellationToken)
                .ConfigureAwait(true);
            return await ReportAsync(check).ConfigureAwait(true);
        });

        Add(checkCommand);

        // Bare `update` must not guess (T003): refuse with the subcommand
        // name and run nothing - no network call, no cooldown stamp.
        SetAction(async (result, cancellationToken) =>
        {
            await Console.Error.WriteLineAsync(
                "'harnessgateway update' requires a subcommand: run 'harnessgateway update check' " +
                "to compare this build against the latest release.").ConfigureAwait(true);
            return (int)LaunchExitCode.UsageError;
        });
    }

    /// <summary>
    /// Prints the outcome and returns its documented exit code: up to date
    /// and the cooldown notice are 0 (T018), update available and check
    /// failed are the two appended contract codes (T014).
    /// </summary>
    private async Task<int> ReportAsync(UpdateCheckResult check)
    {
        switch (check.Outcome)
        {
            case UpdateCheckOutcome.CooldownActive:
                // T018: the notice exits 0 with the remaining time on stdout;
                // scripts wanting a fresh check pass --force.
                Console.WriteLine(
                    $"Update check is cooling down ({FormatRemaining(check.CooldownRemaining)} remaining); " +
                    "run 'harnessgateway update check --force' to check now.");
                return (int)LaunchExitCode.Ok;

            case UpdateCheckOutcome.UpToDate:
                Console.WriteLine($"HarnessGateway {_checker.CurrentVersion} is up to date.");
                return (int)LaunchExitCode.Ok;

            case UpdateCheckOutcome.UpdateAvailable:
                Console.WriteLine(
                    $"A newer HarnessGateway release is available: {check.LatestVersion} " +
                    $"(this build: {_checker.CurrentVersion}).");
                return (int)LaunchExitCode.UpdateAvailable;

            case UpdateCheckOutcome.CheckFailed:
                await Console.Error.WriteLineAsync($"Update check failed: {check.FailureDetail}")
                    .ConfigureAwait(true);
                return (int)LaunchExitCode.CheckFailed;

            default:
                // Unreachable while UpdateCheckOutcome has four members: a
                // new outcome must be mapped here deliberately, not absorbed
                // into whichever code looks closest.
                throw new InvalidOperationException(
                    $"Unknown update-check outcome: {check.Outcome}.");
        }
    }

    /// <summary>
    /// The remaining window as whole minutes and seconds, rounded up so a
    /// skipped check never reports "0s remaining" while still inside it.
    /// </summary>
    private static string FormatRemaining(TimeSpan remaining)
    {
        int totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return $"{totalSeconds / 60}m {totalSeconds % 60}s";
    }
}
