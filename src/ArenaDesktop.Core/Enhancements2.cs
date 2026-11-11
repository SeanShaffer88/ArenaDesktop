namespace ArenaDesktop.Core;

// ───────────────────────── settings backup & restore (enhancement pack) ─────────────────────────

/// <summary>What a folder-write check found (enhancement pack) — the Downloads section's "can downloads land here?" line.</summary>
public sealed record DownloadsFolderCheck(
    string? Folder,
    bool Exists,
    bool Writable,
    long? FreeBytes,
    string? Error)
{
    /// <summary>Below this much free space the check warns — a download that starts with no room fails at the end.</summary>
    public const long LowSpaceThreshold = 500L * 1024L * 1024L;

    public bool LowSpace => FreeBytes is { } free && free >= 0 && free < LowSpaceThreshold;

    /// <summary>True only when the folder exists, a file could be written, and space is not low.</summary>
    public bool Healthy => string.IsNullOrEmpty(Error) && Exists && Writable && !LowSpace;

    /// <summary>A check that could not run (an exception while probing) — reported honestly, never as a pass.</summary>
    public static DownloadsFolderCheck Failed(string? folder, string error) =>
        new(folder, Exists: false, Writable: false, FreeBytes: null, Error: error);

    /// <summary>
    /// The one-line result Settings and Diagnostics show. A null folder
    /// means "the Windows Downloads folder" — that case is reported as
    /// not checked rather than guessed at, because resolving the system
    /// folder is the shell's job.
    /// </summary>
    public string Line
    {
        get
        {
            if (!string.IsNullOrEmpty(Error))
                return $"Couldn't check the downloads folder: {Error}";
            if (string.IsNullOrWhiteSpace(Folder))
                return "Using your Windows Downloads folder (not checked).";
            if (!Exists)
                return $"{Folder} doesn't exist — downloads will fall back to your Downloads folder until this is fixed.";
            if (!Writable)
                return $"{Folder} can't be written to — downloads will fail. Choose another folder.";
            if (LowSpace)
                return $"{Folder} is writable, but only {DownloadText.FormatBytes(FreeBytes ?? 0)} is free — downloads may fail.";
            var space = FreeBytes is { } free ? $" · {DownloadText.FormatBytes(free)} free" : "";
            return $"{Folder} is ready{space}.";
        }
    }
}

// ───────────────────────── startup time trend (enhancement pack) ─────────────────────────

/// <summary>
/// The startup-time trend line for Diagnostics (enhancement pack): the
/// median of the recorded first-page times, compared with the median of
/// the older runs, so one slow launch (an update check on a bad Wi-Fi
/// day) doesn't get reported as the app getting slower. Only completed
/// runs with a first-page time count; medians need at least two runs on
/// each side, and the trend is "steady" until they exist — the line
/// never invents a change from a single reading.
/// </summary>
public static class StartupTrend
{
    /// <summary>A shift smaller than this many milliseconds reads as steady (noise, not a trend).</summary>
    public const long SteadyThresholdMs = 250;

    public static string Summary(IEnumerable<StartupTimings> runsNewestFirst)
    {
        var times = runsNewestFirst
            .Where(r => r.FirstNavigationMs is { } ms && ms > 0)
            .Select(r => r.FirstNavigationMs!.Value)
            .ToList();
        if (times.Count == 0)
            return "No completed startup has a first-page time recorded yet.";
        var newest = times[0];
        if (times.Count < 4)
            return $"Latest first page: {newest:N0} ms (a few more launches will show a trend).";
        var recent = Median(times.Take(3));
        var older = Median(times.Skip(3).Take(5));
        var delta = recent - older;
        if (Math.Abs(delta) < SteadyThresholdMs)
            return $"Startup is steady — first page about {recent:N0} ms over the last 3 launches.";
        return delta > 0
            ? $"Startup is slower by about {delta:N0} ms — last 3 launches averaged {recent:N0} ms, earlier ones {older:N0} ms."
            : $"Startup is faster by about {-delta:N0} ms — last 3 launches averaged {recent:N0} ms, earlier ones {older:N0} ms.";
    }

