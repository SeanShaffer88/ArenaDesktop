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
    /// Substrings that mark a line as progress chatter, never an outcome.
    /// Matched case-insensitively anywhere in the line.
    /// </summary>
    private static readonly string[] ChatterMarkers =
    {
        "loading",
        "ready",
        "downloading",
        "download complete:",   // announced by AnnouncementText with the file name
        "download paused:",
        "download cancelled:",
        "download interrupted:", // announced assertively with the reason
        "starting",
        "restoring",
        "paused",
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
        foreach (var marker in ChatterMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
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
