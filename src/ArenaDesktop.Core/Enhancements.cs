namespace ArenaDesktop.Core;

// ───────────────────────── find-in-page options (enhancement pack) ─────────────────────────

/// <summary>
/// Find-in-page options and wording (enhancement pack, v0.4.0), kept in
/// Core so they are testable. The searching itself stays inside
/// WebView2's own <c>CoreWebView2Find</c> (pinned SDK 1.0.4258.31),
/// which is the only party that can see the page: the app hands it a
/// term, whole-word and match-case flags, and reads back a match
/// count. Nothing here reads page content.
/// </summary>
public static class FindOptionsPolicy
{
    /// <summary>
    /// Whether the term can be handed to WebView2. Blank terms and
    /// absurdly long ones are refused here rather than started as a
    /// find that can never match — the bar then says why.
    /// </summary>
    public static bool IsUsableTerm(string? term, int maxLength, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(term))
        {
            reason = "Type something to find.";
            return false;
        }
        if (term.Length > maxLength)
        {
            reason = $"That search text is too long (limit {maxLength} characters).";
            return false;
        }
        return true;
    }

    /// <summary>
    /// The result line the find bar shows. <paramref name="matchCount"/>
    /// and <paramref name="activeIndex"/> come straight from WebView2
    /// (activeIndex is 0-based). Never invents a count or a position.
    /// </summary>
    public static string ProgressText(int matchCount, int activeIndex)
    {
        if (matchCount <= 0)
            return "No results";
        if (activeIndex < 0 || activeIndex >= matchCount)
            return $"{matchCount} {(matchCount == 1 ? "match" : "matches")}";
        return $"{activeIndex + 1} of {matchCount}";
    }

    /// <summary>The options summary for the bar's tooltip ("Whole word · Match case"), empty when both are off.</summary>
    public static string OptionsSummary(bool wholeWord, bool matchCase)
    {
        var parts = new List<string>();
        if (wholeWord)
            parts.Add("Whole word");
        if (matchCase)
            parts.Add("Match case");
        return string.Join(" · ", parts);
    }
}

// ───────────────────────── zoom presets (enhancement pack) ─────────────────────────

/// <summary>
/// Zoom presets in the View menu (enhancement pack, v0.4.0): named
/// jumps that sit beside the existing step-wise Ctrl+± — the two are
/// complementary, not a replacement. Every preset is also a real step
/// in the standard table (or the extended table when the extended range
/// is on), so picking a preset leaves the zoom somewhere Ctrl+± can
/// continue from. A preset outside the active range is disabled in the
/// menu rather than silently clamped: the menu tells the truth about
/// what picking it does.
/// </summary>
public sealed record ZoomPreset(string Id, string Label, double Factor)
{
    /// <summary>The percentage text the menu shows ("150%").</summary>
    public string Display => ZoomMath.FormatPercent(Factor);
}

public static class ZoomPresets
{
    public static readonly IReadOnlyList<ZoomPreset> All = new[]
    {
        new ZoomPreset("half", "50%", 0.50),
        new ZoomPreset("three-quarters", "75%", 0.75),
        new ZoomPreset("actual", "100% (actual size)", 1.00),
        new ZoomPreset("one-fifty", "150%", 1.50),
        new ZoomPreset("double", "200%", 2.00),
    };

    /// <summary>The preset with this id, or null.</summary>
    public static ZoomPreset? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id == id);

    /// <summary>Whether the active zoom range can hold this preset (the extended range adds 300% and the like; the standard range tops out at 200%).</summary>
    public static bool IsAvailable(ZoomPreset preset, bool extendedRange) =>
        preset.Factor >= ZoomMath.MinFor(extendedRange) - 0.001
        && preset.Factor <= ZoomMath.MaxFor(extendedRange) + 0.001;

    /// <summary>The menu source: every preset, each flagged with availability for the current range.</summary>
    public static IReadOnlyList<(ZoomPreset Preset, bool Available)> MenuItems(bool extendedRange) =>
        All.Select(p => (p, IsAvailable(p, extendedRange))).ToList();
}

