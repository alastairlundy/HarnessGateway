using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarnessGateway.Launcher.Core.Serialization;

namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// Windows implementation. Secrets are DPAPI-protected for the current user and
/// the base64 ciphertext is persisted as <c>{ "hgkey:&lt;name&gt;": "&lt;base64&gt;" }</c>
/// in <c>HarnessGateway-keys.json</c>, a sibling of the settings file. A copy of
/// that file on another machine fails decryption, which surfaces as a dangling
/// reference (exit 7) - keys don't travel.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecureKeyStore : ISecureKeyStore
{
    private const string KeysFileName = "HarnessGateway-keys.json";

    private const string StoreOperation = "save to the platform credential store";
    private const string LoadOperation = "load from the platform credential store";
    private const string DeleteOperation = "delete from the platform credential store";

    // Constant entropy binds the ciphertext to this application's purpose without
    // introducing a second secret the user has to manage.
    private static readonly byte[] Entropy = [.. "HarnessGateway.SecureKeyStore.v1"u8];

    private readonly string _keysFilePath;

    public DpapiSecureKeyStore(string settingsDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SecureKeyStoreException.UnsupportedPlatform(
                "the DPAPI key store only runs on Windows.");
        }

        _keysFilePath = Path.Combine(settingsDirectory, KeysFileName);
    }

    public async Task StoreAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        string backendName = KeyReference.RequireBackendName(reference);
        Dictionary<string, string> entries = await ReadEntries(backendName).ConfigureAwait(false);

        try
        {
            byte[] protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secret),
                Entropy,
                DataProtectionScope.CurrentUser);
            entries[reference] = Convert.ToBase64String(protectedBytes);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw SecureKeyStoreException.OperationFailed(
                backendName, StoreOperation, "the data protection API rejected the secret.", ex);
        }

        await WriteEntries(backendName, entries, StoreOperation).ConfigureAwait(false);
    }

    public async Task<string> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
    {
        string backendName = KeyReference.RequireBackendName(reference);
        Dictionary<string, string> entries = await ReadEntries(backendName).ConfigureAwait(false);

        if (!entries.TryGetValue(reference, out string? cipherText))
        {
            throw SecureKeyStoreException.AbsentKey(reference);
        }

        try
        {
            byte[] plainBytes = ProtectedData.Unprotect(
                Convert.FromBase64String(cipherText),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Undecryptable ciphertext: the file came from another machine or was
            // tampered with. Curated detail only - never the raw exception, which
            // could echo material derived from the secret.
            throw SecureKeyStoreException.SecureOperationFailed(
                "DPAPI",
                $"the stored entry could not be decrypted on this machine; " +
                $"re-save it with 'harnessgateway config backend set-key {backendName}'.");
        }
    }

    public async Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        string backendName = KeyReference.RequireBackendName(reference);

        if (!File.Exists(_keysFilePath))
        {
            return;
        }

        Dictionary<string, string> entries = await ReadEntries(backendName).ConfigureAwait(false);
        if (!entries.Remove(reference))
        {
            return;
        }

        await WriteEntries(backendName, entries, DeleteOperation).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, string>> ReadEntries(string backendName)
    {
        if (!File.Exists(_keysFilePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            string json = await File.ReadAllTextAsync(_keysFilePath).ConfigureAwait(false);
            
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, SettingsJsonContext.Default.DictionaryStringString)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw SecureKeyStoreException.OperationFailed(
                backendName, LoadOperation, $"the key file '{KeysFileName}' could not be read.", ex);
        }
    }

    private async Task WriteEntries(string backendName, Dictionary<string, string> entries, string operation)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_keysFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_keysFilePath, JsonSerializer.Serialize(entries, SettingsJsonContext.Default.DictionaryStringString)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw SecureKeyStoreException.OperationFailed(
                backendName, operation, $"the key file '{KeysFileName}' could not be written.", ex);
        }
    }
}