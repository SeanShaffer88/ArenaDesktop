using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArenaDesktop.Core;

/// <summary>
/// Turns clear bytes into an opaque stored blob and back. The shell
/// implements this with Windows DPAPI (current user); Core ships a
/// passphrase implementation (PBKDF2 + AES-GCM, the EncryptedExport
/// pattern) for portable installs and tests. A vault only ever sees
/// blobs — a key is clear text only in memory, at the moment of use.
/// </summary>
public interface ISecretProtector
{
    string Name { get; }
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] blob);
}

/// <summary>Passphrase protector: each blob carries its own salt/nonce/tag envelope.</summary>
public sealed class PassphraseSecretProtector(string passphrase, int iterations = 210_000) : ISecretProtector
{
    public string Name => "passphrase";
    internal int Iterations => iterations;

    public byte[] Protect(byte[] plain)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(nonce, plain, cipher, tag);
        return [.. salt, .. nonce, .. tag, .. cipher];
    }

    public byte[] Unprotect(byte[] blob)
    {
        if (blob.Length < 16 + 12 + 16) throw new CryptographicException("Protected blob is damaged.");
        var salt = blob[..16];
        var nonce = blob[16..28];
        var tag = blob[28..44];
        var cipher = blob[44..];
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, 16);
        gcm.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}

/// <summary>
/// API keys for BYOK providers, stored as protected blobs in their
/// own file (`api-keys.json` beside the settings) — never inside
/// settings.json, so a settings export never carries a key.
/// </summary>
public sealed class ApiKeyVault
{
    private readonly string _path;
    private readonly ISecretProtector _protector;
    private readonly Dictionary<string, string> _blobs = new(StringComparer.OrdinalIgnoreCase);

    private sealed class VaultFile
    {
        public int Version { get; set; } = 1;
        public string Protector { get; set; } = "";
        public Dictionary<string, string> Keys { get; set; } = new();
    }

    private ApiKeyVault(string path, ISecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public static ApiKeyVault Load(string path, ISecretProtector protector)
    {
        var vault = new ApiKeyVault(path, protector);
        try
        {
            if (!File.Exists(path)) return vault;
            var file = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(path));
            if (file?.Keys is not null)
                foreach (var (id, blob) in file.Keys) vault._blobs[id] = blob;
        }
        catch
        {
            // A damaged vault reads as empty; keys can be re-entered.
            // The file itself is preserved for the user to inspect.
            vault._blobs.Clear();
        }
        return vault;
    }

    public IReadOnlyCollection<string> ProviderIds => _blobs.Keys;

    public bool HasKey(string providerId) => _blobs.ContainsKey(providerId);

    public void SetKey(string providerId, string apiKey)
    {
        var blob = _protector.Protect(Encoding.UTF8.GetBytes(apiKey));
        _blobs[providerId] = Convert.ToBase64String(blob);
        Save();
    }

    /// <summary>The clear key, or null when absent or undecryptable under this protector.</summary>
    public string? GetKey(string providerId)
    {
        if (!_blobs.TryGetValue(providerId, out var base64)) return null;
        try
        {
            var plain = _protector.Unprotect(Convert.FromBase64String(base64));
            return Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }

    public void RemoveKey(string providerId)
    {
        if (_blobs.Remove(providerId)) Save();
    }

    private void Save()
    {
        var file = new VaultFile { Protector = _protector.Name, Keys = new Dictionary<string, string>(_blobs) };
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }
}
