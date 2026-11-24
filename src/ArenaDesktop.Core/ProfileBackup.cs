using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArenaDesktop.Core;

/// <summary>The decrypted contents of a profile backup.</summary>
public sealed record ProfileBackupPayload(string SettingsJson, IReadOnlyList<ProfileSettingsSnapshot> Profiles, DateTimeOffset CreatedUtc);

/// <summary>
/// Passphrase-encrypted profile backup (completeness slice E): the
/// settings JSON plus every profile's metadata snapshot, in the same
/// PBKDF2-HMAC-SHA256 + AES-256-GCM envelope pattern as
/// EncryptedExport, under its own magic so the two file kinds can
/// never be confused. What is NOT in the backup, by design and by
/// physics: cookies and live sign-ins — WebView2 binds those to the
/// Windows user+machine (DPAPI), so they cannot be restored from a
/// file on another machine, and the backup UI says so.
/// </summary>
public static class ProfileBackup
{
    public const string Magic = "ARENADESKTOP-PROFILE-BACKUP";
    public const int FormatVersion = 1;
    public const int Iterations = 210_000;

    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 32;

    private sealed class Envelope
    {
        public int Version { get; set; }
        public int Iterations { get; set; }
        public string Salt { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Data { get; set; } = "";
    }

    private sealed class Payload
    {
        public string SettingsJson { get; set; } = "";
        public List<ProfileSettingsSnapshot> Profiles { get; set; } = [];
        public DateTimeOffset CreatedUtc { get; set; }
    }

    public static bool IsBackup(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.TrimStart().Split('\n', 2)[0].Trim() == Magic;

    public static string Create(AppSettings settings, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("A passphrase is required.", nameof(password));
        var payload = new Payload
        {
            SettingsJson = SettingsStore.Export(settings),
            Profiles = settings.Profiles.Select(ProfileSettingsTransfer.Capture).ToList(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];
        using var gcm = new AesGcm(key, TagBytes);
        gcm.Encrypt(nonce, plain, cipher, tag);
        var envelope = new Envelope
        {
            Version = FormatVersion,
            Iterations = Iterations,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Data = Convert.ToBase64String(cipher),
        };
        return Magic + "\n" + JsonSerializer.Serialize(envelope);
    }

    public static bool TryRead(string? text, string password, out ProfileBackupPayload? payload, out string? error)
    {
        payload = null;
        error = null;
        if (!IsBackup(text)) { error = "That file is not an ArenaDesktop profile backup."; return false; }
        try
        {
            var json = text!.TrimStart()[(Magic.Length)..].Trim();
            var envelope = JsonSerializer.Deserialize<Envelope>(json);
            if (envelope is null || envelope.Version != FormatVersion)
            { error = "The backup's format is not understood by this version."; return false; }
            var iterations = Math.Clamp(envelope.Iterations, 100_000, 10_000_000);
            var key = Rfc2898DeriveBytes.Pbkdf2(password,
                Convert.FromBase64String(envelope.Salt), iterations, HashAlgorithmName.SHA256, KeyBytes);
            var cipher = Convert.FromBase64String(envelope.Data);
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(key, TagBytes);
            gcm.Decrypt(Convert.FromBase64String(envelope.Nonce), cipher,
                Convert.FromBase64String(envelope.Tag), plain);
            var inner = JsonSerializer.Deserialize<Payload>(plain);
            if (inner is null) { error = "The backup's contents are damaged."; return false; }
            payload = new ProfileBackupPayload(inner.SettingsJson, inner.Profiles, inner.CreatedUtc);
            return true;
        }
        catch (CryptographicException)
        {
            error = "The passphrase is wrong, or the backup was altered after it was made.";
            return false;
        }
        catch (Exception)
        {
            error = "The backup file is damaged.";
            return false;
        }
    }
}