// ───────────────────────── session stats & page reading time (enhancement pack) ─────────────────────────

/// <summary>One finished page visit, in the session stats (enhancement pack): the mode it was inside (or null) and how long it was on screen.</summary>
public sealed class PageVisit
{
    public string? ModeId { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// What the session has actually DONE, shown in the new Session window
/// (enhancement pack, v0.4.0): pages visited, time on Arena, downloads
/// finished, mode jumps, prompts copied. This is a count kept in memory
/// for the life of the process — never written anywhere, never sent
/// anywhere, cleared by closing the app — so it is the opposite of
/// telemetry: the user is the only reader it has.
/// </summary>
public sealed class SessionStats
{
    /// <summary>Longest single visit counted, so a window left open overnight doesn't read as a heroic reading session.</summary>
    public static readonly TimeSpan MaxVisitDuration = TimeSpan.FromHours(4);

    /// <summary>How many visit rows are kept for the reading-time-per-mode table.</summary>
    public const int MaxVisits = 500;

    public DateTimeOffset StartedUtc { get; }
    public int PagesVisited { get; private set; }
    public int ModeJumps { get; private set; }
    public int DownloadsCompleted { get; private set; }
    public int PromptsCopied { get; private set; }
    public TimeSpan TimeOnArena { get; private set; }
    public List<PageVisit> Visits { get; } = new();

    public SessionStats(DateTimeOffset? startedUtc = null) => StartedUtc = startedUtc ?? DateTimeOffset.UtcNow;

    /// <summary>Count one top-level page the user landed on (consecutive duplicates are the caller's to collapse, as the history model does).</summary>
    public void RecordPage(string? modeId = null)
    {
        PagesVisited++;
        if (Visits.Count >= MaxVisits)
            return;
        Visits.Add(new PageVisit { ModeId = ResolveModeId(modeId) });
    }

    /// <summary>Count one mode jump (a toolbar, tray, menu or shortcut navigation to a named mode).</summary>
    public void RecordModeJump() => ModeJumps++;

    public void RecordDownloadCompleted() => DownloadsCompleted++;

    public void RecordPromptCopied() => PromptsCopied++;

    /// <summary>
    /// Add screen time to the CURRENT (last) visit. Values outside
    /// 0…<see cref="MaxVisitDuration"/> are ignored rather than clamped
    /// upward — a clock jump or a sleep gap must not invent attention.
    /// </summary>
    public void AddTime(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero || delta > MaxVisitDuration)
            return;
        TimeOnArena += delta;
        if (Visits.Count == 0)
            Visits.Add(new PageVisit());
        Visits[^1].Duration += delta;
    }

    /// <summary>Reading time per mode, biggest first; visits with no recorded mode gather under "Not recorded".</summary>
    public IReadOnlyList<(string Label, TimeSpan Duration)> TimeByMode()
    {
        var labels = ArenaModes.All.ToDictionary(m => m.Id, m => m.Label, StringComparer.Ordinal);
        return Visits
            .GroupBy(v => v.ModeId is not null && labels.ContainsKey(v.ModeId) ? v.ModeId : "")
            .Select(g => (
                Label: g.Key.Length > 0 ? labels[g.Key] : "Not recorded",
                Duration: new TimeSpan(g.Sum(v => v.Duration.Ticks))))
            .Where(t => t.Duration > TimeSpan.Zero)
            .OrderByDescending(t => t.Duration)
            .ThenBy(t => t.Label, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The plain-language summary lines the Session window shows, newest-first data with no surprises.</summary>
    public IReadOnlyList<string> SummaryLines(DateTimeOffset now)
    {
        var lines = new List<string>
        {
            $"This session started {DescribeSince(StartedUtc, now)}.",
            $"On Arena: {FormatDuration(TimeOnArena)} · {PagesVisited} {(PagesVisited == 1 ? "page" : "pages")} visited",
            $"{ModeJumps} mode {(ModeJumps == 1 ? "jump" : "jumps")} · {DownloadsCompleted} {(DownloadsCompleted == 1 ? "download" : "downloads")} finished · {PromptsCopied} {(PromptsCopied == 1 ? "prompt" : "prompts")} copied",
        };
        var byMode = TimeByMode();
        if (byMode.Count > 0)
            lines.Add("Longest: " + string.Join(" · ", byMode.Take(3).Select(t => $"{t.Label} {FormatDuration(t.Duration)}")));
        lines.Add("Counted in memory for this session only — nothing is saved, and nothing leaves this PC.");
        return lines;
    }

    /// <summary>"3 min", "1 h 12 min", "under a minute". Rounded down to the minute, never up.</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "none";
        if (duration < TimeSpan.FromMinutes(1))
            return "under a minute";
        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;
        return hours > 0
            ? (minutes > 0 ? $"{hours} h {minutes} min" : $"{hours} h")
            : $"{minutes} min";
    }

    private static string DescribeSince(DateTimeOffset started, DateTimeOffset now)
    {
        var minutes = (int)(now - started).TotalMinutes;
        if (minutes < 1)
            return "just now";
        if (minutes < 60)
            return $"{minutes} min ago";
        var hours = minutes / 60;
        return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
    }

    private static string? ResolveModeId(string? modeId) =>
        ArenaModes.TryGet(modeId, out var mode) ? mode.Id : null;
}

// ───────────────────────── error-code lookup for the failure screen ─────────────────────────

/// <summary>
/// Turns the raw WebView2 failure status into words a non-technical
/// person can act on (enhancement pack). <see cref="LoadFailureClassifier"/>
/// already groups statuses into categories with long messages; this
/// adds the CODE, the short cause, and the short next step, so the
/// failure screen can show "HOSTNAMENOTRESOLVED · I couldn't look up
/// the address · Check your connection and try again" under the long
/// explanation. Codes are matched case-insensitively on their names,
/// the way WebView2 writes them (<c>HostNameNotResolved</c>).
/// </summary>
public sealed record ErrorLookupEntry(string Code, string Cause, string NextStep)
{
    /// <summary>The line the failure screen prints under its message.</summary>
    public string Line => $"{Code} — {Cause}. {NextStep}";
}

public static class ErrorCodeLookup
{
    public static readonly IReadOnlyList<ErrorLookupEntry> All = new[]
    {
        new ErrorLookupEntry("DISCONNECTED", "the connection dropped", "Check your connection and try again"),
        new ErrorLookupEntry("CANNOTCONNECT", "the connection couldn't be made", "Check your connection and try again"),
        new ErrorLookupEntry("CONNECTIONABORTED", "the connection was cut off", "Check your connection and try again"),
        new ErrorLookupEntry("CONNECTIONRESET", "the connection was reset", "Check your connection and try again"),
        new ErrorLookupEntry("SERVERUNREACHABLE", "Arena's server couldn't be reached", "Wait a moment, then reload"),
        new ErrorLookupEntry("TIMEOUT", "the page took too long to answer", "Check your connection, then reload"),
        new ErrorLookupEntry("HOSTNAMENOTRESOLVED", "that address couldn't be found", "Check your connection or network settings, then reload"),
        new ErrorLookupEntry("CERTIFICATECOMMONNAMEISINCORRECT", "the security certificate doesn't match the site", "Check that this PC's date and time are right, then reload"),
        new ErrorLookupEntry("CERTIFICATEEXPIRED", "the security certificate has expired", "Check that this PC's date and time are right, then reload"),
        new ErrorLookupEntry("CERTIFICATEREVOKED", "the security certificate was revoked", "Wait a moment; if it persists, Arena's certificate needs attention"),
        new ErrorLookupEntry("CERTIFICATEISINVALID", "the security certificate isn't valid", "Check that this PC's date and time are right, then reload"),
        new ErrorLookupEntry("CLIENTCERTIFICATECONTAINTSERRORS", "the client certificate has errors", "Reload; if it persists, check your network's proxy settings"),
    };

    /// <summary>The entry for a <c>CoreWebView2WebErrorStatus</c> name, or a safe generic entry for anything unrecognized (including null).</summary>
    public static ErrorLookupEntry For(string? statusName)
    {
        if (!string.IsNullOrWhiteSpace(statusName))
        {
            var name = statusName.Trim().ToUpperInvariant();
            foreach (var entry in All)
                if (entry.Code == name)
                    return entry;
        }
        return new ErrorLookupEntry("UNKNOWN", "the page didn't load", "Check your connection, then reload");
    }

    /// <summary>The one-line explanation, for a status bar or a copied bug report.</summary>
    public static string LineFor(string? statusName) => For(statusName).Line;
}

// ───────────────────────── searchable keyboard cheat sheet ─────────────────────────

/// <summary>
/// Search + grouping over <see cref="ShortcutCatalog"/> for the
/// shortcut cheat sheet (enhancement pack): the catalog already knows
/// every real binding, so the sheet only needs a query rule. Every
/// search term must prefix-match a word of the action's name, its
/// gesture text, or its group — the same whole-word prefix rule the
/// Settings and Help searches use, so all three feel the same. A blank
/// query lists everything.
/// </summary>
public static class ShortcutCheatSheet
{
    /// <summary>
    /// Which part of the app a shortcut belongs to. Derived from the
    /// catalog id so the catalog stays the single source of truth:
    /// a new mode key lands in "Arena modes" automatically.
    /// </summary>
    public static string GroupFor(ShortcutAction action) => action.Id switch
    {
        _ when action.Id.StartsWith("mode-", StringComparison.Ordinal) => "Arena modes",
        ShortcutCatalog.SettingsId or ShortcutCatalog.DownloadsId or ShortcutCatalog.HomeId or "shortcuts" => "Opening things",
        "find" or "print" or "reload" or "restore-closed" => "The page",
        "back" or "forward" or "focus-page" => "Moving around",
        // The status line is read off the page, not the window: group it
        // with the page keys so "read" finds it where a user looks.
        "status-line" => "The page",
        "zoom-in" or "zoom-out" or "zoom-reset" => "Zoom",
        _ => "The window",
    };

    /// <summary>The groups in the order the cheat sheet shows them.</summary>
    public static readonly IReadOnlyList<string> GroupOrder = new[]
    {
        "Opening things", "The page", "Moving around", "Zoom", "The window", "Arena modes",
    };

    /// <summary>Actions matching <paramref name="query"/>, in catalog order.</summary>
    public static IReadOnlyList<ShortcutAction> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ShortcutCatalog.All;
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Select(t => t.ToLowerInvariant())
                         .ToList();
        if (terms.Count == 0)
            return ShortcutCatalog.All;
        return ShortcutCatalog.All.Where(a => terms.All(t => Matches(a, t))).ToList();
    }

    /// <summary>Actions matching the query, grouped for display: (group title, actions in catalog order), empty groups dropped.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<ShortcutAction> Actions)> Grouped(string? query)
    {
        var matches = Search(query);
        return GroupOrder
            .Select(group => (Group: group, Actions: (IReadOnlyList<ShortcutAction>)matches
                .Where(a => GroupFor(a) == group)
                .ToList()))
            .Where(g => g.Actions.Count > 0)
            .ToList();
    }

    /// <summary>The heading line: how many shortcuts the filter shows, and whether any of them can be changed in Settings.</summary>
    public static string SummaryText(int shown, int total)
    {
        var baseLine = shown == total
            ? $"{total} shortcuts"
            : $"{shown} of {total} shortcuts";
        return baseLine + " · click a row to copy its keys";
    }

    private static bool Matches(ShortcutAction action, string term)
    {
        var haystack = $"{action.Name} {action.DisplayGestures} {GroupFor(action)}".ToLowerInvariant();
        return haystack.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                       .Any(word => word.StartsWith(term, StringComparison.Ordinal));
    }
}

// ───────────────────────── enhanced pasted text (Battle Lab) ─────────────────────────

/// <summary>
/// What to do about a very long prompt (enhancement pack). Arena's
/// prompt box is the page's own; the app can't know its limit, so it
/// doesn't pretend to — it only warns when a prompt is long enough
/// that pasting is likely to be awkward, and offers the two sensible
/// halves: copy anyway, or export the prompt as a .txt file the user
/// can attach or read from.
/// </summary>
public static class PastedTextPolicy
{
    /// <summary>Prompts longer than this many characters get the warning (roughly a long page of text).</summary>
    public const int WarningLength = 6000;

