using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArenaDesktop.Core;

/// <summary>
/// One reusable Battle prompt (ArenaDesktop v0.2.0, Battle Lab). The
/// body may carry <c>[blank]</c> placeholders (see
/// <see cref="PromptBlanks"/>) that are filled in at copy time — the
/// stored body always keeps the placeholders.
/// </summary>
public sealed class BattlePrompt
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Body { get; set; } = "";
}

/// <summary>Who won a recorded Battle (ArenaDesktop v0.2.0, Battle Lab).</summary>
public enum BattleOutcome
{
    AWins,
    BWins,
    Tie,
}

/// <summary>The plain words for an outcome — the journal chips and tests share these.</summary>
public static class BattleOutcomeText
{
    public static string Label(BattleOutcome outcome) => outcome switch
    {
        BattleOutcome.AWins => "A won",
        BattleOutcome.BWins => "B won",
        _ => "Tie",
    };
}

/// <summary>
/// One journal entry (ArenaDesktop v0.2.0, Battle Lab). The prompt
/// NAME is snapshotted at log time, so renaming or deleting the
/// prompt later never rewrites history. Entries are recorded by hand:
/// the app never reads anything back from the Arena page.
/// </summary>
public sealed class BattleJournalEntry
{
    public string Id { get; set; } = "";
    public DateTimeOffset RecordedUtc { get; set; }
    public string PromptName { get; set; } = "";
    public BattleOutcome Outcome { get; set; } = BattleOutcome.Tie;
    public string Note { get; set; } = "";
}

/// <summary>Everything Battle Lab keeps: the prompt library, the run queue (prompt ids, in order), and the results journal (newest first).</summary>
public sealed class BattleLabData
{
    public List<BattlePrompt> Prompts { get; set; } = new();
    public List<string> Queue { get; set; } = new();
    public List<BattleJournalEntry> Journal { get; set; } = new();
}

/// <summary>
/// The <c>[blank]</c> placeholders in a prompt body (ArenaDesktop
/// v0.2.0, Battle Lab). A blank is square brackets around a short
/// label: <c>[topic]</c>. Extraction is ordered and de-duplicated
/// (case-insensitively; the first spelling wins). Filling replaces
/// every occurrence of a named blank; a blank with no supplied value
/// is left exactly as written, so a half-filled prompt is still
/// readable instead of silently losing its placeholders.
/// </summary>
public static class PromptBlanks
{
    private static readonly Regex BlankPattern = new(
        @"\[(?<name>[^\[\]\r\n]{1,60})\]",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> Extract(string? body)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(body))
            return found;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in BlankPattern.Matches(body))
        {
            var name = match.Groups["name"].Value.Trim();
            if (name.Length == 0)
                continue;
            if (seen.Add(name))
                found.Add(name);
        }
        return found;
    }

    public static string Fill(string? body, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(body) || values is null || values.Count == 0)
            return body ?? "";
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
            lookup[pair.Key.Trim()] = pair.Value;
        return BlankPattern.Replace(body, match =>
        {
            var name = match.Groups["name"].Value.Trim();
            return name.Length > 0 && lookup.TryGetValue(name, out var value)
                ? value
                : match.Value;
        });
    }
}

