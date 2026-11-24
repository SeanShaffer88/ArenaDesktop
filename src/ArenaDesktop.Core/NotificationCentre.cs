using System.Text.Json;

namespace ArenaDesktop.Core;

/// <summary>Notification centre categories (completeness slice D3).</summary>
public enum NotificationCategory { Battle, Agent, Download, Connector, System }

/// <summary>One durable notification. Balloons are transient; this is the record.</summary>
public sealed class NotificationEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public NotificationCategory Category { get; set; } = NotificationCategory.System;
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public bool Read { get; set; }
}

/// <summary>
/// The notification centre store (`notifications.json` beside the
/// settings): capped, newest first, read/unread tracked, per-category
/// muting judged here so every producer obeys the same rule.
/// Categories cover what the app itself can know: battle/comparison
/// results the user recorded or ran, local agent-side tasks
/// (terminal/git), downloads, connector-ish events the app performs,
/// and system events (updates). Nothing here invents web-side events
/// the app cannot see.
/// </summary>
public sealed class NotificationStore
{
    public const int MaxEntries = 200;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    public List<NotificationEntry> Entries { get; private set; } = [];

    private NotificationStore(string path) => _path = path;

    public static NotificationStore Load(string path)
    {
        var store = new NotificationStore(path);
        try
        {
            if (File.Exists(path))
                store.Entries = JsonSerializer.Deserialize<List<NotificationEntry>>(File.ReadAllText(path), Options) ?? [];
        }
        catch { store.Entries = []; }
        store.Entries = store.Entries.OrderByDescending(e => e.CreatedUtc).Take(MaxEntries).ToList();
        return store;
    }

    public int UnreadCount => Entries.Count(e => !e.Read);

    public NotificationEntry Add(NotificationCategory category, string title, string body)
    {
        var entry = new NotificationEntry
        {
            Category = category,
            Title = title.Trim(),
            Body = body.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        Entries.Insert(0, entry);
        if (Entries.Count > MaxEntries) Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
        Save();
        return entry;
    }

    public void MarkAllRead()
    {
        foreach (var e in Entries) e.Read = true;
        Save();
    }

    public void MarkRead(string id)
    {
        if (Entries.FirstOrDefault(e => e.Id == id) is { } entry) { entry.Read = true; Save(); }
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private void Save() =>
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Entries, Options));

    /// <summary>Per-category toggles: a category is on unless its name is in the muted list.</summary>
    public static bool IsCategoryEnabled(IReadOnlyCollection<string>? mutedCategories, NotificationCategory category) =>
        mutedCategories is null || !mutedCategories.Contains(category.ToString(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Repair a muted-categories list from settings: keep only real category names, de-duplicated.</summary>
    public static List<string> SanitizeMuted(IEnumerable<string>? muted) =>
        (muted ?? [])
            .Where(name => Enum.TryParse<NotificationCategory>(name, ignoreCase: true, out _))
            .Select(name => Enum.Parse<NotificationCategory>(name, ignoreCase: true).ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