    /// <summary>How many characters the warning's preview keeps.</summary>
    public const int PreviewLength = 240;

    public static bool NeedsWarning(string? text) => (text?.Length ?? 0) > WarningLength;

    /// <summary>The warning line: the exact size, and why it might matter.</summary>
    public static string WarningText(string? text) =>
        $"That prompt is {(text?.Length ?? 0):N0} characters long. Arena's prompt box may cut it short or refuse the paste. " +
        "You can copy it anyway, or export it as a .txt file to attach or reference.";

    /// <summary>A single-line preview with runs of whitespace collapsed, so the dialog shows the start of the prompt without laying out a wall of text.</summary>
    public static string Preview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= PreviewLength
            ? collapsed
            : collapsed[..PreviewLength].TrimEnd() + "…";
    }

    /// <summary>The file name a prompt export gets: the prompt's name made safe, a date stamp, and .txt (never a path).</summary>
    public static string ExportFileName(string? promptName, DateTimeOffset now)
    {
        var safe = new string((promptName ?? "").Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '-')
            .ToArray());
        // A run of substituted characters ("what?/now") collapses to one
        // dash: "what--now" is noise in a file name, and a person reading
        // the folder should be able to match the file to their prompt.
        while (safe.Contains("--", StringComparison.Ordinal))
            safe = safe.Replace("--", "-", StringComparison.Ordinal);
        safe = string.Join(' ', safe.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (safe.Length == 0)
            safe = "prompt";
        if (safe.Length > 60)
            safe = safe[..60].TrimEnd();
        return $"{safe} {now.ToLocalTime():yyyy-MM-dd}.txt";
    }
}

