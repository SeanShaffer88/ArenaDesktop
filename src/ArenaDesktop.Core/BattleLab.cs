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

/// <summary>
/// The journal scoreboard (ArenaDesktop v0.3.0): how many battles
/// were recorded and how they split between A, B and ties. Computed
/// over whatever set of entries the caller passes — the window
/// tallies the currently filtered view, so the line always describes
/// exactly what is on screen.
/// </summary>
public sealed record BattleJournalTally(int Total, int AWins, int BWins, int Ties)
{
    public static BattleJournalTally Of(IEnumerable<BattleJournalEntry> entries)
    {
        var total = 0;
        var aWins = 0;
        var bWins = 0;
        var ties = 0;
        foreach (var entry in entries)
        {
            total++;
            switch (entry.Outcome)
            {
                case BattleOutcome.AWins: aWins++; break;
                case BattleOutcome.BWins: bWins++; break;
                default: ties++; break;
            }
        }
        return new BattleJournalTally(total, aWins, bWins, ties);
    }

    /// <summary>The scoreboard as one line: "12 battles · A won 5 · B won 4 · Ties 3".</summary>
    public string ScoreboardLine =>
        $"{Total} {(Total == 1 ? "battle" : "battles")} · A won {AWins} · B won {BWins} · Ties {Ties}";
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
    /// <summary>
    /// The library size cap the merge operations respect (ArenaDesktop
    /// v0.3.0): duplicating and importing stop here, so a paste of a
    /// huge export file can't grow the library without bound.
    /// </summary>
    public const int MaxPrompts = 200;

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

