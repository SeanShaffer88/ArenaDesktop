namespace ArenaDesktop.Core;

// ───────────────────────── log level filter (enhancement pack 10) ─────────────────────────

/// <summary>The severity of one diagnostic log line, in the order the log writes them.</summary>
public enum LogLevel
{
    /// <summary>An ordinary event (the "INFO" marker).</summary>
    Info,
    /// <summary>Something recoverable went wrong ("WARN").</summary>
    Warn,
    /// <summary>Something failed ("ERROR").</summary>
    Error,
    /// <summary>A line the log didn't write, or wrote in a shape this parser doesn't recognise.</summary>
    Unrecognised,
}

/// <summary>
/// Reading the severity out of <see cref="DiagnosticLog"/>'s own format,
/// so the log viewer can filter by it (enhancement pack 10).
///
/// The log viewer showed the whole file. A megabyte of that is mostly
/// INFO, and the ERROR someone opened the viewer to find is buried in it —
/// which is the opposite of what a diagnostic surface is for.
///
/// The parsing rules are here rather than in the window because getting
/// them wrong is exactly what makes a filter lie:
/// - the marker is the <c>[LEVEL]</c> the logger writes, matched at the
///   start of the message portion of the line — NOT a substring search, so
///   a message that merely mentions "error" is not promoted to an ERROR
///   line, and a warning ABOUT an error stays a warning;
/// - a line that doesn't match the format is <see cref="LogLevel.Unrecognised"/>,
///   never silently dropped and never guessed at;
/// - an unrecognised level word inside a valid marker (a future level, a
///   hand-edited line) is also Unrecognised, so it can never hide a
///   problem by being misfiled.
/// </summary>
public static class LogLevelFilter
{
    /// <summary>Every level a filter can select, in the order the chips are shown.</summary>
    public static readonly IReadOnlyList<LogLevel> Selectable = new[]
    {
        LogLevel.Error, LogLevel.Warn, LogLevel.Info, LogLevel.Unrecognised,
    };

    /// <summary>The chip label for a level.</summary>
    public static string Label(LogLevel level) => level switch
    {
        LogLevel.Error => "Errors",
        LogLevel.Warn => "Warnings",
        LogLevel.Info => "Info",
        _ => "Unparsed",
    };

    /// <summary>
    /// The severity of one line. The logger writes
    /// <c>yyyy-MM-dd HH:mm:ss [LEVEL] message</c>, so the first bracketed
    /// token after the timestamp is the level.
    /// </summary>
    public static LogLevel Classify(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return LogLevel.Unrecognised;
        var text = line;
        // Skip a leading timestamp (19 chars) when it looks like one, so a
        // message that happens to begin with a bracket is not mistaken for
        // the level marker.
        if (text.Length > 20
            && char.IsDigit(text[0]) && text[4] == '-' && text[7] == '-'
            && text[10] == ' ' && text[13] == ':')
            text = text[19..];
        var open = text.IndexOf('[');
        if (open < 0)
            return LogLevel.Unrecognised;
        // Between the timestamp and the marker there is at most a space.
        if (text[..open].Trim().Length > 0)
            return LogLevel.Unrecognised;
        var close = text.IndexOf(']', open + 1);
        if (close < 0)
            return LogLevel.Unrecognised;
        var word = text[(open + 1)..close].Trim();
        return word.ToUpperInvariant() switch
        {
            "INFO" => LogLevel.Info,
            "WARN" => LogLevel.Warn,
            "ERROR" => LogLevel.Error,
            _ => LogLevel.Unrecognised,
        };
    }

    /// <summary>
    /// Keep only the lines whose level is in <paramref name="wanted"/>.
    /// An empty selection means "everything", which is the state the
    /// viewer opens in.
    /// </summary>
    public static string Apply(string? text, IReadOnlyCollection<LogLevel>? wanted)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        if (wanted is null || wanted.Count == 0)
            return text;
        var keep = new HashSet<LogLevel>(wanted);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Where(line => line.Length > 0 && keep.Contains(Classify(line))));
    }

    /// <summary>How many lines of each level the text holds — the counts the chips show.</summary>
    public static IReadOnlyDictionary<LogLevel, int> Counts(string? text)
    {
        var counts = new Dictionary<LogLevel, int>
        {
            [LogLevel.Info] = 0,
            [LogLevel.Warn] = 0,
            [LogLevel.Error] = 0,
            [LogLevel.Unrecognised] = 0,
        };
        if (string.IsNullOrEmpty(text))
            return counts;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0)
                continue;
            counts[Classify(line)]++;
        }
        return counts;
    }

    /// <summary>
    /// Whether this level is worth the viewer drawing attention to it —
    /// used to decide if the filter bar says "nothing at this level"
    /// rather than showing an empty pane.
    /// </summary>
    public static bool IsProblem(LogLevel level) => level is LogLevel.Error or LogLevel.Warn;
}
