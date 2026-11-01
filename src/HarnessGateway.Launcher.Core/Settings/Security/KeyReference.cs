namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// Format of the at-rest credential pointer carried by <c>Provider.ApiKey</c> in
/// the settings file: <c>hgkey:&lt;backend-name&gt;</c>. The prefix is
/// deterministic from the backend name and distinguishable from any real API
/// key, so a legacy plaintext value is still recognised as legacy.
/// </summary>
public static class KeyReference
{
    public const string Prefix = "hgkey:";

    /// <summary>Builds the reference for a backend's saved key.</summary>
    public static string ForProvider(string providerName) => Prefix + providerName;

    /// <summary>True when <paramref name="value"/> is a secure-store reference rather than a legacy plaintext key.</summary>
    public static bool IsReference(string? value) =>
        value is not null
        && value.Length > Prefix.Length
        && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Extracts the backend name from a reference; null when
    /// <paramref name="value"/> is not a reference.
    /// </summary>
    public static string? ProviderName(string? value) =>
        IsReference(value) ? value![Prefix.Length..] : null;

    /// <summary>
    /// Same read as <see cref="ProviderName"/> for store implementations, which
    /// only ever receive references they were handed by a caller.
    /// </summary>
    public static string RequireProviderName(string reference) =>
        ProviderName(reference)
        ?? throw new SecureKeyStoreException("The credential identifier is not a HarnessGateway key reference.");
}