    /// <summary>
    /// Copy a prompt (ArenaDesktop v0.3.0): same category and body,
    /// a new id, and the name "X copy" — or "X copy 2", "X copy 3", …
    /// when that name is already taken. Returns null when the prompt
    /// doesn't exist or the library is at <see cref="MaxPrompts"/>.
    /// </summary>
    public BattlePrompt? DuplicatePrompt(string id)
    {
        var original = FindPrompt(id);
        if (original is null || Data.Prompts.Count >= MaxPrompts)
            return null;
        var baseName = $"{original.Name} copy";
        var name = baseName;
        var suffix = 2;
        while (Data.Prompts.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {suffix++}";
        var copy = new BattlePrompt
        {
            Id = UniqueId(name),
            Name = name,
            Category = original.Category,
            Body = original.Body,
        };
        Data.Prompts.Add(copy);
        Persist();
        return copy;
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

    private string UniqueId(string name, string? alsoTaken = null)
    {
        var slug = new string((name ?? "").Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length == 0)
            slug = "prompt";
        var candidate = slug;
        var suffix = 2;
        while (FindPrompt(candidate) is not null
               || (alsoTaken is not null && candidate.Equals(alsoTaken, StringComparison.OrdinalIgnoreCase)))
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

    /// <summary>
    /// Line up a whole category (ArenaDesktop v0.3.0): every prompt
    /// in the category, in library order, that is not already queued
    /// (a prompt queued twice by hand stays twice — only the prompts
    /// missing entirely are added). A blank category reads as
    /// "General", like everywhere else in Battle Lab. Returns how
    /// many prompts were added.
    /// </summary>
    public int EnqueueCategory(string? category)
    {
        var wanted = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
        var queued = new HashSet<string>(Data.Queue, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var prompt in Data.Prompts)
        {
            var own = string.IsNullOrWhiteSpace(prompt.Category) ? "General" : prompt.Category.Trim();
            if (!string.Equals(own, wanted, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!queued.Add(prompt.Id))
                continue;
            Data.Queue.Add(prompt.Id);
            added++;
        }
        if (added > 0)
            Persist();
        return added;
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

    /// <summary>The scoreboard over the journal, or over the filtered view when a filter is given.</summary>
    public BattleJournalTally Tally(string? promptNameFilter = null) =>
        BattleJournalTally.Of(JournalEntries(promptNameFilter));

    // ────────────────────── library transfer ──────────────────────

    /// <summary>The whole prompt library as a portable JSON document (see <see cref="BattleLabTransfer"/>). Queue and journal stay behind — they are this PC's run state, not the library.</summary>
    public string ExportPromptsJson() => BattleLabTransfer.ExportJson(Data.Prompts);

    /// <summary>
    /// Merge a library export into this library (ArenaDesktop
    /// v0.3.0). Incoming prompts are sanitized, given NEW ids (the
    /// file's ids never enter this library), and skipped when one
    /// with the same name (case-insensitive) and the same body is
    /// already here, or when the library reaches
    /// <see cref="MaxPrompts"/>. A malformed file imports nothing
    /// and reports <see cref="BattleLabImportResult.Ok"/> = false.
    /// </summary>
    public BattleLabImportResult ImportPrompts(string? json)
    {
        if (!BattleLabTransfer.TryRead(json, out var incoming))
            return new BattleLabImportResult(false, 0, 0);
        var added = 0;
        var skipped = 0;
        foreach (var candidate in incoming)
        {
            var isDuplicate = Data.Prompts.Any(p =>
                string.Equals(p.Name.Trim(), candidate.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Body, candidate.Body, StringComparison.Ordinal));
            if (isDuplicate || Data.Prompts.Count >= MaxPrompts)
            {
                skipped++;
                continue;
            }
            Data.Prompts.Add(new BattlePrompt
            {
                // alsoTaken: the id recorded in the file — an imported
                // prompt always lands under a fresh id of its own.
                Id = UniqueId(candidate.Name, candidate.Id),
                Name = candidate.Name,
                Category = candidate.Category,
                Body = candidate.Body,
            });
            added++;
        }
        if (added > 0)
            Persist();
        return new BattleLabImportResult(true, added, skipped);
    }
}

/// <summary>The outcome of a library import: whether the file read at all, and how its prompts split between added and skipped (duplicates or the size cap).</summary>
public sealed record BattleLabImportResult(bool Ok, int Added, int Skipped);

/// <summary>
/// The portable Battle Lab library format (ArenaDesktop v0.3.0):
/// only the prompts travel, as
/// <c>{ "format": "arenadesktop-battle-lab", "version": 1, "prompts": [ … ] }</c>.
/// The format marker and version are checked on read, so a settings
/// export or a random JSON file is refused rather than half-merged.
/// Rows are cleaned the way the store cleans: names and categories
/// trimmed, null text read as empty, and a row with no usable name
/// dropped.
/// </summary>
public static class BattleLabTransfer
{
    public const string FormatMarker = "arenadesktop-battle-lab";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class TransferFile
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        // Null when the file has no prompts array at all — that is a
        // malformed file, not an empty library.
        public List<TransferPrompt>? Prompts { get; set; }
    }

    private sealed class TransferPrompt
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Body { get; set; } = "";
    }

    public static string ExportJson(IEnumerable<BattlePrompt> prompts)
    {
        var file = new TransferFile
        {
            Format = FormatMarker,
            Version = FormatVersion,
            Prompts = prompts.Select(p => new TransferPrompt
            {
                Id = p.Id,
                Name = p.Name,
                Category = p.Category,
                Body = p.Body,
            }).ToList(),
        };
        return JsonSerializer.Serialize(file, Options);
    }

    /// <summary>Parse a library export. False — with an empty list — when the text is not a Battle Lab library file at all.</summary>
    public static bool TryRead(string? json, out IReadOnlyList<BattlePrompt> prompts)
    {
        prompts = Array.Empty<BattlePrompt>();
        if (string.IsNullOrWhiteSpace(json))
            return false;
        TransferFile? file;
        try
        {
            file = JsonSerializer.Deserialize<TransferFile>(json, Options);
        }
        catch (Exception)
        {
            return false;
        }
        if (file is null
            || !string.Equals(file.Format, FormatMarker, StringComparison.Ordinal)
            || file.Version != FormatVersion
            || file.Prompts is null)
            return false;
        var cleaned = new List<BattlePrompt>();
        foreach (var row in file.Prompts!)
        {
            if (row is null)
                continue;
            var name = (row.Name ?? "").Trim();
            if (name.Length == 0)
                continue;
            cleaned.Add(new BattlePrompt
            {
                Id = (row.Id ?? "").Trim(),
                Name = name,
                Category = (row.Category ?? "").Trim(),
                Body = row.Body ?? "",
            });
        }
        prompts = cleaned;
        return true;
    }
}

/// <summary>
/// The journal as a Markdown document (ArenaDesktop v0.3.0): the
/// scoreboard line, then one table row per entry in the order given
/// (the window passes its currently filtered, newest-first view).
/// Pipes and newlines in names and notes are flattened so a note can
/// never break the table.
/// </summary>
public static class BattleJournalMarkdown
{
    public static string RenderJournalMarkdown(IEnumerable<BattleJournalEntry> entries, BattleJournalTally tally)
    {
        var list = entries as IReadOnlyList<BattleJournalEntry> ?? entries.ToList();
        var lines = new List<string>
        {
            "# Battle Lab journal",
            "",
            tally.ScoreboardLine,
            "",
        };
        if (list.Count == 0)
        {
            lines.Add("No battles recorded.");
        }
        else
        {
            lines.Add("| Date | Prompt | Result | Note |");
            lines.Add("| --- | --- | --- | --- |");
            foreach (var entry in list)
            {
                var date = entry.RecordedUtc.ToLocalTime().ToString("yyyy-MM-dd");
                lines.Add($"| {date} | {Cell(entry.PromptName)} | {BattleOutcomeText.Label(entry.Outcome)} | {Cell(entry.Note)} |");
            }
        }
        return string.Join("\n", lines).TrimEnd() + "\n";
    }

    private static string Cell(string? text) =>
        (text ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
}