/// <summary>
/// Battle Lab's JSON store (ArenaDesktop v0.2.0): one file beside the
/// settings, written with the same atomic tmp-then-move pattern as
/// the settings and download history. A missing file is a first run
/// and loads the sample library; a corrupt file is preserved next to
/// the original as <c>.corrupt-*</c> (the SettingsStore contract)
/// and loads empty — the samples are NOT resurrected over the top of
/// a library the user once had.
/// </summary>
public static class BattleLabStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static BattleLabData Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return CreateSeeded();
            var data = JsonSerializer.Deserialize<BattleLabData>(File.ReadAllText(path), Options)
                       ?? new BattleLabData();
            return Sanitized(data);
        }
        catch (Exception)
        {
            try
            {
                var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(path, backup, overwrite: false);
            }
            catch
            {
                // Best effort only — returning an empty library is the contract.
            }
            return new BattleLabData();
        }
    }

    public static void Save(string path, BattleLabData data)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Sanitized(data), Options));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Drop anything a hand-edited file shouldn't be able to smuggle in: null rows, id-less prompts, queue ids naming no prompt, duplicate queue entries are kept (running a prompt twice is legitimate) but order is untouched.</summary>
    public static BattleLabData Sanitized(BattleLabData data)
    {
        data.Prompts ??= new List<BattlePrompt>();
        data.Prompts.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Id));
        foreach (var prompt in data.Prompts)
        {
            prompt.Name ??= "";
            prompt.Category ??= "";
            prompt.Body ??= "";
        }
        var ids = new HashSet<string>(data.Prompts.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        data.Queue ??= new List<string>();
        data.Queue.RemoveAll(id => string.IsNullOrWhiteSpace(id) || !ids.Contains(id));
        data.Journal ??= new List<BattleJournalEntry>();
        data.Journal.RemoveAll(e => e is null || string.IsNullOrWhiteSpace(e.PromptName));
        foreach (var entry in data.Journal)
            entry.Note ??= "";
        data.Journal = data.Journal.OrderByDescending(e => e.RecordedUtc).ToList();
        return data;
    }

    /// <summary>The first-run sample library: four prompts the user can edit or delete like any other.</summary>
    public static BattleLabData CreateSeeded() => new()
    {
        Prompts = new List<BattlePrompt>
        {
            new()
            {
                Id = "code-review-gauntlet",
                Name = "Code review gauntlet",
                Category = "Coding",
                Body = "Review a code change as a strict senior engineer. The change is written in [language] and touches [area]. "
                     + "List every problem you find — correctness first, then readability, then performance — ranked by severity, "
                     + "and finish with how you would fix the worst one.",
            },
            new()
            {
                Id = "math-word-problem-set",
                Name = "Math word-problem set",
                Category = "Reasoning",
                Body = "Write a word problem about [topic] at [difficulty] difficulty that needs two steps to solve. "
                     + "Make the numbers awkward on purpose — no round answers. Then solve it yourself, showing each step, "
                     + "and check the answer a second way.",
            },
            new()
            {
                Id = "story-opener-duel",
                Name = "Story opener duel",
                Category = "Writing",
                Body = "Write the opening paragraph of a [genre] story set in [setting]. The first sentence must contain a sound. "
                     + "End the paragraph on a question the reader needs answered.",
            },
            new()
            {
                Id = "explain-it-simply",
                Name = "Explain it simply",
                Category = "Everyday",
                Body = "Explain [concept] to a smart ten-year-old. Use one everyday analogy, no jargon, and finish with a "
                     + "one-sentence summary the child could repeat to someone else.",
            },
        },
        Queue = new List<string>(),
        Journal = new List<BattleJournalEntry>(),
    };
}

/// <summary>
/// The working Battle Lab library (ArenaDesktop v0.2.0): the data
/// plus every operation the window offers, persisting after each
/// change when opened from a file (autosave — there is no Save
/// button anywhere in Battle Lab). Open it with
/// <see cref="Open(string)"/>; construct it directly over a
/// <see cref="BattleLabData"/> for tests, where nothing is written.
/// </summary>
public sealed class BattleLabLibrary
{
    private readonly string? _path;

    public BattleLabLibrary(BattleLabData data, string? path = null)
    {
        Data = BattleLabStore.Sanitized(data);
        _path = path;
    }

    public BattleLabData Data { get; }

    /// <summary>
    /// Load from disk and bind autosave. A first run's samples are
    /// written out immediately, so "seeded" is a one-time event:
    /// deleting every sample afterwards sticks.
    /// </summary>
    public static BattleLabLibrary Open(string path)
    {
        var existed = File.Exists(path);
        var library = new BattleLabLibrary(BattleLabStore.Load(path), path);
        if (!existed)
            library.Persist();
        return library;
    }

    private void Persist()
    {
        if (_path is not null)
            BattleLabStore.Save(_path, Data);
    }

    // ─────────────────────────── library ───────────────────────────

    public IReadOnlyList<BattlePrompt> Prompts => Data.Prompts;

    public BattlePrompt? FindPrompt(string? id) =>
        id is null ? null : Data.Prompts.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public BattlePrompt AddPrompt(string name, string category, string body)
    {
        var prompt = new BattlePrompt
        {
            Id = UniqueId(name),
            Name = name,
            Category = category,
            Body = body,
        };
        Data.Prompts.Add(prompt);
        Persist();
        return prompt;
    }

