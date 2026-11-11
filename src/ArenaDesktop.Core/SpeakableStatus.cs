namespace ArenaDesktop.Core;

// ───────────────────────── speakable status (hardening pack) ─────────────────────────

/// <summary>
/// Which status-bar lines are worth saying out loud (hardening pack).
///
/// Today the live region speaks download completions/failures and update
/// notices — the three events the shell explicitly announces. Everything
/// else lands in the status bar and is invisible to a screen-reader user:
/// "That link can't open inside ArenaDesktop", "Copying to the clipboard
/// failed", "Settings saved", a mode jump that failed. Those are exactly
/// the messages a sighted user notices, and the gap is the status bar
/// being a purely visual surface.
///
/// Saying EVERY status line would be worse than saying none: the bar also
/// narrates progress (download percentages, "Loading Arena…", "Ready"),
/// and a polite announcement for each of those would talk over the page.
/// So the rule is a narrow allow-list of *outcomes the user asked for*
/// plus a hard exclusion list of chatter, and a length ceiling so a long
/// explanatory line doesn't become a monologue.
///
/// The decision is pure and pinned by tests; the shell only calls
/// <see cref="Announcer.Polite"/> when it says yes.
/// </summary>
public static class SpeakableStatus
{
    /// <summary>Status lines longer than this are left unspoken — the user can read the bar.</summary>
    public const int MaxSpokenLength = 140;

    /// <summary>
    /// Prefixes that START a progress line. "Loading Arena…", "Starting
    /// up", "Restoring your last page" — these always mean the app is busy,
    /// and the line names what it is busy with, so a prefix test is right.
    /// </summary>
    private static readonly string[] ChatterPrefixes =
    {
        "loading",
        "starting",
        "restoring",
    };

    /// <summary>
    /// Words that are chatter only when they ARE the whole line ("Ready",
    /// "Paused"). Matched exactly, never as a substring or a word: "This PC
    /// is ready." and "Downloads paused by you." are outcomes the user asked
    /// for, and swallowing them would silence real information.
    /// </summary>
    private static readonly string[] ChatterWords =
    {
        "ready",
        "paused",
    };

    /// <summary>
    /// Phrases that mark a line as chatter in any position. These are long
    /// and specific enough that a false positive is not a risk, and they
    /// are the exact shapes the shell formats for progress. Each is
    /// already announced through <c>AnnouncementText</c> where it matters,
    /// so speaking it here too would double up.
    /// </summary>
    private static readonly string[] ChatterPhrases =
    {
        "downloading",
        "download complete:",   // announced by AnnouncementText with the file name
        "download paused:",
        "download cancelled:",
        "download interrupted:", // announced assertively with the reason
    };

    /// <summary>
    /// True when <paramref name="status"/> should go to the polite live
    /// region. Conservative by construction: a blank line, a progress
    /// line, a duplicate of the previous line, or anything longer than
    /// <see cref="MaxSpokenLength"/> is refused.
    /// </summary>
    public static bool ShouldSpeak(string? status, string? previousStatus = null)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;
        var text = status.Trim();
        if (text.Length > MaxSpokenLength)
            return false;
        // A line repeated verbatim is one event, not two.
        if (previousStatus is not null
            && string.Equals(text, previousStatus.Trim(), StringComparison.Ordinal))
            return false;
        foreach (var phrase in ChatterPhrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        foreach (var prefix in ChatterPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        // A bare progress word on its own ("Ready", "Paused") is chatter.
        // A line that merely USES the word is an outcome — "This PC is
        // ready." and "Downloads paused by you." are things the user asked
        // for, so matching them would swallow real information.
        foreach (var word in ChatterWords)
        {
            if (IsJustProgressWord(text, word))
                return false;
        }
        return true;
    }

    /// <summary>
    /// True when the line is nothing but the progress word (surrounded only
    /// by punctuation or an ellipsis). "Ready", "Ready…" and "Loading…"
    /// are chatter; "This PC is ready." is a result. The test is
    /// deliberately this narrow: the cost of missing a chatter line is one
    /// extra short sentence read aloud, and the cost of over-matching is a
    /// real outcome the user never hears.
    /// </summary>
    private static bool IsJustProgressWord(string text, string word)
    {
        if (text.Length < word.Length)
            return false;
        // Strip leading/trailing punctuation and whitespace, then compare.
        var start = 0;
        while (start < text.Length && !char.IsLetterOrDigit(text[start]))
            start++;
        var end = text.Length;
        while (end > start && !char.IsLetterOrDigit(text[end - 1]))
            end--;
        return string.Equals(text[start..end], word, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The spoken form of a status line: the same words, minus the visual
    /// hints a screen reader would otherwise read out literally (the
    /// mouse-only "— click to open" tail, and a trailing ellipsis that
    /// means "still working" on screen but reads as an unfinished
    /// sentence aloud).
    /// </summary>
    public static string Spoken(string status)
    {
        var text = (status ?? "").Trim();
        var cut = text.IndexOf(" — click", StringComparison.OrdinalIgnoreCase);
        if (cut > 0)
            text = text[..cut].TrimEnd();
        if (text.EndsWith("…", StringComparison.Ordinal))
            text = text[..^1].TrimEnd();
        return text;
    }
}
