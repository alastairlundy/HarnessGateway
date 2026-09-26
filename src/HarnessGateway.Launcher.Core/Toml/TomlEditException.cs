namespace HarnessGateway.Launcher.Core.Toml;

/// <summary>
/// Thrown when a TOML document cannot be parsed and therefore cannot be edited.
/// </summary>
public sealed class TomlEditException : Exception
{
    public TomlEditException(string message, IReadOnlyList<string> diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// The parser diagnostics that caused the refusal, one entry per diagnostic message.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; }
}
