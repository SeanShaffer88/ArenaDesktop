namespace ArenaDesktop.Core;

// ───────────────────────── arrival notice (enhancement pack) ─────────────────────────

/// <summary>
/// The little line the status bar shows when a jump finishes
/// (enhancement pack): where you are, in plain words, for a few
/// seconds — long enough to catch out of the corner of your eye, short
/// enough not to talk over the page. Pure so the durations and wording
/// are pinned by tests rather than discovered in a XAML timer.
/// </summary>
public static class ArrivalNotice
{
    /// <summary>How long the notice stays up. Long enough to read a short label, short enough to be unobtrusive.</summary>
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(4);

    /// <summary>The notice text for a mode jump ("Arrived at Leaderboard · Ctrl+4").</summary>
    public static string ForMode(ArenaMode mode) => $"Arrived at {mode.Label} · {mode.ShortcutText}";

    /// <summary>The notice text for any other finished navigation ("Arrived at Battle"). Falls back to the mode label when the mode is unknown.</summary>
    public static string ForModeId(string? modeId) =>
        ArenaModes.TryGet(modeId, out var mode) ? ForMode(mode) : "Page loaded";
}

// ───────────────────────── startup mode restoration (enhancement pack) ─────────────────────────

/// <summary>
/// Deciding what a launch restores (enhancement pack): the per-profile
/// start page still wins, safe mode still starts at Home, and the
/// profile's last page is only reused when it still parses as an Arena
/// page — a saved address that Arena has since changed (or a hand-edited
/// one that never parsed) falls back to Home instead of showing a broken
/// page. <see cref="StartPagePolicy"/> keeps its existing behavior; this
/// is the narrower question "is this remembered address still usable?".
/// </summary>
public static class StartupRestore
{
    /// <summary>A remembered page is usable when it is an absolute https Arena address (sign-in hosts excluded — restore only ever reopens Arena itself).</summary>
    public static bool IsUsableLastPage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme == "https" && ArenaModes.ForUrl(url) is not null && UrlPolicy.IsInAppHost(uri.Host);
    }

    /// <summary>
    /// The page a launch opens: Home when safe mode is on; otherwise the
    /// remembered page when it is still usable; otherwise the startup
    /// mode's page. Never returns null or an unusable address.
    /// </summary>
    public static string Resolve(bool safeMode, string? lastPage, ArenaMode startupMode)
    {
        if (safeMode)
            return UrlPolicy.HomeUrl;
        if (IsUsableLastPage(lastPage))
            return lastPage!.Trim();
        return startupMode.Url;
    }

    /// <summary>The status line after a restore, so the user knows why they are where they are.</summary>
    public static string RestoredNote(bool safeMode, bool restoredLastPage) =>
        safeMode
            ? "Safe mode — starting at Arena's home page"
            : restoredLastPage
                ? "Picked up where you left off"
                : "";
}

// ───────────────────────── run-queue rebase (enhancement pack) ─────────────────────────

/// <summary>
/// Reordering the run queue, kept pure (enhancement pack): the Battle
/// Lab's queue is a list of prompt ids, and moving one entry must not
/// disturb the rest. A move outside the list is a no-op that reports
/// "not moved", so the window's up/down buttons can't corrupt the order
/// at the ends.
/// </summary>
public static class RunQueue
{
    /// <summary>
    /// Move the entry at <paramref name="index"/> by <paramref name="delta"/>
    /// places. Returns true when the order changed. A negative index, an
    /// index past the end, or a destination outside the list leaves the
    /// list untouched and returns false.
    /// </summary>
    public static bool Move(IList<string> queue, int index, int delta)
    {
        if (queue is null || index < 0 || index >= queue.Count || delta == 0)
            return false;
        var target = index + delta;
        if (target < 0 || target >= queue.Count)
            return false;
        var item = queue[index];
        queue.RemoveAt(index);
        queue.Insert(target, item);
        return true;
    }

    /// <summary>The one-line label for a queued prompt ("2 of 5 · Prompt name"), so the queue is readable without counting rows.</summary>
    public static string PositionLabel(int index, int count, string? promptName) =>
        $"{index + 1} of {count} · {(string.IsNullOrWhiteSpace(promptName) ? "(deleted prompt)" : promptName)}";

    /// <summary>How many queued ids name no prompt — leftovers a sanitize would drop; the window offers to tidy them.</summary>
    public static int Orphans(IEnumerable<string> queue, IEnumerable<BattlePrompt> prompts)
    {
        var ids = new HashSet<string>(prompts.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        return queue.Count(id => !ids.Contains(id));
    }
}

// ───────────────────────── wording shared by the pack's surfaces (enhancement pack) ─────────────────────────

/// <summary>
/// The strings the pack's shell surfaces share, in one place so a
/// tooltip, a menu item and the Help text can't drift into three
/// slightly different sentences for the same action.
/// </summary>
public static class PackText
{
    public const string FindOptionsTooltip =
        "Find options: whole word and match case — handed straight to WebView2's own finder";

    public const string ZoomPresetsHeader = "Zoom presets";

    public const string SessionWindowTitle = "This session";

    public const string ErrorLookupHeader = "What this error means";

    public const string QueueReorderTooltip = "Move the selected prompt up or down in the run queue";

    public const string DuplicatesButton = "Find duplicates";

    public const string PinModeMenuHeader = "Pin this mode";

    public const string PinnedModeStatusPrefix = "Pinned";

    public const string RestartToApply = "Applied — this takes effect on the next launch for settings that are read at startup.";
}
