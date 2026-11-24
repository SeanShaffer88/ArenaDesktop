namespace ArenaDesktop.Core;

/// <summary>One browser tab's durable state: where it is and what it is called.</summary>
public sealed class BrowserTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";

    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title
        : Uri.TryCreate(Url, UriKind.Absolute, out var u) && u.Host.Length > 0 ? u.Host
        : "New tab";
}

/// <summary>
/// The tab set for one profile (completeness slice D1): open / close /
/// activate / reorder, a closed-tab stack for reopen (Ctrl+Shift+T),
/// and sanitization for persistence. The model stays view-agnostic:
/// it records which tabs currently hold a LIVE view (multi-view
/// slice, docs/MULTI_VIEW_TABS.md) in activation order, and decides
/// eviction when the live-view cap is reached — the shell owns the
/// actual WebView2 controls and only obeys these decisions.
/// </summary>
public sealed class TabSet
{
    public const int MaxTabs = 12;
    public const int MaxClosed = 10;

    /// <summary>
    /// How many tabs may hold a live WebView2 view at once. A live
    /// view is a full renderer process; the cap bounds memory while
    /// keeping every recently used tab instant. Tabs beyond the cap
    /// fall back to state tabs (URL/title kept, page reloads on
    /// activation) — the v0.20.0 behaviour.
    /// </summary>
    public const int MaxLiveViews = 6;

    public List<BrowserTab> Tabs { get; set; } = [];
    public string ActiveTabId { get; set; } = "";
    public List<BrowserTab> Closed { get; set; } = [];

    /// <summary>
    /// Ids of tabs holding a live view, least-recently-activated
    /// first. Maintained by <see cref="RegisterLiveView"/>,
    /// <see cref="Activate"/> and <see cref="Close"/>; pruned by
    /// <see cref="Sanitize"/>. Runtime state only (JsonIgnore): a
    /// view exists only while the process runs, so a persisted copy
    /// would be a lie after a restart — the shell rebuilds it as
    /// views are created.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> LiveViewIds { get; set; } = [];

    public bool HasLiveView(string tabId) => LiveViewIds.Contains(tabId);

    /// <summary>
    /// Record that a tab now holds a live view (or was re-activated
    /// with one). Returns the id of the least-recently-used OTHER tab
    /// whose view the shell must dispose when the cap is exceeded, or
    /// null when nothing needs evicting. The named tab itself is
    /// never the eviction candidate.
    /// </summary>
    public string? RegisterLiveView(string tabId)
    {
        if (Tabs.All(t => t.Id != tabId)) return null;
        LiveViewIds.Remove(tabId);
        LiveViewIds.Add(tabId);
        if (LiveViewIds.Count <= MaxLiveViews) return null;
        var evict = LiveViewIds.First(id => id != tabId);
        LiveViewIds.Remove(evict);
        return evict;
    }

    /// <summary>Record that a tab's live view is gone (closed, evicted, or its environment died).</summary>
    public void UnregisterLiveView(string tabId) => LiveViewIds.Remove(tabId);

    /// <summary>Forget every live view (the shared environment died; all views go with it).</summary>
    public void ClearLiveViews() => LiveViewIds.Clear();

    public BrowserTab? Active => Tabs.FirstOrDefault(t => t.Id == ActiveTabId) ?? Tabs.FirstOrDefault();

    public BrowserTab Open(string url, string title = "")
    {
        if (Tabs.Count >= MaxTabs) return Active!;
        var tab = new BrowserTab { Url = url, Title = title };
        Tabs.Add(tab);
        ActiveTabId = tab.Id;
        return tab;
    }

    /// <summary>Close a tab; returns the tab that should become visible (null when none remain).</summary>
    public BrowserTab? Close(string tabId)
    {
        var index = Tabs.FindIndex(t => t.Id == tabId);
        if (index < 0) return Active;
        var tab = Tabs[index];
        Tabs.RemoveAt(index);
        LiveViewIds.Remove(tabId);
        Closed.Insert(0, tab);
        if (Closed.Count > MaxClosed) Closed.RemoveRange(MaxClosed, Closed.Count - MaxClosed);
        if (ActiveTabId == tabId)
        {
            var next = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
            ActiveTabId = next?.Id ?? "";
            return next;
        }
        return Active;
    }

    public bool Activate(string tabId)
    {
        if (Tabs.All(t => t.Id != tabId)) return false;
        ActiveTabId = tabId;
        // Activation order is the eviction order: a live view the
        // user just visited is the last one that may be evicted.
        if (LiveViewIds.Remove(tabId)) LiveViewIds.Add(tabId);
        return true;
    }

    /// <summary>Reopen the most recently closed tab (restoring its place at the end).</summary>
    public BrowserTab? ReopenClosed()
    {
        if (Closed.Count == 0 || Tabs.Count >= MaxTabs) return null;
        var tab = Closed[0];
        Closed.RemoveAt(0);
        Tabs.Add(tab);
        ActiveTabId = tab.Id;
        return tab;
    }

    public void Move(string tabId, int delta)
    {
        var index = Tabs.FindIndex(t => t.Id == tabId);
        if (index < 0) return;
        var target = Math.Clamp(index + delta, 0, Tabs.Count - 1);
        if (target == index) return;
        var tab = Tabs[index];
        Tabs.RemoveAt(index);
        Tabs.Insert(target, tab);
    }

    /// <summary>Repair persisted state: drop blank tabs, guarantee unique ids and a valid active id.</summary>
    public static TabSet Sanitize(TabSet? set)
    {
        var result = set ?? new TabSet();
        result.Tabs = (result.Tabs ?? []).Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Url)).Take(MaxTabs).ToList();
        result.Closed = (result.Closed ?? []).Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Url)).Take(MaxClosed).ToList();
        var seen = new HashSet<string>();
        foreach (var tab in result.Tabs.Concat(result.Closed))
        {
            if (string.IsNullOrWhiteSpace(tab.Id) || !seen.Add(tab.Id)) tab.Id = Guid.NewGuid().ToString("n");
            seen.Add(tab.Id);
            tab.Title ??= "";
            tab.Url = tab.Url.Trim();
        }
        if (result.Tabs.All(t => t.Id != result.ActiveTabId))
            result.ActiveTabId = result.Tabs.FirstOrDefault()?.Id ?? "";
        // Live-view bookkeeping: only ids of surviving tabs, no
        // duplicates (the LAST occurrence wins — it is the most
        // recent activation), capped with the most recent kept.
        var tabIds = result.Tabs.Select(t => t.Id).ToHashSet();
        var live = new List<string>();
        foreach (var id in result.LiveViewIds ?? [])
        {
            if (!tabIds.Contains(id)) continue;
            live.Remove(id);
            live.Add(id);
        }
        if (live.Count > MaxLiveViews) live.RemoveRange(0, live.Count - MaxLiveViews);
        result.LiveViewIds = live;
        return result;
    }
}
