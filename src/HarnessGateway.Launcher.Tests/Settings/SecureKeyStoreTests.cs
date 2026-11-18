using HarnessGateway.Launcher.Core.Settings.Security;
using HarnessGateway.Launcher.Tests.Fakes;

namespace HarnessGateway.Launcher.Tests.Settings;

/// <summary>
/// Construction-level coverage plus the delete-outcome policy: the gate never
/// opens a real keychain or spawns a real credential process (fakes strategy) -
/// the interface contract runs against <see cref="FakeSecureKeyStore"/>, the
/// process stores' exit-code interpretation is pinned as pure logic here, and
/// the real round-trips are review-level OS checks outside the gate.
/// </summary>
public class SecureKeyStoreTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"HarnessGateway-keystore-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Exception? TryConstruct(Action construct)
    {
        try
        {
            construct();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Test]
    public async Task Linux_delete_policy_reports_every_non_zero_exit_as_failure()
    {
        // `secret-tool clear` succeeds when nothing matched (the Secret
        // Service Clear call is vacuous), so a non-zero exit can only be an
        // operational failure - a locked collection, an unreachable session -
        // and must surface as one instead of being swallowed.
        await Assert.That(SecureKeyStoreProcess.LinuxDeleteFailed(0)).IsFalse();
        await Assert.That(SecureKeyStoreProcess.LinuxDeleteFailed(1)).IsTrue();
        await Assert.That(SecureKeyStoreProcess.LinuxDeleteFailed(2)).IsTrue();
    }

    [Test]
    public async Task MacOs_delete_policy_distinguishes_the_absent_item_from_a_real_failure()
    {
        // `security delete-generic-password` exits non-zero for both the
        // idempotent miss and an operational failure; only the recognized
        // "could not be found" miss succeeds, and ambiguity fails loud so a
        // stored secret can never be orphaned silently.
        await Assert.That(SecureKeyStoreProcess.MacOsDeleteFailed(0, string.Empty)).IsFalse();
        await Assert.That(
                SecureKeyStoreProcess.MacOsDeleteFailed(
                    1,
                    "security: SecKeychainSearchCopyNext: The specified item could not be found in the keychain."))
            .IsFalse();
        await Assert.That(
                SecureKeyStoreProcess.MacOsDeleteFailed(1, "security: SecKeychainItemCopyAttributes: authentication failed"))
            .IsTrue();
        await Assert.That(SecureKeyStoreProcess.MacOsDeleteFailed(1, string.Empty)).IsTrue();
        await Assert.That(SecureKeyStoreProcess.MacOsDeleteFailed(1, null)).IsTrue();
    }

    [Test]
    public async Task Factory_returns_the_platform_store_for_this_os()
    {
        string tempDir = CreateTempDir();
        try
        {
            ISecureKeyStore store = SecureKeyStoreFactory.Create(tempDir);

            if (OperatingSystem.IsWindows())
            {
                await Assert.That(store.GetType()).IsEqualTo(typeof(DpapiSecureKeyStore));
            }
            else if (OperatingSystem.IsMacOS())
            {
                await Assert.That(store.GetType()).IsEqualTo(typeof(MacOsKeychainSecureKeyStore));
            }
            else if (OperatingSystem.IsLinux())
            {
                await Assert.That(store.GetType()).IsEqualTo(typeof(LinuxSecretServiceSecureKeyStore));
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task DpapiSecureKeyStore_constructs_only_on_windows()
    {
        string tempDir = CreateTempDir();
        try
        {
            // Constructed inline (not through the shared helper) so the platform
            // guard flows to the call site for the platform-compatibility analyzer.
            bool constructed = false;
            if (OperatingSystem.IsWindows())
            {
                DpapiSecureKeyStore store = new(tempDir);
                constructed = store is not null;
            }

            await Assert.That(constructed).IsEqualTo(OperatingSystem.IsWindows());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task MacOsKeychainSecureKeyStore_constructs_only_on_macos()
    {
        Exception? caught = TryConstruct(() => new MacOsKeychainSecureKeyStore());

        if (OperatingSystem.IsMacOS())
        {
            await Assert.That(caught).IsNull();
            return;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught.Message).Contains("macOS");
    }

    [Test]
    public async Task LinuxSecretServiceSecureKeyStore_constructs_only_on_linux()
    {
        Exception? caught = TryConstruct(() => new LinuxSecretServiceSecureKeyStore());

        if (OperatingSystem.IsLinux())
        {
            await Assert.That(caught).IsNull();
            return;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught.Message).Contains("Linux");
    }

    [Test]
    public async Task Store_retrieve_delete_contract_round_trips_on_the_fake()
    {
        FakeSecureKeyStore keyStore = new();

        await keyStore.StoreAsync("hgkey:default", "sk-round-trip");
        string retrieved = await keyStore.RetrieveAsync("hgkey:default");

        await Assert.That(retrieved).IsEqualTo("sk-round-trip");
        await Assert.That(keyStore.StoreCallCount).IsEqualTo(1);
        await Assert.That(keyStore.RetrieveCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Retrieve_on_a_missing_reference_throws_absent_key()
    {
        FakeSecureKeyStore keyStore = new();

        Exception? caught = null;
        try
        {
            await keyStore.RetrieveAsync("hgkey:absent");
        }
        catch (SecureKeyStoreException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught.Message)
            .IsEqualTo(SecureKeyStoreException.AbsentKey("hgkey:absent").Message);
    }

    [Test]
    public async Task Delete_is_idempotent()
    {
        FakeSecureKeyStore keyStore = new();
        await keyStore.StoreAsync("hgkey:default", "sk-temp");

        await keyStore.DeleteAsync("hgkey:default");
        await keyStore.DeleteAsync("hgkey:default");

        Exception? caught = null;
        try
        {
            await keyStore.RetrieveAsync("hgkey:default");
        }
        catch (SecureKeyStoreException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught.Message)
            .IsEqualTo(SecureKeyStoreException.AbsentKey("hgkey:default").Message);
        await Assert.That(keyStore.DeleteCallCount).IsEqualTo(2);
    }
}