// ───────────────────────── download queue sort & size filters ─────────────────────────

/// <summary>
/// Sorts and size filters for the Downloads window's history
/// (enhancement pack). <see cref="DownloadHistorySort"/> already covers
/// date, size and name; these are the extras the manager gains: oldest
/// first (for finding what to clean up) and a smallest-first order,
/// plus a size band. Size bands are named, not typed — "Under 1 MB",
/// "1–10 MB", "Over 10 MB", "Over 100 MB" — because a number box is a
/// test nobody asked for. The search, state filter, sort and size band
/// compose: folder queries, failed-only, largest-first, over 10 MB all
/// at once.
/// </summary>
public sealed record DownloadSizeBand(string Id, string Label, long MinBytes, long? MaxBytes)
{
    /// <summary>Whether a record's size falls inside the band — the max is exclusive, so the bands never overlap.</summary>
    public bool Contains(long sizeBytes) =>
        sizeBytes >= MinBytes && (MaxBytes is not { } max || sizeBytes < max);
}

public static class DownloadSizeBands
{
    public const long Mb = 1024L * 1024L;

    public static readonly IReadOnlyList<DownloadSizeBand> All = new[]
    {
        new DownloadSizeBand("under-1mb", "Under 1 MB", 0, Mb),
        new DownloadSizeBand("1-10mb", "1–10 MB", Mb, 10 * Mb),
        new DownloadSizeBand("over-10mb", "Over 10 MB", 10 * Mb, null),
        new DownloadSizeBand("over-100mb", "Over 100 MB", 100 * Mb, null),
    };

