using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HarnessGateway.Launcher.Core.Settings.Security;

/// <summary>
/// Windows implementation. Secrets are DPAPI-protected for the current user and
/// the base64 ciphertext is persisted as <c>{ "hgkey:&lt;name&gt;": "&lt;base64&gt;" }</c>
/// in <c>HarnessGateway-keys.json</c>, a sibling of the settings file. A copy of
/// that file on another machine fails decryption, which surfaces as a dangling
/// reference (exit 7) - consistent with T040's "keys don't travel".
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
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HarnessGateway.SecureKeyStore.v1");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

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

    public Task StoreAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        string backendName = KeyReference.RequireBackendName(reference);
        Dictionary<string, string> entries = ReadEntries(backendName);

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

        WriteEntries(backendName, entries, StoreOperation);
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
    {
        string backendName = KeyReference.RequireBackendName(reference);
        Dictionary<string, string> entries = ReadEntries(backendName);

        if (!entries.TryGetValue(reference, out string? cipherText))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            byte[] plainBytes = ProtectedData.Unprotect(
                Convert.FromBase64String(cipherText),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(plainBytes));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Undecryptable ciphertext: the file came from another machine or was
            // tampered with. Report it as "no entry" so the caller raises the
            // instructive dangling-reference failure instead of leaking detail.
            return Task.FromResult<string?>(null);
        }
    }

    public Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        string backendName = KeyReference.RequireBackendName(reference);

        if (!File.Exists(_keysFilePath))
        {
            return Task.CompletedTask;
        }

        Dictionary<string, string> entries = ReadEntries(backendName);
        if (!entries.Remove(reference))
        {
            return Task.CompletedTask;
        }

        WriteEntries(backendName, entries, DeleteOperation);
        return Task.CompletedTask;
    }

    private Dictionary<string, string> ReadEntries(string backendName)
    {
        if (!File.Exists(_keysFilePath))
        {
            return new(StringComparer.Ordinal);
        }

        try
        {
            string json = File.ReadAllText(_keysFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw SecureKeyStoreException.OperationFailed(
                backendName, LoadOperation, $"the key file '{KeysFileName}' could not be read.", ex);
        }
    }

    private void WriteEntries(string backendName, Dictionary<string, string> entries, string operation)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_keysFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_keysFilePath, JsonSerializer.Serialize(entries, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw SecureKeyStoreException.OperationFailed(
                backendName, operation, $"the key file '{KeysFileName}' could not be written.", ex);
        }
    }
}