    private static long Median(IEnumerable<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}

// ───────────────────────── duplicate-prompt detection (Battle Lab) ─────────────────────────

/// <summary>
/// Finds prompts that are the same text under different names
/// (enhancement pack): a library quietly grows copies when the same
/// prompt is re-added, imported, or duplicated before a rename. Access
/// stays separate from identity — a prompt can reasonably appear in
/// different categories — so this groups by TEXT, with whitespace and
/// case folded, and reports every group of two or more. Name-only
/// matches are a separate, softer signal (<see cref="SameNameGroups"/>),
/// because two prompts may legitimately share a name across categories.
/// </summary>
public static class DuplicatePrompts
{
    /// <summary>Groups of prompts whose bodies match after folding; each group lists the prompt names (library order).</summary>
    public static IReadOnlyList<IReadOnlyList<string>> SameTextGroups(IEnumerable<BattlePrompt> prompts)
    {
        return prompts
            .Where(p => !string.IsNullOrWhiteSpace(Fold(p.Body)))
            .GroupBy(p => Fold(p.Body), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<string>)g.Select(p => p.Name).ToList())
            .ToList();
    }

    /// <summary>Groups of prompts sharing a name (trimmmed, case-insensitive), each with its distinct categories.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> SameNameGroups(IEnumerable<BattlePrompt> prompts)
    {
        return prompts
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<string>)g
                .Select(p => string.IsNullOrWhiteSpace(p.Category) ? "(no category)" : p.Category.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList())
            .ToList();
    }

    /// <summary>The plain-language report the Battle Lab shows behind its "Find duplicates" button. Null means "nothing to report".</summary>
    public static string? Report(IEnumerable<BattlePrompt> prompts)
    {
        var list = prompts as IReadOnlyList<BattlePrompt> ?? prompts.ToList();
        var sameText = SameTextGroups(list);
        var sameName = SameNameGroups(list);
        if (sameText.Count == 0 && sameName.Count == 0)
            return null;
        var lines = new List<string>();
        if (sameText.Count > 0)
        {
            var copies = sameText.Sum(g => g.Count);
            lines.Add($"{sameText.Count} {(sameText.Count == 1 ? "group" : "groups")} of prompts with identical text ({copies} prompts):");
            foreach (var group in sameText)
                lines.Add("  · " + string.Join(" / ", group));
        }
        if (sameName.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.Add($"{sameName.Count} name{(sameName.Count == 1 ? " is" : "s are")} used more than once (categories differ — that may be fine):");
            foreach (var group in sameName)
                lines.Add("  · " + string.Join(", ", group));
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Fold text for identity: every run of whitespace becomes one space, trimmed, lower-cased.</summary>
    public static string Fold(string? body) =>
        string.Join(' ', (body ?? "").ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

// ───────────────────────── pinned mode page (enhancement pack) ─────────────────────────

/// <summary>
/// The pinned mode (enhancement pack): ONE mode the user pins so the
/// app can jump back to it from anywhere — restore it at the next launch
/// and after a recovery reload, and show it in the status bar's
/// activity area. Pinning is a preference (settings), so a pinned mode
/// survives restarts; it is independent of the per-profile start page
/// and of the startup-mode choice, and it never changes what a launch
/// does by itself — it offers a jump, it doesn't hijack navigation.
/// </summary>
public static class PinnedMode
{
    /// <summary>The canonical pinned-mode id for <paramref name="modeId"/>, or null to pin nothing (unknown ids are refused, not guessed).</summary>
    public static string? Sanitize(string? modeId) =>
        ArenaModes.TryGet(modeId, out var mode) ? mode.Id : null;

    /// <summary>The pinned mode, or null when nothing usable is pinned.</summary>
    public static ArenaMode? Resolve(string? modeId) =>
        ArenaModes.TryGet(modeId, out var mode) ? mode : null;

    /// <summary>The status-bar label that names the pinned mode ("Battle · Ctrl+Alt+1") — null when nothing is pinned, so the status line stays clean by default.</summary>
    public static string? StatusLabel(string? modeId) =>
        Resolve(modeId) is { } mode ? $"Pinned: {mode.Label}" : null;

    /// <summary>The tooltip explaining what the status label does.</summary>
    public static string Tooltip(ArenaMode mode) =>
        $"Pinned to {mode.Label}. Click to go there; unpin from the ⋯ menu or Settings.";
}

// ───────────────────────── session summary for the diagnostics report (enhancement pack) ─────────────────────────

/// <summary>
/// The Session block appended to the Diagnostics report (enhancement
/// pack): what this run of the app actually did, in numbers the user
/// can read before sharing a report. It is session state only, so the
/// report says exactly that — nothing here survived a restart, and
/// nothing came from the Arena page.
/// </summary>
public static class SessionReport
{
    public static IReadOnlyList<string> Lines(SessionStats stats, DateTimeOffset now, string? pinnedModeLabel)
    {
        var lines = new List<string>
        {
            "Session (this run only — nothing is saved)",
            $"  Started {stats.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · on Arena {SessionStats.FormatDuration(stats.TimeOnArena)}",
            $"  Pages {stats.PagesVisited} · mode jumps {stats.ModeJumps} · downloads finished {stats.DownloadsCompleted} · prompts copied {stats.PromptsCopied}",
        };
        if (!string.IsNullOrWhiteSpace(pinnedModeLabel))
            lines.Add($"  Pinned mode: {pinnedModeLabel}");
        return lines;
    }
}

// ───────────────────────── quieter shell (enhancement pack) ─────────────────────────

/// <summary>
/// Shell chrome that stays out of the way (enhancement pack): the
/// setting behind "Dim the toolbar while I'm reading". Off by default —
/// the shell's existing look is the touchstone, and this is an opt-in
/// for people who want even less furniture. Kept pure so the wording
/// and the threshold are tested rather than buried in a XAML handler.
/// </summary>
public static class ShellQuiet
{
    /// <summary>The opacity the toolbar dims to while the page has keyboard focus. Never below this: the buttons must stay discoverable.</summary>
    public const double DimmedToolbarOpacity = 0.72;

    /// <summary>The opacity the toolbar returns to when it has focus or the pointer is over it.</summary>
    public const double FullToolbarOpacity = 1.0;

    /// <summary>
    /// The toolbar's opacity for the current state: full while the
    /// setting is off, full while the shell (not the page) owns focus
    /// or the pointer is over the toolbar, dimmed otherwise.
    /// </summary>
    public static double ToolbarOpacity(bool dimWhenReading, bool pageHasFocus, bool pointerOverToolbar) =>
        !dimWhenReading || !pageHasFocus || pointerOverToolbar
            ? FullToolbarOpacity
            : DimmedToolbarOpacity;

    /// <summary>The setting's label in Settings.</summary>
    public const string DimSettingLabel = "Dim the toolbar while I'm reading";

    /// <summary>The setting's explanation — what it does and what it never does.</summary>
    public const string DimSettingNote =
        "Cools the toolbar down to a faint presence while the Arena page has focus, and restores it the moment you move the pointer to it or tab back into the shell. The page itself is never affected.";
}

// ───────────────────────── notes carried by numbered notifications (enhancement pack) ─────────────────────────

/// <summary>
/// The tray balloon for a finished download (enhancement pack): the
/// existing "download finished" balloon now carries the file's size and
/// folder, so a glance answers "which file, how big, where". The pure
/// composer keeps the two balloons (one-file and batch) and their
/// casing consistent and testable.
/// </summary>
public static class DownloadNotificationText
{
    /// <summary>The title line: the file's name for a single download, or a count for a batch.</summary>
    public static string Title(string? firstName, int count) =>
        count <= 1
            ? $"Downloaded {firstName ?? "a file"}"
            : $"{count} downloads finished";

    /// <summary>
    /// The body line: size and folder for one file, the total size for a
    /// batch. A null size means "not known yet" (the history save hasn't
    /// landed) — the line then drops the number instead of claiming 0 B.
    /// </summary>
    public static string Body(long? sizeBytes, string? folder, int count)
    {
        var where = folder ?? "your Downloads folder";
        var size = sizeBytes is { } bytes ? DownloadText.FormatBytes(bytes) : null;
        if (count > 1)
            return size is null ? $"{count} files · {where}" : $"{size} in total · {where}";
        return size is null ? $"Saved to {where}" : $"{size} · saved to {where}";
    }
}
