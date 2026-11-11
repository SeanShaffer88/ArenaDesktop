using System.Text;

namespace ArenaDesktop.Core;

// ───────────────────────── prompt export formats (hardening pack) ─────────────────────────

/// <summary>
/// The prompt library had only its own JSON round trip, which is right for
/// backup and useless for "paste my prompts into a doc" or "open this in a
/// spreadsheet" (hardening pack).
///
/// Two more formats, both generated from the same sanitized list the JSON
/// export writes, so nothing extra can leak:
/// - Markdown keeps the reading order (category headings, then prompts),
///   which is what a person shares;
/// - CSV is one row per prompt with the bodies intact, which is what a
///   spreadsheet wants.
/// Escaping is the whole job here: a prompt body can contain anything the
/// user typed, and a stray quote, comma or newline must not break the file.
/// </summary>
public static class PromptExportText
{
    /// <summary>Markdown: a heading per category, then each prompt under a subheading.</summary>
    public static string ToMarkdown(IEnumerable<BattlePrompt> prompts)
    {
        var list = (prompts ?? Enumerable.Empty<BattlePrompt>())
            .Where(p => p is not null)
            .OrderBy(CategoryOf, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var builder = new StringBuilder();
        builder.AppendLine("# Battle Lab prompts");
        builder.AppendLine();
        if (list.Count == 0)
        {
            builder.AppendLine("No prompts in the library.");
            return builder.ToString().TrimEnd() + "\n";
        }
        string? currentCategory = null;
        foreach (var prompt in list)
        {
            var category = CategoryOf(prompt);
            if (!string.Equals(category, currentCategory, StringComparison.Ordinal))
            {
                builder.AppendLine($"## {category}");
                builder.AppendLine();
                currentCategory = category;
            }
            builder.AppendLine($"### {prompt.Name}");
            builder.AppendLine();
            builder.AppendLine((prompt.Body ?? "").Trim());
            builder.AppendLine();
        }
        return builder.ToString().TrimEnd() + "\n";
    }

    /// <summary>CSV: a header row, then one row per prompt. Quoted and escaped per RFC 4180.</summary>
    public static string ToCsv(IEnumerable<BattlePrompt> prompts)
    {
        var list = (prompts ?? Enumerable.Empty<BattlePrompt>()).Where(p => p is not null).ToList();
        var builder = new StringBuilder();
        builder.Append("Category,Name,Body\n");
        foreach (var prompt in list)
        {
            builder.Append(CsvField(CategoryOf(prompt))).Append(',')
                   .Append(CsvField(prompt.Name)).Append(',')
                   .Append(CsvField(prompt.Body))
                   .Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>
    /// One CSV field. A value containing a comma, quote or line break is
    /// wrapped in quotes with its inner quotes doubled; anything else passes
    /// through unchanged. Leading/trailing spaces get quoted too, since a
    /// spreadsheet would otherwise trim them away.
    /// </summary>
    public static string CsvField(string? value)
    {
        var text = Defuse(value ?? "");
        var needsQuotes = text.Length > 0
            && (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
                || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
        if (!needsQuotes)
            return text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Stop a spreadsheet from executing a prompt as a formula. A cell
    /// beginning with = + - @ or a control character is treated as a
    /// formula by Excel and Sheets; prefixing an apostrophe makes the cell
    /// text without changing what a reader sees. This matters because a
    /// prompt body is arbitrary user text and the export is meant to be
    /// opened by someone else.
    /// </summary>
    private static string Defuse(string text)
    {
        if (text.Length == 0)
            return text;
        var first = text[0];
        var dangerous = first is '=' or '+' or '-' or '@' or '\t' or '\r';
        return dangerous ? "'" + text : text;
    }

    /// <summary>The suggested file name, dated so two exports of the same format never collide.</summary>
    public static string FileNameFor(string extension, DateTimeOffset now) =>
        $"battle-lab-prompts-{now.ToLocalTime():yyyy-MM-dd}.{extension.TrimStart('.')}";

    private static string CategoryOf(BattlePrompt prompt) =>
        string.IsNullOrWhiteSpace(prompt.Category) ? "General" : prompt.Category.Trim();
}
