using System.Text.Json;

namespace ArenaDesktop.Core;

/// <summary>What the user marked for one field-pass check (ArenaDesktop v0.2.0, field pass).</summary>
public enum FieldPassVerdict
{
    /// <summary>Not marked yet.</summary>
    None,

    /// <summary>It worked the way the check describes.</summary>
    Pass,

    /// <summary>Something was wrong — the note field says what.</summary>
    Problem,

    /// <summary>The check doesn't apply (e.g. the app lock, when the user doesn't use one) or is left for later.</summary>
    Skipped,
}

/// <summary>The plain words for a verdict — the window and the summary share these.</summary>
public static class FieldPassVerdictText
{
    public static string Label(FieldPassVerdict verdict) => verdict switch
    {
        FieldPassVerdict.Pass => "Passed",
        FieldPassVerdict.Problem => "Problem",
        FieldPassVerdict.Skipped => "Skipped",
        _ => "Not done",
    };
}

/// <summary>Which existing window a check's "Open feature" button opens (ArenaDesktop v0.2.0, field pass). None = the check is done by hand.</summary>
public enum FieldPassTarget
{
    None,
    Settings,
    BattleLab,
    Downloads,
}

/// <summary>The button label for a target — "Open Settings", "Open Battle Lab", "Open Downloads".</summary>
public static class FieldPassTargetText
{
    public static string OpenLabel(FieldPassTarget target) => target switch
    {
        FieldPassTarget.Settings => "Open Settings",
        FieldPassTarget.BattleLab => "Open Battle Lab",
        FieldPassTarget.Downloads => "Open Downloads",
        _ => "",
    };
}

/// <summary>
/// One field-pass check (ArenaDesktop v0.2.0). The Instructions say
/// exactly what to do; the SuccessLine says what working looks like,
/// in one line. Every check is a SHELL check the user performs by
/// hand — the app never injects script into or reads from the
/// arena.ai page, so the only evidence a check ever produces is the
/// verdict the user records.
/// </summary>
public sealed record FieldPassStep(
    string Id, string Title, string Instructions, string SuccessLine, FieldPassTarget Target);

/// <summary>The nine checks, in the order the window walks them.</summary>
public static class FieldPassCatalog
{
    public static readonly IReadOnlyList<FieldPassStep> Steps = new[]
    {
        new FieldPassStep("layouts",
            "Layouts",
            "Open Settings → Appearance & toolbar (or use the ⋯ menu) and pick each layout in turn: Standard, then Compact Focus, then Left Rail. Watch the window rearrange each time. Then close ArenaDesktop, reopen it, and check the layout you left selected is still in use.",
            "Each layout rearranges the window, and the layout you saved is still selected after a restart.",
            FieldPassTarget.Settings),
        new FieldPassStep("battle-lab",
            "Battle Lab",
            "Open the Battle Lab and pick one of the sample prompts. If it has blanks like [topic], fill them in when asked, then choose \"Copy prompt & open Battle\".",
            "The main window comes forward on the Battle page, and the finished prompt is on the clipboard, ready to paste (Ctrl+V) into the prompt box.",
            FieldPassTarget.BattleLab),
        new FieldPassStep("arena-modes",
            "Arena modes",
            "Press Ctrl+1, Ctrl+2, Ctrl+3 and Ctrl+4 in turn, then open the ⋯ menu's Arena modes and pick one. Battle, Direct Chat, Side by Side and the Leaderboard should each load.",
            "Every mode you jump to loads its page — no blank page, no error.",
            FieldPassTarget.None),
        new FieldPassStep("battle-journal",
            "Battle + journal",
            "On the Battle page, send a prompt and vote for the better answer. Then open the Battle Lab, choose \"Log a result\", record the winner (A won / B won / Tie), and add a note if you like.",
            "The journal shows the result with today's date.",
            FieldPassTarget.BattleLab),
        new FieldPassStep("downloads",
            "Downloads",
            "Download a generated image or file from Arena. Watch the Downloads window while it saves, then open the file from the history list afterwards.",
            "The download shows live progress, finishes, and the file opens from history.",
            FieldPassTarget.Downloads),
        new FieldPassStep("tray-hotkey",
            "Tray + hotkey",
            "Close the main window — ArenaDesktop should minimise to the tray, not quit. Bring it back with the global hotkey (Ctrl+Alt+A, unless you changed it in Settings). While it's in the tray, right-click the tray icon and try the Arena modes submenu.",
            "Closing hides the app to the tray, the hotkey brings it back, and the tray's Arena modes submenu works.",
            FieldPassTarget.None),
        new FieldPassStep("app-lock",
            "App lock",
            "If you use the app lock: choose Lock now from the ⋯ menu, then unlock with your PIN or password. If you don't use a lock, Skip is the right answer for this check.",
            "The app locks, and your PIN or password unlocks it.",
            FieldPassTarget.None),
        new FieldPassStep("profiles",
            "Profile switching",
            "Switch to another profile from the ⋯ menu or the tray menu, use Arena for a moment, then switch back.",
            "Each profile keeps its own Arena sign-in — switching never mixes them up.",
            FieldPassTarget.None),
        new FieldPassStep("update-check",
            "Update check",
            "Open Settings and choose Check for updates.",
            "It tells you honestly — up to date, or a newer version to download — and it never installs anything without asking you first.",
            FieldPassTarget.Settings),
    };

