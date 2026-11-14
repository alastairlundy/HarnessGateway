using System.Globalization;

namespace HarnessGateway.Launcher.Core.Update;

/// <summary>
/// The on-disk last-check timestamp behind the update-check cooldown (T017):
/// the file lives in the per-OS config directory next to the settings file
/// and is implementation-named. Reads and writes are best effort - a missing,
/// damaged, or unwritable stamp costs one extra network call, never the check
/// itself, so no fault here can surface to the user as a failed check.
/// </summary>
public sealed class UpdateCooldownStore
{
    /// <summary>The file's name inside the config directory (T017 leaves it implementation-chosen).</summary>
    public const string FileName = "update-check.timestamp";

    private readonly string _filePath;

    public UpdateCooldownStore(string directory)
    {
        _filePath = Path.Combine(directory, FileName);
    }

    public string FilePath => _filePath;

    /// <summary>
    /// The instant the last check that reached the network was stamped, or
    /// null when none did (no file, an unreadable file, or an unparseable
    /// value - all read as "no cooldown").
    /// </summary>
    public DateTimeOffset? ReadLastCheck()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            string text = File.ReadAllText(_filePath).Trim();
            return DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset stamp)
                ? stamp
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stamp we cannot read is treated as no stamp: the worst case
            // is one extra unauthenticated call, which the window itself
            // exists to avoid - never a broken command.
            return null;
        }
    }

    public void WriteLastCheck(DateTimeOffset timestamp)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, timestamp.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort by design: the stamp only keeps repeat checks clear
            // of the unauthenticated rate limit, so a failed write is allowed
            // to cost one extra call - not the check's outcome.
        }
    }
}
