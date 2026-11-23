using System.Globalization;
using System.Text.Json;

namespace ArenaDesktop.Core;

// ───────────────────────── the prompt pad (v0.9.0) ─────────────────────────

/// <summary>
/// One prompt the pad remembers (v0.9.0): the text exactly as it was
/// copied, and when. The pad keeps a handful of these so a prompt
/// written once can be handed to Arena a second time without retyping
/// it, which is the whole point of a scratchpad.
/// </summary>
public sealed class PromptPadEntry
{
    /// <summary>The prompt text, as the user left it (trimmed at the ends, otherwise untouched).</summary>
    public string Text { get; set; } = "";

    /// <summary>When this prompt was last copied from the pad (local clock, stored as UTC).</summary>
    public DateTimeOffset UsedUtc { get; set; }
}

/// <summary>
/// The prompt pad's file (v0.9.0): the few prompts it remembers and the
/// page "Copy &amp; open" goes to. It holds prompts, not conversations —
/// nothing is read from Arena, and the pad never touches the page.
/// </summary>
public sealed class PromptPadData
{
    /// <summary>Newest first, at most <see cref="PromptPad.MaxRecents"/> entries.</summary>
    public List<PromptPadEntry> Recents { get; set; } = new();

    /// <summary>The mode "Copy &amp; open" navigates to, as an Arena mode id; Battle when nothing usable is stored.</summary>
    public string ModeId { get; set; } = ArenaModes.BattleId;

    /// <summary>
    /// What was in the box when the pad was last closed (v0.10.0). The
    /// pad used to lose unsent text, which is a poor deal for the one
    /// surface in the app that exists to hold a half-written thought.
    /// Cleared when the prompt it describes is copied, so the pad never
    /// reopens with the same text twice. Plain text on disk, exactly like
    /// the remembered prompts — see KNOWN_LIMITATIONS.md.
    /// </summary>
    public string Draft { get; set; } = "";
}

/// <summary>
/// The prompt pad's rules, kept pure (v0.9.0). The pad is the smallest
/// surface in the app: a box to write a prompt in, a page to send it to,
/// and a short list of what you sent before. Everything about it that
/// could be got wrong — what counts as the same prompt, how many are
/// kept, what a blank box does, what the status line says — is decided
/// here so the window has no opinion of its own.
///
/// The pad never injects anything into Arena and never reads the page.
/// "Copy &amp; open" puts the finished text on the clipboard and
/// navigates the main window; pasting is the user's own Ctrl+V, exactly
/// as with Battle Lab's copy button (see DECISIONS.md).
/// </summary>
public static class PromptPad
{
    /// <summary>How many prompts the pad remembers. Small on purpose: this is a scratchpad, not a library — Battle Lab is the library.</summary>
    public const int MaxRecents = 10;

    /// <summary>How much of a remembered prompt the list shows before trimming with an ellipsis.</summary>
    public const int PreviewLength = 90;