    public static FieldPassStep? ById(string? id) =>
        id is null ? null : Steps.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One recorded verdict: which step, what the user marked, their note, and when (local history only — notes stay on this PC).</summary>
public sealed class FieldPassRecord
{
    public string StepId { get; set; } = "";
    public FieldPassVerdict Verdict { get; set; } = FieldPassVerdict.None;
    public string Note { get; set; } = "";
    public DateTimeOffset? RecordedUtc { get; set; }
}

/// <summary>Everything the field pass keeps: one record per marked check, in catalogue order once sanitized.</summary>
public sealed class FieldPassData
{
    public List<FieldPassRecord> Records { get; set; } = new();
}

/// <summary>The pass's counts: how many checks are passed, marked as a problem, skipped, and still to do.</summary>
public sealed record FieldPassCounts(int Passed, int Problems, int Skipped, int Remaining)
{
    /// <summary>Checks with any verdict at all (passed + problems + skipped).</summary>
    public int Done => Passed + Problems + Skipped;
}

/// <summary>
/// The field pass's JSON store (ArenaDesktop v0.2.0): one file beside
/// the settings, written with the same atomic tmp-then-move pattern
/// as the settings and Battle Lab. A missing file is a fresh pass; a
/// corrupt file is preserved next to the original as
/// <c>.corrupt-*</c> (the SettingsStore contract) and a fresh pass
/// loads — a half-finished pass file is never allowed to take the
/// window down.
/// </summary>
public static class FieldPassStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static FieldPassData Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new FieldPassData();
            var data = JsonSerializer.Deserialize<FieldPassData>(File.ReadAllText(path), Options)
                       ?? new FieldPassData();
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
                // Best effort only — returning a fresh pass is the contract.
            }
            return new FieldPassData();
        }
    }

    public static void Save(string path, FieldPassData data)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Sanitized(data), Options));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Keep only records naming a real catalogue step, at most one per step, with a defined verdict; order follows the catalogue, so the file reads in walk order.</summary>
    public static FieldPassData Sanitized(FieldPassData data)
    {
        data.Records ??= new List<FieldPassRecord>();
        var byStep = new Dictionary<string, FieldPassRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in data.Records)
        {
            if (record is null || string.IsNullOrWhiteSpace(record.StepId))
                continue;
            var step = FieldPassCatalog.ById(record.StepId.Trim());
            if (step is null || byStep.ContainsKey(step.Id))
                continue;
            record.StepId = step.Id;
            if (!Enum.IsDefined(record.Verdict))
                record.Verdict = FieldPassVerdict.None;
            record.Note ??= "";
            byStep[step.Id] = record;
        }
        data.Records = FieldPassCatalog.Steps
            .Where(s => byStep.ContainsKey(s.Id))
            .Select(s => byStep[s.Id])
            .ToList();
        return data;
    }
}

/// <summary>
/// A running field pass (ArenaDesktop v0.2.0): the records plus every
/// operation the window offers, persisting after each change when
/// opened from a file (autosave — there is no Save button). Open it
/// with <see cref="Open(string)"/>; construct it directly over a
/// <see cref="FieldPassData"/> for tests, where nothing is written.
/// </summary>
public sealed class FieldPassSession
{
    private readonly string? _path;

    public FieldPassSession(FieldPassData data, string? path = null)
    {
        Data = FieldPassStore.Sanitized(data);
        _path = path;
    }

    public FieldPassData Data { get; }

    /// <summary>Load from disk and bind autosave.</summary>
    public static FieldPassSession Open(string path) =>
        new(FieldPassStore.Load(path), path);

    private void Persist()
    {
        if (_path is not null)
            FieldPassStore.Save(_path, Data);
    }

