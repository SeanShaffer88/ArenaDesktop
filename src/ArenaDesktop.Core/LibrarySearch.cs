using System.Text;
using System.Text.Json;

namespace ArenaDesktop.Core;

/// <summary>Which of the app's own stores a library entry came from.</summary>
public enum LibrarySource { Prompt, Journal, PromptPad, Download }

/// <summary>One searchable unit of the app's own stored data.</summary>
public sealed record LibraryEntry(LibrarySource Source, string Title, string Body, DateTimeOffset? WhenUtc)
{
    public string SourceName => Source switch
    {
        LibrarySource.Prompt => "Battle Lab prompt",
        LibrarySource.Journal => "Battle journal",
        LibrarySource.PromptPad => "Prompt pad",
        LibrarySource.Download => "Download",
        _ => "Library",
    };
}

/// <summary>
/// Full-text search + structured export over the data ArenaDesktop
/// itself keeps (completeness slice D2): Battle Lab prompts and
/// journal, prompt pad entries, download history. Arena
/// conversations are NOT here — they live on Arena's servers and the
/// app never reads the page, so there is nothing local to search;
/// the library window says so rather than implying otherwise.
/// Matching follows the house rule: every query term must
/// prefix-match a word of the entry.
/// </summary>
public static class LibrarySearch
{
    public static IReadOnlyList<LibraryEntry> BuildIndex(
        BattleLabData? battleLab, PromptPadData? promptPad, IReadOnlyList<DownloadRecord>? downloads)
    {
        var entries = new List<LibraryEntry>();
        if (battleLab is not null)
        {
            foreach (var p in battleLab.Prompts)
                entries.Add(new LibraryEntry(LibrarySource.Prompt, p.Name, $"{p.Category}\n{p.Body}", null));
            foreach (var j in battleLab.Journal)
                entries.Add(new LibraryEntry(LibrarySource.Journal, j.PromptName,
                    $"{j.Outcome} — {j.Note}", j.RecordedUtc));
        }
        if (promptPad is not null)
        {
            foreach (var r in promptPad.Recents)
                entries.Add(new LibraryEntry(LibrarySource.PromptPad, FirstLine(r.Text), r.Text, r.UsedUtc));
            if (!string.IsNullOrWhiteSpace(promptPad.Draft))
                entries.Add(new LibraryEntry(LibrarySource.PromptPad, "Unsent draft", promptPad.Draft, null));
        }
        if (downloads is not null)
            foreach (var d in downloads)
                entries.Add(new LibraryEntry(LibrarySource.Download, d.FileName,
                    $"{d.State} — {DownloadText.FormatBytes(d.SizeBytes)} — {d.FullPath}", d.CompletedUtc));
        return entries;
    }

    public static IReadOnlyList<LibraryEntry> Search(IReadOnlyList<LibraryEntry> index, string? query)
    {
        var terms = Tokenize(query ?? "");
        if (terms.Count == 0) return index;
        return index.Where(entry =>
        {
            var words = Tokenize($"{entry.Title} {entry.Body}");
            return terms.All(term => words.Any(w => w.StartsWith(term, StringComparison.Ordinal)));
        }).ToList();
    }

    internal static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) current.Append(ch);
            else if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', 2)[0].Trim() is { Length: > 60 } line ? line[..59] + "…" : text.Split('\n', 2)[0].Trim();
}

/// <summary>The structured library export document (versioned).</summary>
public sealed class LibraryDocument
{
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedUtc { get; set; }
    public BattleLabData BattleLab { get; set; } = new();
    public PromptPadData PromptPad { get; set; } = new();
    public List<DownloadRecord> Downloads { get; set; } = [];
}

public static class LibraryExport
{
    public static LibraryDocument Build(BattleLabData battleLab, PromptPadData promptPad, IReadOnlyList<DownloadRecord> downloads) =>
        new()
        {
            ExportedUtc = DateTimeOffset.UtcNow,
            BattleLab = battleLab,
            PromptPad = promptPad,
            Downloads = downloads.ToList(),
        };

    public static string ToJson(LibraryDocument document) =>
        JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });

    public static string ToMarkdown(LibraryDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ArenaDesktop library export");
        sb.AppendLine();
        sb.AppendLine($"Exported {document.ExportedUtc:yyyy-MM-dd HH:mm} UTC. Contains only data ArenaDesktop itself stores — no Arena conversation content (that lives on Arena's servers).");
        sb.AppendLine();
        sb.AppendLine("## Battle Lab prompts");
        foreach (var p in document.BattleLab.Prompts)
        {
            sb.AppendLine();
            sb.AppendLine($"### {p.Name}");
            if (p.Category.Length > 0) sb.AppendLine($"Category: {p.Category}");
            sb.AppendLine();
            sb.AppendLine(p.Body);
        }
        sb.AppendLine();
        sb.AppendLine("## Battle journal");
        foreach (var j in document.BattleLab.Journal)
            sb.AppendLine($"- {j.RecordedUtc:yyyy-MM-dd} — {j.PromptName}: {j.Outcome}{(j.Note.Length > 0 ? $" — {j.Note}" : "")}");
        sb.AppendLine();
        sb.AppendLine("## Prompt pad");
        foreach (var r in document.PromptPad.Recents)
            sb.AppendLine($"- {r.Text.Replace("\n", " ")}");
        sb.AppendLine();
        sb.AppendLine("## Downloads");
        foreach (var d in document.Downloads)
            sb.AppendLine($"- {d.CompletedUtc:yyyy-MM-dd} — {d.FileName} ({DownloadText.FormatBytes(d.SizeBytes)}, {d.State})");
        return sb.ToString();
    }
}
