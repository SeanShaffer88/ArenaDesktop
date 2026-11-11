namespace ArenaDesktop.Core;

/// <summary>
/// Find inside the log viewer (hardening pack).
///
/// The viewer shows up to a megabyte of text in a read-only box, and the
/// reason anyone opens it is to find one line — the failure, the file, the
/// profile. Without find, the only option is Ctrl+A, copy, and search
/// somewhere else, which is exactly the flow the redaction audit exists to
/// make unnecessary.
///
/// The rules are pinned here rather than in the window so the wrapping and
/// the empty-search behaviour cannot drift:
/// - matching is case-insensitive (nobody remembers whether the log says
///   WARN or warn) and literal (a log full of [ and ( must not be
///   interpreted as a pattern);
/// - Next wraps from the end back to the first match and Previous wraps
///   the other way, so a repeated press always moves;
/// - a blank or absent query has no matches and no index, rather than
///   matching everything;
/// - the count is the number of separate occurrences, so "3 of 12" means
///   what a person expects.
/// </summary>
public static class LogSearch
{
    /// <summary>One match: where it starts and how long it is.</summary>
    public readonly record struct Match(int Index, int Length)
    {
        public int End => Index + Length;
    }

    /// <summary>
    /// Every occurrence of <paramref name="query"/> in <paramref name="text"/>,
    /// left to right, case-insensitive, overlapping matches counted
    /// separately (searching "aa" in "aaaa" finds three, because that is
    /// what a reader can see on the line).
    /// </summary>
    public static IReadOnlyList<Match> FindAll(string? text, string? query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            return Array.Empty<Match>();
        var matches = new List<Match>();
        var from = 0;
        while (from <= text.Length - query.Length)
        {
            var at = text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                break;
            matches.Add(new Match(at, query.Length));
            from = at + 1;
        }
        return matches;
    }

    /// <summary>
    /// Which match Next/Previous should select, one-based for display.
    /// <paramref name="current"/> is -1 when nothing is selected yet, where
    /// Next starts at the first match and Previous at the last. Out-of-range
    /// input wraps rather than throwing.
    /// </summary>
    public static int Step(int count, int current, bool forward)
    {
        if (count <= 0)
            return -1;
        if (current < 0 || current >= count)
            return forward ? 0 : count - 1;
        return forward ? (current + 1) % count : (current - 1 + count) % count;
    }

    /// <summary>The status line: "3 of 12 matches", or words when there are none.</summary>
    public static string Describe(int count, int current)
    {
        if (count <= 0)
            return "No matches";
        var position = current < 0 || current >= count ? 0 : current;
        return count == 1 ? "1 match" : $"{position + 1} of {count} matches";
    }
}
