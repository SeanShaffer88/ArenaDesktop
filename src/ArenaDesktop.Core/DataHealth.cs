using System.Text.Json;

namespace ArenaDesktop.Core;

/// <summary>
/// Whether one of the app's own files can still be read (v0.14.0). Every
/// store in this app is a JSON file that, when it fails to parse, is
/// preserved and replaced by defaults — a good rule, but a silent one: the
/// user sees "some settings could not be shown" and never learns *which*
/// file. This check is the missing inventory: name, size, and either "ok"
/// or what is wrong with it, plus a repair that backs the file up before
/// anything is thrown away.
/// </summary>
public enum DataFileState
{
    /// <summary>Parsed as a JSON object.</summary>
    Ok,

    /// <summary>Not present — normal on a first run, or after a reset.</summary>
    Missing,

    /// <summary>Present but not a readable JSON object: empty, truncated, or not JSON.</summary>
    Unreadable,
}

/// <summary>One data file's verdict.</summary>
public sealed record DataFileReport(string Name, string Path, DataFileState State, string Detail, long Bytes)
{
    public bool NeedsRepair => State == DataFileState.Unreadable;

    /// <summary>The one line the Diagnostics report prints for this file.</summary>
    public string Line => State switch
    {
        DataFileState.Ok => $"{Name} — ok ({DownloadText.FormatBytes(Bytes)})",
        DataFileState.Missing => $"{Name} — not created yet",
        _ => $"{Name} — unreadable: {Detail}",
    };
}

/// <summary>
/// The health check for the app's own files, and the repair for the ones that
/// cannot be parsed (v0.14.0). Deliberately conservative: repair never deletes
/// a file it could not first copy, so the worst outcome of pressing the button
/// is nothing happening.
/// </summary>
public static class DataHealth
{
    /// <summary>The files this check covers, in the order they are reported.</summary>
    public static readonly IReadOnlyList<string> FileNames = new[]
    {
        "settings.json", "downloads.json", "battle-lab.json",
        "prompt-pad.json", "field-pass.json", "privacy-stats.json",
        "crash-history.json",
    };

    /// <summary>Check every file in <paramref name="rootDir"/>. Never throws: an unreadable file is a result, not an error.</summary>
    public static IReadOnlyList<DataFileReport> Check(string rootDir)
    {
        var reports = new List<DataFileReport>(FileNames.Count);
        foreach (var name in FileNames)
        {
            var path = Path.Combine(rootDir, name);
            reports.Add(CheckOne(name, path));
        }
        return reports;
    }

    /// <summary>One file's verdict.</summary>
    public static DataFileReport CheckOne(string name, string path)
    {
        var bytes = 0L;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return new DataFileReport(name, path, DataFileState.Missing, "no file", 0);
            bytes = info.Length;
            if (bytes == 0)
                return new DataFileReport(name, path, DataFileState.Unreadable, "the file is empty", 0);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new DataFileReport(name, path, DataFileState.Unreadable,
                    $"the file holds a JSON {document.RootElement.ValueKind.ToString().ToLowerInvariant()}, not an object", bytes);
            return new DataFileReport(name, path, DataFileState.Ok, "", bytes);
        }
        catch (JsonException ex)
        {
            return new DataFileReport(name, path, DataFileState.Unreadable, Describe(ex), bytes);
        }
        catch (Exception ex)
        {
            // Locked, permission-denied, path-too-long: still not "ok", and
            // worth naming, because it is not something repair can fix.
            return new DataFileReport(name, path, DataFileState.Unreadable,
                $"could not be read ({ex.GetType().Name})", bytes);
        }
    }

    /// <summary>
    /// The whole inventory as text, one file per line, with a verdict line at
    /// the end. This is what the Diagnostics report carries.
    /// </summary>
    public static string Summary(IReadOnlyList<DataFileReport> reports)
    {
        if (reports.Count == 0)
            return "Data files: nothing checked.";
        var lines = reports.Select(r => r.Line).ToList();
        var bad = reports.Count(r => r.NeedsRepair);
        lines.Add(bad == 0
            ? "Every data file that exists parses; nothing needs repair."
            : $"{bad} file(s) cannot be read. Repair backs each one up first, then lets the app start that file fresh.");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Make an unreadable file safe to recreate: copy it beside itself
    /// (<see cref="CorruptFile.Marker"/>) and only then delete the original.
    /// Returns the backup path, or null when nothing was changed — because the
    /// file was fine, because it was gone, or because the copy itself failed.
    /// </summary>
    public static string? Repair(string path, DateTimeOffset now)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            if (CheckOne(Path.GetFileName(path), path).State != DataFileState.Unreadable)
                return null;
            var backup = CorruptFile.Preserve(path, now);
            if (backup is null)
                return null;   // no copy, no delete — the user's bytes stay put
            File.Delete(path);
            return backup;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Repair everything that needs it; returns the backups that were made.</summary>
    public static IReadOnlyList<string> RepairAll(IReadOnlyList<DataFileReport> reports, DateTimeOffset now)
    {
        var backups = new List<string>();
        foreach (var report in reports.Where(r => r.NeedsRepair))
        {
            if (Repair(report.Path, now) is { } backup)
                backups.Add(backup);
        }
        return backups;
    }

    private static string Describe(JsonException ex)
    {
        // JsonException messages include line/position on a new line; keep the
        // first line and drop the file's own content, which we never echo.
        var message = (ex.Message ?? "").Replace("\r", " ").Split('\n')[0].Trim();
        return message.Length <= 120 ? message : message[..119] + "…";
    }
}
