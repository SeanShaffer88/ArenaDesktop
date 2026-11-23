using System.Text.Json;

namespace ArenaDesktop.Core;

/// <summary>One recorded crash (v0.14.0). No prompt text, no page content — the headline and the summary only.</summary>
public sealed record CrashRecord(DateTimeOffset WhenUtc, string Headline, string Summary);

/// <summary>The crash ledger as it is stored.</summary>
public sealed class CrashHistoryData
{
    /// <summary>Recent crashes, newest last, trimmed to <see cref="CrashHistory.Keep"/>.</summary>
    public List<CrashRecord> Crashes { get; set; } = new();

    /// <summary>
    /// The crash the previous run ended in, cleared on a clean exit. This is
    /// what lets the next launch say "the last run ended in an error" without
    /// guessing — a marker, not an inference.
    /// </summary>
    public CrashRecord? LastCrash { get; set; }
}

/// <summary>
/// The crash ledger (v0.14.0). Three things need it: a crash on a background
/// thread (which kills the process with no dialog), a crash on the UI thread
/// repeated three times in ten minutes (which is a loop, and safe mode is the
/// way out of it), and "what happened last time?" on the next launch. The
/// policy is here so it can be tested; the shell only writes the file.
/// </summary>
public static class CrashHistory
{
    /// <summary>Crashes kept in the file. Enough to see a pattern, small enough to read.</summary>
    public const int Keep = 20;

    /// <summary>Crashes inside <see cref="Window"/> that make a loop.</summary>
    public const int Threshold = 3;

    /// <summary>The span a loop is measured over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>Append a crash, trim the ledger, and mark it as the one the last run ended in.</summary>
    public static CrashHistoryData Record(CrashHistoryData data, CrashRecord record, DateTimeOffset now)
    {
        data.Crashes.Add(record);
        if (data.Crashes.Count > Keep)
            data.Crashes.RemoveRange(0, data.Crashes.Count - Keep);
        data.LastCrash = record;
        return data;
    }

    /// <summary>Three or more crashes inside the window, counting back from <paramref name="now"/>.</summary>
    public static bool IsLoop(CrashHistoryData data, DateTimeOffset now) =>
        data.Crashes.Count(c => now - c.WhenUtc <= Window) >= Threshold;

    /// <summary>A clean exit clears the marker — the next launch has nothing to report.</summary>
    public static CrashHistoryData CleanExit(CrashHistoryData data)
    {
        data.LastCrash = null;
        return data;
    }

    /// <summary>The line the Diagnostics report and the launch banner use, or null when the last run was fine.</summary>
    public static string? LastCrashLine(CrashHistoryData data) =>
        data.LastCrash is { } crash
            ? $"The last run ended in an error ({crash.WhenUtc.ToLocalTime():yyyy-MM-dd HH:mm}): {crash.Summary}"
            : null;
}

/// <summary>Load/save for the crash ledger. Best effort in both directions, like every other store.</summary>
public static class CrashHistoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static CrashHistoryData Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new CrashHistoryData();
            var data = JsonSerializer.Deserialize<CrashHistoryData>(File.ReadAllText(path), Options)
                       ?? new CrashHistoryData();
            data.Crashes ??= new List<CrashRecord>();
            return data;
        }
        catch
        {
            // A broken ledger is not worth preserving twice: the log still has
            // the crashes, and starting empty only costs the loop detection.
            return new CrashHistoryData();
        }
    }

    /// <summary>Save; returns false when the file could not be written (the caller logs, nothing throws).</summary>
    public static bool Save(string path, CrashHistoryData data)
    {
        try
        {
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(data, Options));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Record a crash and persist it. Never throws — this runs inside a crash handler.</summary>
    public static CrashHistoryData Record(string path, CrashRecord record, DateTimeOffset now)
    {
        var data = Load(path);
        CrashHistory.Record(data, record, now);
        Save(path, data);
        return data;
    }

    /// <summary>Clear the "last run ended badly" marker on a clean exit.</summary>
    public static void MarkCleanExit(string path)
    {
        var data = Load(path);
        if (data.LastCrash is null)
            return;
        CrashHistory.CleanExit(data);
        Save(path, data);
    }
}