    public bool UpdatePrompt(string id, string name, string category, string body)
    {
        var prompt = FindPrompt(id);
        if (prompt is null)
            return false;
        prompt.Name = name;
        prompt.Category = category;
        prompt.Body = body;
        Persist();
        return true;
    }

    /// <summary>Delete a prompt and any queue entries naming it. Journal entries keep their name snapshots and are untouched.</summary>
    public bool DeletePrompt(string id)
    {
        var removed = Data.Prompts.RemoveAll(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            return false;
        Data.Queue.RemoveAll(q => q.Equals(id, StringComparison.OrdinalIgnoreCase));
        Persist();
        return true;
    }

    /// <summary>Search name, category and body (case-insensitive). A blank query returns the whole library, in library order.</summary>
    public IReadOnlyList<BattlePrompt> SearchPrompts(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Data.Prompts.ToList();
        var term = query.Trim();
        return Data.Prompts.Where(p =>
            p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || p.Category.Contains(term, StringComparison.OrdinalIgnoreCase)
            || p.Body.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Every category in use, A–Z (blank categories read as "General").</summary>
    public IReadOnlyList<string> Categories() => Data.Prompts
        .Select(p => string.IsNullOrWhiteSpace(p.Category) ? "General" : p.Category.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private string UniqueId(string name)
    {
        var slug = new string((name ?? "").Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length == 0)
            slug = "prompt";
        var candidate = slug;
        var suffix = 2;
        while (FindPrompt(candidate) is not null)
            candidate = $"{slug}-{suffix++}";
        return candidate;
    }

    // ─────────────────────────── run queue ───────────────────────────

    /// <summary>The queue resolved to prompts, in run order. Ids naming a deleted prompt are already gone (the store sanitizes them away).</summary>
    public IReadOnlyList<BattlePrompt> QueuePrompts =>
        Data.Queue.Select(id => FindPrompt(id)).Where(p => p is not null).Cast<BattlePrompt>().ToList();

    /// <summary>The next prompt to run, or null when the queue is empty.</summary>
    public BattlePrompt? NextUp => QueuePrompts.FirstOrDefault();

    public bool Enqueue(string promptId)
    {
        if (FindPrompt(promptId) is null)
            return false;
        Data.Queue.Add(FindPrompt(promptId)!.Id);
        Persist();
        return true;
    }

    public bool RemoveFromQueueAt(int index)
    {
        if (index < 0 || index >= Data.Queue.Count)
            return false;
        Data.Queue.RemoveAt(index);
        Persist();
        return true;
    }

    /// <summary>Move one queue entry; <paramref name="delta"/> is -1 (up) or +1 (down). Moving past either end is a no-op that returns false.</summary>
    public bool MoveInQueue(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= Data.Queue.Count || target < 0 || target >= Data.Queue.Count)
            return false;
        (Data.Queue[index], Data.Queue[target]) = (Data.Queue[target], Data.Queue[index]);
        Persist();
        return true;
    }

    public void ClearQueue()
    {
        if (Data.Queue.Count == 0)
            return;
        Data.Queue.Clear();
        Persist();
    }

    // ─────────────────────────── journal ───────────────────────────

    /// <summary>Journal entries, newest first; a non-blank filter matches the recorded prompt name (contains, case-insensitive).</summary>
    public IReadOnlyList<BattleJournalEntry> JournalEntries(string? promptNameFilter = null)
    {
        IEnumerable<BattleJournalEntry> query = Data.Journal;
        if (!string.IsNullOrWhiteSpace(promptNameFilter))
        {
            var term = promptNameFilter.Trim();
            query = query.Where(e => e.PromptName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return query.OrderByDescending(e => e.RecordedUtc).ToList();
    }

    public BattleJournalEntry AddJournalEntry(string promptName, BattleOutcome outcome, string note, DateTimeOffset? recordedUtc = null)
    {
        var entry = new BattleJournalEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordedUtc = recordedUtc ?? DateTimeOffset.UtcNow,
            PromptName = promptName?.Trim() ?? "",
            Outcome = outcome,
            Note = note?.Trim() ?? "",
        };
        Data.Journal.Insert(0, entry);
        Data.Journal = Data.Journal.OrderByDescending(e => e.RecordedUtc).ToList();
        Persist();
        return entry;
    }

    public bool DeleteJournalEntry(string id)
    {
        var removed = Data.Journal.RemoveAll(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
            Persist();
        return removed;
    }
}