    /// <summary>True when there is something to copy: whitespace alone is not a prompt.</summary>
    public static bool IsCopyable(string? text) => !string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// The comparison form of a prompt: runs of whitespace collapsed,
    /// ends trimmed, case folded. Two prompts that differ only in
    /// spacing or capitalization are the same prompt to the pad, so
    /// copying one again moves it to the top instead of storing a twin.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.ToLowerInvariant();
    }

    /// <summary>
    /// Record a prompt the user just copied: trimmed, moved to the top
    /// (an existing identical prompt is replaced, never duplicated), and
    /// the list is capped. A blank prompt is not recorded and changes
    /// nothing. Mutates and returns <paramref name="data"/> so callers
    /// can save in one line.
    /// </summary>
    public static PromptPadData Remember(PromptPadData data, string? text, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!IsCopyable(text))
            return data;
        var trimmed = text!.Trim();
        var key = Normalize(trimmed);
        data.Recents ??= new List<PromptPadEntry>();
        data.Recents.RemoveAll(e => e is null || Normalize(e.Text) == key);
        data.Recents.Insert(0, new PromptPadEntry { Text = trimmed, UsedUtc = nowUtc });
        if (data.Recents.Count > MaxRecents)
            data.Recents.RemoveRange(MaxRecents, data.Recents.Count - MaxRecents);
        return data;
    }

    /// <summary>
    /// Forget one remembered prompt (same prompt test as
    /// <see cref="Remember"/>). Returns whether anything was removed, so
    /// the window can say "removed" rather than redrawing silently.
    /// </summary>
    public static bool Forget(PromptPadData data, string? text)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Recents is null || !IsCopyable(text))
            return false;
        var key = Normalize(text);
        return data.Recents.RemoveAll(e => e is null || Normalize(e.Text) == key) > 0;
    }

    /// <summary>
    /// Keep what is in the box for the next time the pad opens
    /// (v0.10.0). Whitespace-only text is stored as no draft at all: an
    /// empty box that happens to hold a newline should reopen empty.
    /// </summary>
    public static PromptPadData SetDraft(PromptPadData data, string? text)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Draft = IsCopyable(text) ? text!.Trim() : "";
        return data;
    }

    /// <summary>The draft to put back in the box, never null.</summary>
    public static string Draft(PromptPadData data) => data?.Draft ?? "";

    /// <summary>Forget every remembered prompt. The target page is left alone — clearing the list is not a settings reset.</summary>
    public static PromptPadData Clear(PromptPadData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Recents = new List<PromptPadEntry>();
        return data;
    }

    /// <summary>
    /// Point the pad at a page. Only a registered mode id is accepted
    /// (everything else — an empty picker, a hand-edited file — leaves
    /// the current page alone), and the stored id is always a canonical
    /// one, so the picker can select it again on the next visit.
    /// </summary>
    public static PromptPadData WithMode(PromptPadData data, string? modeId)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (ArenaModes.TryGet(modeId, out var mode))
            data.ModeId = mode.Id;
        return data;
    }

    /// <summary>The page the pad sends prompts to: the stored mode when it still exists, Battle otherwise.</summary>
    public static ArenaMode TargetMode(PromptPadData data) =>
        ArenaModes.TryGet(data?.ModeId, out var mode) ? mode : ArenaModes.All[0];

    /// <summary>
    /// Repair anything a hand-edited or half-written file shouldn't be
    /// able to smuggle in: null rows and blank prompts dropped, the same
    /// prompt kept once (newest wins), order newest first, the cap
    /// applied, and an unknown mode id read as Battle.
    /// </summary>
    public static PromptPadData Sanitized(PromptPadData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Recents ??= new List<PromptPadEntry>();
        // Newest first before de-duplication, so the surviving copy of a
        // repeated prompt is the most recent one — its timestamp is the
        // one that describes when the user last used it.
        data.Recents = data.Recents
            .Where(e => e is not null && IsCopyable(e.Text))
            .OrderByDescending(e => e.UsedUtc)
            .ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        data.Recents = data.Recents
            .Where(e => seen.Add(Normalize(e.Text)))
            .Take(MaxRecents)
            .ToList();
        foreach (var entry in data.Recents)
            entry.Text = entry.Text.Trim();
        data.Draft = (data.Draft ?? "").Trim();
        data.ModeId = ArenaModes.TryGet(data.ModeId, out var mode) ? mode.Id : ArenaModes.BattleId;
        return data;
    }

    /// <summary>"412 characters" / "1,204 characters" — the exact size of what just went to the clipboard.</summary>
    public static string CharacterCount(int characters)
    {
        var size = characters.ToString("N0", CultureInfo.InvariantCulture);
        var noun = characters == 1 ? "character" : "characters";
        return size + " " + noun;
    }

    /// <summary>
    /// The status line after a copy: how much text went to the clipboard
    /// and which page is waiting for the paste. Kept under the 140
    /// characters the announcement filter allows, and it never claims the
    /// paste happened — it hasn't.
    /// </summary>
    public static string CopyStatus(string? text, ArenaMode mode) =>
        $"Copied {CharacterCount(text?.Length ?? 0)} · {mode.Label} is ready — paste with Ctrl+V";

    /// <summary>
    /// The status line when nothing was copied, so a mistaken Enter reads
    /// as a refusal rather than as silence.
    /// </summary>
    public const string EmptyStatus = "The prompt pad is empty — type or paste a prompt first";

    /// <summary>
    /// A one-line preview of a remembered prompt: whitespace collapsed
    /// (a list row is one line), trimmed to
    /// <see cref="PreviewLength"/> characters with an ellipsis. Never
    /// returns null; an empty prompt reads as an em dash.
    /// </summary>
    public static string Preview(string? text, int maxLength = PreviewLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "—";
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (maxLength <= 0)
            return "";
        return collapsed.Length <= maxLength
            ? collapsed
            : collapsed[..maxLength].TrimEnd() + "…";
    }

    /// <summary>
    /// The list's own heading: the count, so the pad says how much it is
    /// keeping — and that it is keeping it on this PC.
    /// </summary>
    public static string RecentsSummary(int count) =>
        count switch
        {
            0 => "No remembered prompts yet — the next one you copy is kept here, on this PC.",
            1 => "1 remembered prompt, kept on this PC. Copy it again, or remove it.",
            _ => $"{count} remembered prompts, kept on this PC. Copy one again, or remove it.",
        };
}

