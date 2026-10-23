namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// Closed family of terminal launch results: exactly <see cref="LaunchCompleted"/>,
/// <see cref="LaunchFailed"/>, and <see cref="LaunchRefused"/>. Nothing else
/// crosses the seam, so consumers pattern-match one exhaustive switch instead
/// of scattering exit codes and stderr writes through the pipeline.
/// </summary>
public abstract record LaunchOutcome;
