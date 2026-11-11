using System.IO;
using System.Windows;
using System.Windows.Media;
using ArenaDesktop.Core;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace ArenaDesktop;

/// <summary>
/// Battle Lab (v0.2.0): the prompt library, the run queue, and the
/// results journal in their own window. Everything is stored by
/// <see cref="BattleLabLibrary"/> in Core and saved as you go — there
/// is no Save button. The copy button fills the prompt's blanks,
/// puts the finished text on the clipboard, and brings the main
/// window forward on the page picked beside it (Battle, Direct Chat
/// or Side by Side — v0.3.0; Battle is the default); pasting is the
/// user's. Nothing is ever injected into or read from the Arena page,
/// and journal results are recorded by hand.
/// </summary>
public partial class BattleLabWindow : Window
{
    private readonly DiagnosticLog _log;
    private readonly BattleLabLibrary _library;
    private readonly Action<string> _openCopiedPromptInMode;

    private bool _populating;
    private string? _selectedPromptId;
    private string? _lastCopiedPromptName;
    private string? _lastCopiedModeId;
    private string? _startedQueuePromptId;

    /// <summary>One journal mode-filter choice: a label and the mode id it filters by (null = all modes).</summary>
    private sealed class ModeFilterOption
    {
        public required string Label { get; init; }
        public string? ModeId { get; init; }
    }

    /// <summary>One journal date-filter choice: a label and the Core range it applies.</summary>
    private sealed class RangeFilterOption
    {
        public required string Label { get; init; }
        public BattleJournalDateRange Range { get; init; }
    }

    /// <summary>One library row: the prompt plus its display category.</summary>
    public sealed class PromptRow
    {
        public required BattlePrompt Prompt { get; init; }
        public string Name => Prompt.Name;
        public string CategoryLabel => string.IsNullOrWhiteSpace(Prompt.Category) ? "General" : Prompt.Category;
    }

    /// <summary>One queue row: "1  Name" plus its ready / running / waiting state.</summary>
    public sealed class QueueRow
    {
        public required int Index { get; init; }
        public required string Label { get; init; }
        public required string State { get; init; }
    }

    /// <summary>One journal card: the entry plus its formatted date and outcome chip.</summary>
    public sealed class JournalRow
    {
        public required BattleJournalEntry Entry { get; init; }
        public required Brush ChipBrush { get; init; }
        public string PromptName => Entry.PromptName;
        public string OutcomeLabel => BattleOutcomeText.Label(Entry.Outcome);
        /// <summary>The recorded mode's label, or nothing for entries logged before modes were recorded.</summary>
        public string ModeLabel => ArenaModes.TryGet(Entry.ModeId, out var mode) ? mode.Label : "";
        public string Note => Entry.Note;
        public string When
        {
            get
            {
                var local = Entry.RecordedUtc.ToLocalTime();
                var today = DateTime.Now.Date;
                if (local.Date == today)
                    return $"Today, {local:HH:mm}";
                if (local.Date == today.AddDays(-1))
                    return "Yesterday";
                if (local.Date > today.AddDays(-7))
                    return local.ToString("ddd");
                return local.ToString("yyyy-MM-dd");
            }
        }
    }

    public BattleLabWindow(DiagnosticLog log, Action<string> openCopiedPromptInMode)
    {
        _log = log;
        _openCopiedPromptInMode = openCopiedPromptInMode;
        _library = BattleLabLibrary.Open(AppPaths.BattleLabPath);
        InitializeComponent();
        CopyTargetCombo.ItemsSource = new[]
        {
            ArenaModes.All.First(m => m.Id == ArenaModes.BattleId),
            ArenaModes.All.First(m => m.Id == ArenaModes.DirectId),
            ArenaModes.All.First(m => m.Id == ArenaModes.SideBySideId),
        };
        CopyTargetCombo.SelectedIndex = 0;
        JournalModeCombo.ItemsSource = new[]
        {
            new ModeFilterOption { Label = "All modes", ModeId = null },
            new ModeFilterOption { Label = "Battle", ModeId = ArenaModes.BattleId },
            new ModeFilterOption { Label = "Direct Chat", ModeId = ArenaModes.DirectId },
            new ModeFilterOption { Label = "Side by Side", ModeId = ArenaModes.SideBySideId },
        };
        JournalModeCombo.SelectedIndex = 0;
        JournalRangeCombo.ItemsSource = new[]
        {
            new RangeFilterOption { Label = "All time", Range = BattleJournalDateRange.AllTime },
            new RangeFilterOption { Label = "Last 7 days", Range = BattleJournalDateRange.Last7Days },
            new RangeFilterOption { Label = "Last 30 days", Range = BattleJournalDateRange.Last30Days },
        };
        JournalRangeCombo.SelectedIndex = 0;
        RefreshLibrary();
        RefreshQueue();
        RefreshJournal();
        if (PromptList.Items.Count > 0)
            PromptList.SelectedIndex = 0;
    }