    public static DownloadSizeBand? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(b => b.Id == id);
}

/// <summary>The extra sort orders (enhancement pack) — the existing <see cref="DownloadHistorySort"/> values keep their meaning exactly.</summary>
public enum DownloadsSortOrder
{
    /// <summary>Newest first (the long-standing default).</summary>
    NewestFirst,

    /// <summary>Oldest first — the clean-up view.</summary>
    OldestFirst,

    /// <summary>Largest first.</summary>
    LargestFirst,

    /// <summary>Smallest first.</summary>
    SmallestFirst,

    /// <summary>File name, A–Z.</summary>
    NameAscending,
}

/// <summary>
/// The Downloads window's one query (enhancement pack): text search,
/// state filter, size band and sort, composed. Kept in Core beside
/// <see cref="DownloadHistoryQuery"/> so the window and the tests share
/// one definition; the older helper is left untouched for its callers.
/// </summary>
public static class DownloadsView
{
    public static List<DownloadRecord> Apply(
        IEnumerable<DownloadRecord> records,
        string? search,
        DownloadStateFilter filter,
        DownloadSizeBand? sizeBand,
        DownloadsSortOrder sort)
    {
        IEnumerable<DownloadRecord> query = records;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => r.FileName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        query = filter switch
        {
            DownloadStateFilter.Completed => query.Where(r => r.State == DownloadRecordState.Completed),
            DownloadStateFilter.Failed => query.Where(r => r.State == DownloadRecordState.Failed),
            _ => query,
        };
        if (sizeBand is not null)
            query = query.Where(r => sizeBand.Contains(r.SizeBytes));
        return sort switch
        {
            DownloadsSortOrder.OldestFirst => query.OrderBy(r => r.CompletedUtc).ToList(),
            DownloadsSortOrder.LargestFirst => query.OrderByDescending(r => r.SizeBytes).ToList(),
            DownloadsSortOrder.SmallestFirst => query.OrderBy(r => r.SizeBytes).ToList(),
            DownloadsSortOrder.NameAscending => query.OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => query.OrderByDescending(r => r.CompletedUtc).ToList(),
        };
    }

