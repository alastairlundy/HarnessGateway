namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// Picks the platform-native credential store for the current OS. Each
/// implementation re-checks its own OS at construction so an unsupported
/// platform still fails with an instructive message rather than a
/// <see cref="PlatformNotSupportedException"/>.
/// </summary>
public static class SecureKeyStoreFactory
{
    public static ISecureKeyStore Create(string settingsDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiSecureKeyStore(settingsDirectory);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsKeychainSecureKeyStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretServiceSecureKeyStore();
        }

        throw SecureKeyStoreException.UnsupportedPlatform(
            "no platform credential store is supported on this operating system.");
    }
}
