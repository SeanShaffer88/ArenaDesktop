using System.Text.Json;

namespace ArenaDesktop.Core;

/// <summary>One audit-log entry: when, what kind of event, and a non-sensitive detail.</summary>
public sealed record AuditEntry(DateTimeOffset Utc, string Kind, string Detail);

/// <summary>
/// The user-facing audit log (completeness slice E): an append-only
/// JSON-lines file recording the sensitive things the app itself
/// does — profile switches, downloads, data clearing, backup and
/// restore, settings exports, permission denials. Details carry
/// hosts and file names, never URLs, page content, or secrets. The
/// file rotates once at ~1 MB (one .old kept), like the diagnostic
/// log. Writing never throws into the caller's flow.
/// </summary>
public static class AuditLog
{
    public const string ProfileSwitch = "ProfileSwitch";
    public const string DownloadCompleted = "DownloadCompleted";
    public const string DataCleared = "DataCleared";
    public const string BackupCreated = "BackupCreated";
    public const string BackupRestored = "BackupRestored";
    public const string SettingsExported = "SettingsExported";
    public const string PermissionDenied = "PermissionDenied";
    public const string ProfileBackupNote = "ProfileBackup";

    private const long MaxBytes = 1_000_000;
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly object Gate = new();

    public static void Record(string path, string kind, string detail)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RotateIfNeeded(path);
                var line = JsonSerializer.Serialize(
                    new AuditEntry(DateTimeOffset.UtcNow, kind, Sanitize(detail)), Options);
                File.AppendAllText(path, line + "\n");
            }
        }
        catch { /* the audit log must never break the action it records */ }
    }

    /// <summary>Newest first, at most <paramref name="max"/> entries; damaged lines are skipped.</summary>
    public static IReadOnlyList<AuditEntry> ReadRecent(string path, int max = 200)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var entries = new List<AuditEntry>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<AuditEntry>(line, Options);
                    if (entry is not null) entries.Add(entry);
                }
                catch (JsonException) { /* a torn last line is skipped, not fatal */ }
            }
            entries.Reverse();
            return entries.Take(max).ToList();
        }
        catch { return []; }
    }

    private static string Sanitize(string detail) =>
        detail.Replace('\n', ' ').Replace('\r', ' ') is { Length: > 300 } longText ? longText[..300] : detail.Replace('\n', ' ').Replace('\r', ' ');

    private static void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes) return;
        var old = path + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(path, old);
    }
}