    /// <summary>The total size of the records shown — the "what would cleaning this view free" number.</summary>
    public static long TotalBytes(IEnumerable<DownloadRecord> records) => records.Sum(r => r.SizeBytes);

    /// <summary>The summary line under the history list: "12 files · 340.2 MB shown".</summary>
    public static string SummaryText(IReadOnlyCollection<DownloadRecord> shown) =>
        $"{shown.Count} {(shown.Count == 1 ? "file" : "files")} · {DownloadText.FormatBytes(TotalBytes(shown))} shown";
}

// ───────────────────────── reduced-motion find-bar shortcut ─────────────────────────

/// <summary>
/// The find bar's keyboard cycle (enhancement pack): Enter / Shift+Enter
/// step through matches, a repeated term re-runs the find instead of
/// leaving a stale count on screen, and Escape closes the bar and puts
/// focus back on the page. Kept as a tiny pure decision so the shell's
/// key handler can't drift from what the bar's tooltips promise.
/// </summary>
public static class FindBarKeys
{
    /// <summary>The find term currently in the box, trimmed; blank means "nothing to find".</summary>
    public static string NormalizeTerm(string? term) => (term ?? "").Trim();

    /// <summary>Whether a new term needs a fresh find (the old count no longer describes anything).</summary>
    public static bool NeedsRestart(string? previousTerm, string? newTerm) =>
        !string.Equals(NormalizeTerm(previousTerm), NormalizeTerm(newTerm), StringComparison.Ordinal);

    /// <summary>The tooltip on the next-match button, which reflects whether there is anything to step through.</summary>
    public static string NextTooltip(int matchCount) =>
        matchCount <= 0 ? "Next match (Enter) — nothing to step through yet" : "Next match (Enter)";
}