    /// <summary>The page the copy button opens: the mode picked beside it, Battle when nothing is picked.</summary>
    private ArenaMode CopyTarget =>
        CopyTargetCombo?.SelectedItem as ArenaMode ?? ArenaModes.All[0];

    private void OnCopyTargetChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        var label = CopyTarget.Label;
        CopyButton.Content = $"Copy prompt & open {label}";
        System.Windows.Automation.AutomationProperties.SetName(CopyButton, $"Copy prompt and open {label}");
    }

    private BattlePrompt? SelectedPrompt =>
        (PromptList.SelectedItem as PromptRow)?.Prompt
        ?? (_selectedPromptId is null ? null : _library.FindPrompt(_selectedPromptId));

    // ─────────────────────────── library ───────────────────────────

    private void RefreshLibrary()
    {
        var selectedId = _selectedPromptId;
        var rows = _library.SearchPrompts(SearchText?.Text)
            .Select(p => new PromptRow { Prompt = p }).ToList();
        PromptList.ItemsSource = rows;
        var row = rows.FirstOrDefault(r => r.Prompt.Id == selectedId) ?? rows.FirstOrDefault();
        if (row is not null)
            PromptList.SelectedItem = row;
        else
            PopulateEditor(null);
    }

    private void PopulateEditor(BattlePrompt? prompt)
    {
        _populating = true;
        try
        {
            _selectedPromptId = prompt?.Id;
            PromptNameText.Text = prompt?.Name ?? "";
            PromptBodyText.Text = prompt?.Body ?? "";
            var categories = _library.Categories();
            PromptCategoryCombo.ItemsSource = categories;
            PromptCategoryCombo.Text = prompt is null
                ? ""
                : string.IsNullOrWhiteSpace(prompt.Category) ? "General" : prompt.Category;
            var hasPrompt = prompt is not null;
            PromptNameText.IsEnabled = hasPrompt;
            PromptBodyText.IsEnabled = hasPrompt;
            PromptCategoryCombo.IsEnabled = hasPrompt;
            CopyButton.IsEnabled = hasPrompt;
            CopyTargetCombo.IsEnabled = hasPrompt;
            QueueButton.IsEnabled = hasPrompt;
            DeletePromptButton.IsEnabled = hasPrompt;
            DuplicatePromptButton.IsEnabled = hasPrompt;
            EnqueueCategoryButton.IsEnabled = hasPrompt;
            UpdateBlanksHint(prompt);
        }
        finally
        {
            _populating = false;
        }
    }

    private void UpdateBlanksHint(BattlePrompt? prompt)
    {
        var blanks = prompt is null ? Array.Empty<string>() : PromptBlanks.Extract(prompt.Body);
        BlanksHint.Text = blanks.Count == 0
            ? "Blanks like [topic] ask you to fill them in each run."
            : $"Blanks to fill in at copy time: {string.Join(", ", blanks.Select(b => $"[{b}]"))}.";
    }

    private void OnPromptSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        PopulateEditor((PromptList.SelectedItem as PromptRow)?.Prompt);

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsLoaded)
            RefreshLibrary();
    }

    private void OnEditorChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_populating || _selectedPromptId is null)
            return;
        var prompt = _library.FindPrompt(_selectedPromptId);
        if (prompt is null)
            return;
        _library.UpdatePrompt(prompt.Id, PromptNameText.Text, PromptCategoryCombo.Text, PromptBodyText.Text);
        UpdateBlanksHint(_library.FindPrompt(prompt.Id));
        // Reflect renames in the list without disturbing the editor.
        var selectedId = _selectedPromptId;
        PromptList.ItemsSource = _library.SearchPrompts(SearchText.Text)
            .Select(p => new PromptRow { Prompt = p }).ToList();
        PromptList.SelectedItem = (PromptList.ItemsSource as List<PromptRow>)?
            .FirstOrDefault(r => r.Prompt.Id == selectedId);
        _selectedPromptId = selectedId;
    }

    private void OnCategoryCommitted(object sender, RoutedEventArgs e)
    {
        if (_populating || _selectedPromptId is null)
            return;
        var prompt = _library.FindPrompt(_selectedPromptId);
        if (prompt is not null)
            _library.UpdatePrompt(prompt.Id, PromptNameText.Text, PromptCategoryCombo.Text, PromptBodyText.Text);
    }

    private void OnNewPrompt(object sender, RoutedEventArgs e)
    {
        var prompt = _library.AddPrompt("New prompt", "General",
            "Write the prompt here. Square brackets like [topic] become blanks to fill in at copy time.");
        SearchText.Text = "";
        RefreshLibrary();
        PromptList.SelectedItem = (PromptList.ItemsSource as List<PromptRow>)?
            .FirstOrDefault(r => r.Prompt.Id == prompt.Id);
        PromptNameText.Focus();
        PromptNameText.SelectAll();
    }

    private void OnDeletePrompt(object sender, RoutedEventArgs e)
    {
        var prompt = SelectedPrompt;
        if (prompt is null)
            return;
        if (MessageBox.Show(this,
                $"Delete the prompt '{prompt.Name}'? It also leaves the run queue. Journal entries keep their history.",
                "Delete prompt", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _library.DeletePrompt(prompt.Id);
        _selectedPromptId = null;
        RefreshLibrary();
        RefreshQueue();
        SetStatus($"Deleted '{prompt.Name}'.");
    }

    private void OnDuplicatePrompt(object sender, RoutedEventArgs e)
    {
        var prompt = SelectedPrompt;
        if (prompt is null)
            return;
        var copy = _library.DuplicatePrompt(prompt.Id);
        if (copy is null)
        {
            SetStatus($"The library is full ({BattleLabLibrary.MaxPrompts} prompts) — delete one before duplicating.");
            return;
        }
        SearchText.Text = "";
        RefreshLibrary();
        PromptList.SelectedItem = (PromptList.ItemsSource as List<PromptRow>)?
            .FirstOrDefault(r => r.Prompt.Id == copy.Id);
        SetStatus($"Duplicated as '{copy.Name}'.");
    }

    // ────────────────────── library export / import ──────────────────────

    private void OnExportLibrary(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export the Battle Lab prompt library",
            FileName = "battle-lab-library.json",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, _library.ExportPromptsJson());
            SetStatus($"Exported {_library.Prompts.Count} prompts to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            _log.Warn($"Battle Lab: library export failed: {ex.Message}");
            SetStatus("Exporting the library failed — the file couldn't be written.");
        }
    }

    private void OnImportLibrary(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import a Battle Lab prompt library",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        string json;
        try
        {
            json = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            _log.Warn($"Battle Lab: library import read failed: {ex.Message}");
            SetStatus("That file couldn't be read — nothing was imported.");
            return;
        }
        // One choice per import, applied to the whole file: how
        // title clashes resolve (the merge itself is Core's).
        var strategyDialog = new ImportStrategyWindow { Owner = this };
        strategyDialog.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(strategyDialog);
        if (strategyDialog.ShowDialog() != true)
            return;
        var result = _library.ImportPrompts(json, strategyDialog.Strategy);
        if (!result.Ok)
        {
            SetStatus("That isn't a Battle Lab library file — nothing was imported.");
            return;
        }
        RefreshLibrary();
        SetStatus(ImportStatusText(result));
    }

    /// <summary>The import outcome in one line, naming whichever of added / replaced / skipped actually happened.</summary>
    private static string ImportStatusText(BattleLabImportResult result)
    {
        var parts = new List<string>();
        if (result.Added > 0)
            parts.Add($"imported {result.Added} {(result.Added == 1 ? "prompt" : "prompts")}");
        if (result.Replaced > 0)
            parts.Add($"replaced {result.Replaced} {(result.Replaced == 1 ? "prompt" : "prompts")} already in the library");
        if (parts.Count == 0)
            return $"Nothing new to import — {result.Skipped} {(result.Skipped == 1 ? "prompt was" : "prompts were")} already in the library, skipped.";
        var text = string.Join("; ", parts);
        text = char.ToUpperInvariant(text[0]) + text[1..];
        if (result.Skipped > 0)
            text += $"; {result.Skipped} skipped";
        return text + ".";
    }

    // ──────────────────────── copy & open a mode ────────────────────────

    /// <summary>Enhancement pack: show the duplicate report — identical bodies first, then shared names — or say the library is clean.</summary>
    private void OnFindDuplicates(object sender, RoutedEventArgs e)
    {
        var report = DuplicatePrompts.Report(_library.Prompts);
        if (report is null)
        {
            SetStatus($"No duplicates — all {_library.Prompts.Count} prompts are unique by text and by name.");
            MessageBox.Show(this,
                "Nothing to report: every prompt in the library has different text, and no name is used twice.",
                "Find duplicates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetStatus("Duplicates found — see the report.");
        MessageBox.Show(this, report, "Find duplicates", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Enhancement pack: a very long prompt gets a warning before it is
    /// copied — Arena's prompt box may cut it short — with the choice to
    /// copy anyway or export it as a .txt file instead. Short prompts
    /// take exactly the old path with no extra click.
    /// </summary>
    private bool OfferLongPromptChoices(string promptName, string text)
    {
        if (!PastedTextPolicy.NeedsWarning(text))
            return true;
        var preview = PastedTextPolicy.Preview(text);
        var answer = MessageBox.Show(this,
            $"{PastedTextPolicy.WarningText(text)}\n\nStart of the prompt:\n{preview}\n\n" +
            "Choose Yes to copy it anyway, No to export it as a .txt file instead, or Cancel to do neither.",
            $"Long prompt — {promptName}", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.No)
        {
            ExportPromptText(promptName, text);
            return false;
        }
        if (answer == MessageBoxResult.Cancel)
        {
            SetStatus("Copy cancelled — nothing was put on the clipboard.");
            return false;
        }
        return true;
    }

    /// <summary>The long-prompt export: a plain .txt file, saved where the user picks (never a path chosen for them).</summary>
    private void ExportPromptText(string promptName, string text)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt",
            FileName = PastedTextPolicy.ExportFileName(promptName, DateTimeOffset.Now),
            Title = "Export the prompt as a text file",
        };
        var invoker = ArenaDesktop.FocusReturn.Capture();
        var chosen = dialog.ShowDialog(this) == true;
        ArenaDesktop.FocusReturn.Restore(invoker);
        if (!chosen)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, text);
            SetStatus($"Prompt exported to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't export the prompt: {ex.Message}");
        }
    }

    private void OnCopyAndOpenBattle(object sender, RoutedEventArgs e)
    {
        if (SelectedPrompt is { } prompt)
            CopyAndOpenBattle(prompt, fromQueue: false);
    }

    private void CopyAndOpenBattle(BattlePrompt prompt, bool fromQueue)
    {
        var finalText = prompt.Body;
        var blanks = PromptBlanks.Extract(prompt.Body);
        if (blanks.Count > 0)
        {
            var dialog = new FillBlanksWindow(prompt.Name, blanks) { Owner = this };
            dialog.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(dialog);
            if (dialog.ShowDialog() != true)
                return;
            finalText = dialog.Fill(prompt.Body);
        }
        // Enhancement pack: a very long prompt is checked before it
        // reaches the clipboard, and the user picks copy-anyway,
        // export as .txt, or neither. Short prompts skip this entirely.
        if (!OfferLongPromptChoices(prompt.Name, finalText))
            return;
        try
        {
            Clipboard.SetText(finalText);
        }
        catch (Exception ex)
        {
            _log.Warn($"Battle Lab: clipboard copy failed: {ex.Message}");
            SetStatus("Copying to the clipboard failed — try again.");
            return;
        }
        var target = CopyTarget;
        _lastCopiedPromptName = prompt.Name;
        _lastCopiedModeId = target.Id;
        if (fromQueue)
            _startedQueuePromptId = prompt.Id;
        _log.Info($"Battle Lab: copied prompt '{prompt.Name}' and opened {target.Label}");
        SetStatus($"'{prompt.Name}' copied — paste it into {target.Label} (Ctrl+V).");
        RefreshQueue();
        _openCopiedPromptInMode(target.Id);
    }

    // ─────────────────────────── run queue ───────────────────────────

    private void RefreshQueue()
    {
        var prompts = _library.QueuePrompts;
        // Enhancement pack: the row label is Core's shared "2 of 5 ·
        // name" formatter, so the queue and the status line read alike.
        QueueList.ItemsSource = prompts.Select((p, i) => new QueueRow
        {
            Index = i,
            Label = RunQueue.PositionLabel(i, prompts.Count, p.Name),
            State = i == 0
                ? (_startedQueuePromptId is not null && _startedQueuePromptId == p.Id ? "running" : "ready")
                : "waiting",
        }).ToList();
        UseNextButton.IsEnabled = prompts.Count > 0;
    }

    private void OnAddToQueue(object sender, RoutedEventArgs e)
    {
        if (SelectedPrompt is not { } prompt)
            return;
        _library.Enqueue(prompt.Id);
        RefreshQueue();
        SetStatus($"'{prompt.Name}' added to the run queue.");
    }

    private void OnEnqueueCategory(object sender, RoutedEventArgs e)
    {
        if (SelectedPrompt is not { } prompt)
            return;
        var category = string.IsNullOrWhiteSpace(prompt.Category) ? "General" : prompt.Category.Trim();
        var added = _library.EnqueueCategory(category);
        RefreshQueue();
        SetStatus(added > 0
            ? $"Added {added} {(added == 1 ? "prompt" : "prompts")} from '{category}' to the run queue."
            : $"Everything in '{category}' is already in the run queue.");
    }

    private void OnUseNext(object sender, RoutedEventArgs e)
    {
        var next = _library.NextUp;
        if (next is null)
        {
            SetStatus("The run queue is empty.");
            return;
        }
        CopyAndOpenBattle(next, fromQueue: true);
    }

    private void OnQueueMoveUp(object sender, RoutedEventArgs e) => MoveSelectedInQueue(-1);

    private void OnQueueMoveDown(object sender, RoutedEventArgs e) => MoveSelectedInQueue(+1);

    private void MoveSelectedInQueue(int delta)
    {
        if (QueueList.SelectedItem is not QueueRow row)
            return;
        if (!_library.MoveInQueue(row.Index, delta))
        {
            // At an end: say so instead of leaving the click unanswered.
            SetStatus(delta < 0 ? "That prompt is already first in the queue." : "That prompt is already last in the queue.");
            return;
        }
        // Rebuild first, then land on the moved row: a second click (or
        // the keyboard) keeps moving the same prompt, and the status
        // line names its new place.
        var moved = row.Index + delta;
        var prompts = _library.QueuePrompts;
        RefreshQueue();
        if (moved >= 0 && moved < prompts.Count)
        {
            QueueList.SelectedIndex = moved;
            SetStatus(RunQueue.PositionLabel(moved, prompts.Count, prompts[moved].Name));
        }
    }

    private void OnQueueRemove(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueRow row)
            return;
        var prompts = _library.QueuePrompts;
        if (row.Index < prompts.Count && _startedQueuePromptId == prompts[row.Index].Id)
            _startedQueuePromptId = null;
        _library.RemoveFromQueueAt(row.Index);
        RefreshQueue();
    }

    private void OnQueueClear(object sender, RoutedEventArgs e)
    {
        _startedQueuePromptId = null;
        _library.ClearQueue();
        RefreshQueue();
    }

    // ─────────────────────────── journal ───────────────────────────

    private string? JournalModeFilter =>
        (JournalModeCombo?.SelectedItem as ModeFilterOption)?.ModeId;

    private BattleJournalDateRange JournalRangeFilter =>
        (JournalRangeCombo?.SelectedItem as RangeFilterOption)?.Range ?? BattleJournalDateRange.AllTime;

    /// <summary>The journal exactly as the three filters (name, mode, date range) shape it — the list, the scoreboard and the Markdown export all read this one view, so they can never disagree.</summary>
    private IReadOnlyList<BattleJournalEntry> CurrentJournalEntries() =>
        _library.JournalEntries(JournalFilterText?.Text, JournalModeFilter, JournalRangeFilter);

    private void RefreshJournal()
    {
        var chipBrushes = new Dictionary<BattleOutcome, Brush>
        {
            [BattleOutcome.AWins] = new SolidColorBrush(Color.FromRgb(0x7A, 0x1F, 0x23)),
            [BattleOutcome.BWins] = (Brush)FindResource("AccentFillBrush"),
            [BattleOutcome.Tie] = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
        };
        var entries = CurrentJournalEntries();
        JournalList.ItemsSource = entries
            .Select(e => new JournalRow { Entry = e, ChipBrush = chipBrushes[e.Outcome] }).ToList();
        if (ScoreboardText is not null)
            ScoreboardText.Text = BattleJournalTally.Of(entries).ScoreboardLine;
    }

    private void OnJournalFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsLoaded)
            RefreshJournal();
    }

    private void OnJournalFilterControlChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            RefreshJournal();
    }

    private void OnLogResult(object sender, RoutedEventArgs e)
    {
        var names = _library.Prompts.Select(p => p.Name).ToList();
        if (names.Count == 0)
        {
            SetStatus("Add a prompt first, then log its results.");
            return;
        }
        var fallback = _lastCopiedPromptName
                       ?? SelectedPrompt?.Name
                       ?? _library.NextUp?.Name;
        // The mode defaults to the page the prompt was last copied
        // to (or the copy picker's current target when nothing has
        // been copied yet); the dialog lets the user correct it.
        var dialog = new LogResultWindow(names, fallback, _lastCopiedModeId ?? CopyTarget.Id) { Owner = this };
        dialog.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(dialog);
        if (dialog.ShowDialog() != true)
            return;
        var entry = _library.AddJournalEntry(dialog.PromptName, dialog.Outcome, dialog.Note, modeId: dialog.ModeId);

        // The queue flow completes itself: when the result just logged
        // is for the prompt "Use next from queue" started, and that
        // prompt is still at the head, it leaves the queue.
        var head = _library.NextUp;
        if (_startedQueuePromptId is not null && head is not null
            && head.Id == _startedQueuePromptId
            && string.Equals(entry.PromptName, head.Name, StringComparison.OrdinalIgnoreCase))
        {
            _library.RemoveFromQueueAt(0);
            _startedQueuePromptId = null;
            SetStatus($"Result logged for '{entry.PromptName}' — it has left the run queue.");
        }
        else
        {
            SetStatus($"Result logged for '{entry.PromptName}'.");
        }
        RefreshJournal();
        RefreshQueue();
    }

    private void OnDeleteJournalEntry(object sender, RoutedEventArgs e)
    {
        if (JournalList.SelectedItem is not JournalRow row)
            return;
        if (MessageBox.Show(this, $"Delete this journal entry for '{row.PromptName}'?",
                "Delete entry", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _library.DeleteJournalEntry(row.Entry.Id);
        RefreshJournal();
    }

    private void OnExportJournal(object sender, RoutedEventArgs e)
    {
        var entries = CurrentJournalEntries();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
            Title = "Export the Battle Lab journal",
            FileName = $"battle-lab-journal-{DateTime.Now:yyyy-MM-dd}.md",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var markdown = BattleJournalMarkdown.RenderJournalMarkdown(entries, BattleJournalTally.Of(entries));
            File.WriteAllText(dialog.FileName, markdown);
            SetStatus($"Exported {entries.Count} journal {(entries.Count == 1 ? "entry" : "entries")} to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            _log.Warn($"Battle Lab: journal export failed: {ex.Message}");
            SetStatus("Exporting the journal failed — the file couldn't be written.");
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
