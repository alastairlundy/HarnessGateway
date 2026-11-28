using HarnessGateway.Launcher.Core.Adapters.Abstractions;

namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The prose the launch flow and the wizard share when availability is why a
/// launch cannot run.
/// </summary>
public static class AvailabilityMessages
{
    /// <summary>
    /// Appended wherever a harness is reported as not installed. The probe
    /// sees this process's PATH and nothing else, so a harness installed
    /// outside it is indistinguishable from one never installed: say that
    /// instead of implying a diagnosis the probe cannot support.
    /// </summary>
    public const string NotOnPath =
        "If it is installed, it is missing from PATH - add it to PATH and re-run.";

    /// <summary>
    /// Appends <paramref name="sentence"/> as a following sentence. Install
    /// hints are written without a trailing full stop, so the joiner is
    /// decided here rather than assumed by every caller. Empty text returns
    /// the sentence alone, never a leading full stop.
    /// </summary>
    public static string AppendSentence(string text, string sentence)
    {
        string trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
        {
            return sentence;
        }

        return trimmed[^1] is '.' or '!' or '?' or ':'
            ? $"{trimmed} {sentence}"
            : $"{trimmed}. {sentence}";
    }

    /// <summary>
    /// Refusal for a probe that ran and found nothing: the install line the
    /// harness already carries, plus the PATH caveat.
    /// </summary>
    public static string NotInstalled(HarnessMetadata metadata) =>
        AppendSentence(metadata.InstallHint, NotOnPath);

    /// <summary>
    /// Refusal for a probe that could not run. Deliberately carries no
    /// install hint: the check established nothing about whether the harness
    /// is installed, so advising an install would be inventing a finding.
    /// </summary>
    public static string CheckFailed(HarnessMetadata metadata, string? cause) =>
        $"Could not check whether {metadata.Name} is installed: "
        + $"{cause ?? "the check failed"} (checked with '{metadata.DetectionCommand}').";
}
