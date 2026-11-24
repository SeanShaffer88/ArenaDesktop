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
/// and sanitization for persistence. View-agnostic on purpose: the
/// shell hosts tabs on its browsing view today (see
/// KNOWN_LIMITATIONS — state tabs, pages reload on switch); a future
/// multi-view host can adopt this model unchanged.
/// </summary>
public sealed class TabSet
{
    public const int MaxTabs = 12;
    public const int MaxClosed = 10;

    public List<BrowserTab> Tabs { get; set; } = [];
    public string ActiveTabId { get; set; } = "";
    public List<BrowserTab> Closed { get; set; } = [];

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
        return result;
    }
}