    /// <summary>The record for a step, or null when it hasn't been touched.</summary>
    public FieldPassRecord? RecordFor(string stepId) =>
        Data.Records.FirstOrDefault(r => r.StepId.Equals(stepId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The step's verdict (None when unmarked or the id is unknown).</summary>
    public FieldPassVerdict VerdictFor(string stepId) =>
        RecordFor(stepId)?.Verdict ?? FieldPassVerdict.None;

    /// <summary>
    /// Mark a step. The note is kept (trimmed) with any verdict;
    /// marking <see cref="FieldPassVerdict.None"/> clears the verdict
    /// and its timestamp but keeps the note. Unknown step ids are
    /// ignored and return false.
    /// </summary>
    public bool SetVerdict(string stepId, FieldPassVerdict verdict, string? note, DateTimeOffset? recordedUtc = null)
    {
        var step = FieldPassCatalog.ById(stepId);
        if (step is null)
            return false;
        var record = RecordFor(step.Id);
        if (record is null)
        {
            record = new FieldPassRecord { StepId = step.Id };
            Data.Records.Add(record);
        }
        record.Verdict = verdict;
        record.Note = (note ?? "").Trim();
        record.RecordedUtc = verdict == FieldPassVerdict.None
            ? null
            : recordedUtc ?? DateTimeOffset.UtcNow;
        Data.Records = FieldPassStore.Sanitized(Data).Records;
        Persist();
        return true;
    }

    /// <summary>Update a step's note without touching its verdict (used as the user moves between steps, so typed notes survive navigation).</summary>
    public bool SetNote(string stepId, string? note)
    {
        var step = FieldPassCatalog.ById(stepId);
        if (step is null)
            return false;
        var record = RecordFor(step.Id);
        var trimmed = (note ?? "").Trim();
        if (record is null)
        {
            if (trimmed.Length == 0)
                return true;
            record = new FieldPassRecord { StepId = step.Id };
            Data.Records.Add(record);
        }
        if (record.Note == trimmed)
            return true;
        record.Note = trimmed;
        Data.Records = FieldPassStore.Sanitized(Data).Records;
        Persist();
        return true;
    }

    /// <summary>Clear every verdict and note — a fresh pass. The confirmation lives in the shell.</summary>
    public void Reset()
    {
        Data.Records.Clear();
        Persist();
    }

    /// <summary>Passed / problem / skipped / still-to-do counts over the catalogue.</summary>
    public FieldPassCounts Counts
    {
        get
        {
            var passed = 0;
            var problems = 0;
            var skipped = 0;
            foreach (var step in FieldPassCatalog.Steps)
            {
                switch (VerdictFor(step.Id))
                {
                    case FieldPassVerdict.Pass: passed++; break;
                    case FieldPassVerdict.Problem: problems++; break;
                    case FieldPassVerdict.Skipped: skipped++; break;
                }
            }
            return new FieldPassCounts(passed, problems, skipped,
                FieldPassCatalog.Steps.Count - passed - problems - skipped);
        }
    }

    /// <summary>The index (into the catalogue) of the first check with no verdict — where a reopened pass resumes. Equals the step count when every check is marked.</summary>
    public int FirstUnfinishedIndex
    {
        get
        {
            for (var i = 0; i < FieldPassCatalog.Steps.Count; i++)
                if (VerdictFor(FieldPassCatalog.Steps[i].Id) == FieldPassVerdict.None)
                    return i;
            return FieldPassCatalog.Steps.Count;
        }
    }
}

/// <summary>
/// The field pass's text surfaces (ArenaDesktop v0.2.0): the header
/// progress line the window shows, and the plain-markdown summary
/// "Copy summary" puts on the clipboard for a bug report. The summary
/// carries versions, dates, verdicts and the user's own notes —
/// nothing from the Arena page, ever.
/// </summary>
public static class FieldPassSummary
{
    /// <summary>The header line: "Step 3 of 9 · 2 passed · 0 with a problem · 0 skipped · 7 to go".</summary>
    public static string ProgressText(FieldPassSession session, int currentStepNumber)
    {
        var counts = session.Counts;
        var total = FieldPassCatalog.Steps.Count;
        var step = Math.Clamp(currentStepNumber, 1, total);
        return $"Step {step} of {total} · {counts.Passed} passed · {counts.Problems} with a problem · " +
               $"{counts.Skipped} skipped · {counts.Remaining} to go";
    }

    /// <summary>The counts line on its own: "5 passed · 1 with a problem · 1 skipped · 2 not done".</summary>
    public static string CountsText(FieldPassCounts counts) =>
        $"{counts.Passed} passed · {counts.Problems} with a problem · {counts.Skipped} skipped · {counts.Remaining} not done";

    /// <summary>The full summary as plain markdown.</summary>
    public static string Render(FieldPassSession session, string appVersion, DateTimeOffset generated)
    {
        var counts = session.Counts;
        var lines = new List<string>
        {
            "# ArenaDesktop field pass",
            "",
            $"ArenaDesktop version: {appVersion}",
            $"Date: {generated:yyyy-MM-dd}",
            $"Result: {CountsText(counts)}",
            "",
            "## Checks",
            "",
        };
        for (var i = 0; i < FieldPassCatalog.Steps.Count; i++)
        {
            var step = FieldPassCatalog.Steps[i];
            var record = session.RecordFor(step.Id);
            var verdict = record?.Verdict ?? FieldPassVerdict.None;
            lines.Add($"### {i + 1}. {step.Title} — {FieldPassVerdictText.Label(verdict)}");
            lines.Add("");
            lines.Add($"Working looks like: {step.SuccessLine}");
            if (record is not null && !string.IsNullOrWhiteSpace(record.Note))
                lines.Add($"Note: {record.Note.Trim()}");
            if (record?.RecordedUtc is { } when)
                lines.Add($"Recorded: {when.ToLocalTime():yyyy-MM-dd HH:mm}");
            lines.Add("");
        }
        lines.Add("The field pass records what the user saw on this PC; the app does not verify the checks itself, and nothing in this summary comes from the Arena page.");
        return string.Join("\n", lines).TrimEnd() + "\n";
    }
}