/// <summary>
/// The prompt pad's file (v0.9.0), following the repo's store patterns:
/// atomic replace, a corrupt file preserved beside it as
/// <c>prompt-pad.json.corrupt-&lt;stamp&gt;</c> before defaults are
/// loaded, and sanitizing on both load and save so neither a hand-edited
/// file nor a future field can put the window in a state its rules
/// don't allow.
/// </summary>
public static class PromptPadStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static PromptPadData Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new PromptPadData();
            var data = JsonSerializer.Deserialize<PromptPadData>(File.ReadAllText(path), Options)
                       ?? new PromptPadData();
            return PromptPad.Sanitized(data);
        }
        catch (Exception)
        {
            // Returning an empty pad is the contract; the unreadable file
            // is kept beside it so a prompt the user cared about is still
            // on disk to recover from.
            CorruptFile.Preserve(path, DateTimeOffset.Now);
            return new PromptPadData();
        }
    }

    public static void Save(string path, PromptPadData data) =>
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(PromptPad.Sanitized(data), Options));
}

// ─────────────────── keeping a pad prompt in Battle Lab (v0.12.0) ───────────────────

/// <summary>What happened when the prompt pad handed a prompt to Battle Lab's library (v0.12.0).</summary>
public enum PadKeepOutcome
{
    /// <summary>It is in the library now.</summary>
    Kept,

    /// <summary>The same text was already there, so nothing was added.</summary>
    AlreadyThere,

    /// <summary>The box held nothing worth keeping.</summary>
    Empty,

    /// <summary>The library is at <see cref="BattleLabLibrary.MaxPrompts"/> and nothing was added.</summary>
    LibraryFull,
}

/// <summary>
/// The result of one handoff (v0.12.0): what happened, the prompt concerned
/// (the new one, or the existing copy), and the line the pad shows. The
/// wording lives here so it is testable and so the pad and any future
/// surface say the same thing.
/// </summary>
public sealed record PadKeepResult(PadKeepOutcome Outcome, BattlePrompt? Prompt, string Status);

/// <summary>
/// Handing a prompt from the pad to Battle Lab's library (v0.12.0).
///
/// The pad is deliberately not a library — that is the whole reason it
/// exists — but "actually, keep this one" is a thought you have while
/// writing, and before now it meant copy, open the Lab, New prompt, paste.
/// This is that sequence as one call.
///
/// The rules are the library's, not the pad's: an exact duplicate (the same
/// text, surrounding whitespace ignored) is refused rather than added twice,
/// the library's size cap is respected, and the prompt lands in its own
/// category so a handoff is findable later without polluting "General".
/// </summary>
public static class PadHandoff
{
    /// <summary>The category every handed-off prompt lands in.</summary>
    public const string Category = "Prompt pad";

    /// <summary>How long a derived name may be before it is cut short.</summary>
    public const int MaxNameLength = 60;

    /// <summary>The name a handed-off prompt gets: its first line, trimmed, cut to <see cref="MaxNameLength"/>.</summary>
    public static string NameFor(string? text)
    {
        var firstLine = (text ?? "")
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
        if (firstLine is null)
            return "Prompt from the pad";
        return firstLine.Length <= MaxNameLength
            ? firstLine
            : firstLine[..(MaxNameLength - 1)].TrimEnd() + "…";
    }

    /// <summary>Hand <paramref name="text"/> to <paramref name="library"/>. Idempotent: keeping the same prompt twice adds it once.</summary>
    public static PadKeepResult Keep(BattleLabLibrary library, string? text)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (!PromptPad.IsCopyable(text))
            return new PadKeepResult(PadKeepOutcome.Empty, null,
                "Nothing to keep — the box is empty.");

        var body = text!.Trim();
        var existing = library.Prompts.FirstOrDefault(p =>
            string.Equals((p.Body ?? "").Trim(), body, StringComparison.Ordinal));
        if (existing is not null)
            return new PadKeepResult(PadKeepOutcome.AlreadyThere, existing,
                $"Already in Battle Lab · {existing.Name}");

        if (library.Prompts.Count >= BattleLabLibrary.MaxPrompts)
            return new PadKeepResult(PadKeepOutcome.LibraryFull, null,
                $"Battle Lab is full ({BattleLabLibrary.MaxPrompts} prompts) — delete one before keeping this.");

        var name = UniqueName(library, NameFor(body));
        var prompt = library.AddPrompt(name, Category, body);
        return new PadKeepResult(PadKeepOutcome.Kept, prompt, $"Saved to Battle Lab · {name}");
    }

    /// <summary>"X", or "X 2", "X 3", … — the first name no prompt in the library already has (case-insensitive).</summary>
    public static string UniqueName(BattleLabLibrary library, string name)
    {
        var taken = library.Prompts
            .Select(p => (p.Name ?? "").Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
            return name;
        for (var n = 2; ; n++)
        {
            var candidate = $"{name} {n}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }
}
