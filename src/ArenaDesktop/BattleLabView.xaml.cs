using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArenaDesktop.Core;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace ArenaDesktop;

/// <summary>
/// Battle Lab as a control (v0.8.0). It used to be a window of its
/// own and is now the side pane of the main window, so a prompt can be
/// read while writing the battle it belongs to. The library, editor, run queue and journal
/// are unchanged; what is new is the <see cref="ApplyArrangement"/>
/// reflow (three columns, two columns, or one tabbed column, chosen by
/// <see cref="SplitWorkspace"/>) and the close button.
///
/// Everything is stored by <see cref="BattleLabLibrary"/> in Core and
/// saved as you go — there is no Save button. The copy button fills
/// the prompt's blanks, puts the finished text on the clipboard, and
/// brings the main window forward on the page picked beside it;
/// pasting is the user's. Nothing is ever injected into or read from
/// the Arena page, and journal results are recorded by hand.
/// </summary>

public partial class BattleLabView : UserControl
{
    /// <summary>Raised when the user closes the pane from its own ✕; the main window owns the split and does the closing.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Which of the three arrangements is on screen (three columns, two, or one tabbed column).</summary>
    public SplitWorkspace.PaneArrangement Arrangement { get; private set; } = SplitWorkspace.PaneArrangement.ThreeColumns;

    /// <summary>The pane that is currently visible: 0 prompts, 1 editor, 2 journal. Only meaningful when tabbed.</summary>
    public int ActivePane { get; private set; }

    private readonly PaneArrangementTracker _arrangementTracker = new();

    private readonly DiagnosticLog _log;
    private readonly BattleLabLibrary _library;
    private readonly Action<string> _openCopiedPromptInMode;

    private bool _populating;
    private string? _selectedPromptId;
    private string? _lastCopiedPromptName;
    private string? _lastCopiedModeId;
    private string? _startedQueuePromptId;

    // Hardening pack: the clipboard lifetime. A copied prompt is the one
    // thing this window hands to shared, long-lived state, so the app
    // remembers exactly what it put there and when it should go.
    private readonly AppSettings _settings;
    private string? _clipboardCopiedText;
    private DateTimeOffset? _clipboardClearAtUtc;
    private DispatcherTimer? _clipboardTimer;

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

    public BattleLabView(DiagnosticLog log, Action<string> openCopiedPromptInMode, AppSettings? settings = null)
    {
        _log = log;
        _openCopiedPromptInMode = openCopiedPromptInMode;
        _settings = settings ?? new AppSettings();
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
        // The pane can be closed, reopened, and re-parented between
        // arrangements; the timer is stopped when it leaves the tree and
        // restarted implicitly by the next copy, so nothing outlives it.
        Unloaded += (_, _) => StopClipboardTimer();
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
        // Hardening pack: a name box mid-edit is empty, and the editor
        // saves on every change; store the fallback rather than a blank
        // label that the library, queue and journal would all show.
        var savedName = PromptNaming.Normalize(PromptNameText.Text);
        _library.UpdatePrompt(prompt.Id, savedName, PromptCategoryCombo.Text, PromptBodyText.Text);
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
            // The category was committed, not the name — keep whatever
            // name the prompt already had rather than a mid-edit blank.
            _library.UpdatePrompt(prompt.Id, PromptNaming.Normalize(prompt.Name),
                PromptCategoryCombo.Text, PromptBodyText.Text);
    }

    private void OnNewPrompt(object sender, RoutedEventArgs e)
    {
        // Hardening pack: never reuse a name, so two new prompts are never
        // two identical rows in the list.
        var prompt = _library.AddPrompt(PromptNaming.NextUntitled(_library.Prompts), "General",
            "Write the prompt here. Square brackets like [topic] become blanks to fill in at copy time.");
        SearchText.Text = "";
        RefreshLibrary();
        PromptList.SelectedItem = (PromptList.ItemsSource as List<PromptRow>)?
            .FirstOrDefault(r => r.Prompt.Id == prompt.Id);
        FocusPane(1);
        PromptNameText.Focus();
        PromptNameText.SelectAll();
    }

    private void OnDeletePrompt(object sender, RoutedEventArgs e)
    {
        var prompt = SelectedPrompt;
        if (prompt is null)
            return;
        if (MessageBox.Show(OwnerWindow,
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

    /// <summary>
    /// Export the library (hardening pack: three formats, one dialog).
    /// JSON is the round trip the Import button reads; Markdown is what a
    /// person shares; CSV is what a spreadsheet opens. The filter is the
    /// format picker — the chosen filter decides the content and the
    /// suggested extension, so the two can never disagree.
    /// </summary>
    private void OnExportLibrary(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON library (*.json)|*.json|Markdown (*.md)|*.md|Spreadsheet CSV (*.csv)|*.csv",
            Title = "Export the Battle Lab prompt library",
            FileName = PromptExportText.FileNameFor("json", DateTimeOffset.Now),
            DefaultExt = ".json",
            AddExtension = true,
        };
        if (ShowFileDialog(dialog) != true)
            return;
        // The chosen filter index is the format: 0 JSON, 1 Markdown, 2 CSV.
        var (contents, label) = dialog.FilterIndex switch
        {
            1 => (PromptExportText.ToMarkdown(_library.Prompts), "Markdown"),
            2 => (PromptExportText.ToCsv(_library.Prompts), "CSV"),
            _ => (_library.ExportPromptsJson(), "JSON"),
        };
        try
        {
            File.WriteAllText(dialog.FileName, contents);
            SetStatus($"Exported {_library.Prompts.Count} prompts as {label} to {Path.GetFileName(dialog.FileName)}.");
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
        if (ShowFileDialog(dialog) != true)
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
        var strategyDialog = new ImportStrategyWindow { Owner = OwnerWindow };
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
            MessageBox.Show(OwnerWindow,
                "Nothing to report: every prompt in the library has different text, and no name is used twice.",
                "Find duplicates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetStatus("Duplicates found — see the report.");
        MessageBox.Show(OwnerWindow, report, "Find duplicates", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var answer = MessageBox.Show(OwnerWindow,
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
        var chosen = ShowFileDialog(dialog) == true;
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
        IReadOnlyDictionary<string, string> blanksFilled = new Dictionary<string, string>();
        var blanks = PromptBlanks.Extract(prompt.Body);
        if (blanks.Count > 0)
        {
            var dialog = new FillBlanksWindow(prompt.Name, blanks) { Owner = OwnerWindow };
            dialog.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(dialog);
            if (dialog.ShowDialog() != true)
                return;
            blanksFilled = dialog.Values;
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
        ArmClipboardClear(finalText);
        var target = CopyTarget;
        _lastCopiedPromptName = prompt.Name;
        _lastCopiedModeId = target.Id;
        if (fromQueue)
            _startedQueuePromptId = prompt.Id;
        _log.Info($"Battle Lab: copied prompt '{prompt.Name}' and opened {target.Label}");
        // Hardening pack: if the user skipped blanks, the copied text still
        // carries their [placeholders] — say so, and name them, instead of
        // letting the placeholder surface only in the pasted prompt.
        var unfilledNote = PromptBlanks.UnfilledSummary(prompt.Body, blanksFilled);
        SetStatus(unfilledNote.Length == 0
            ? $"'{prompt.Name}' copied — paste it into {target.Label} (Ctrl+V)."
            : $"'{prompt.Name}' copied, but {unfilledNote}. Paste it into {target.Label} (Ctrl+V).");
        RefreshQueue();
        // In the tabbed arrangement, copying from the prompts list would
        // otherwise leave the editor hidden behind the list the user just
        // acted on. Follow the action.
        FocusPane(1);
        _openCopiedPromptInMode(target.Id);
    }

    // ─────────────────────── clipboard lifetime (hardening pack) ───────────────────────

    /// <summary>
    /// Remember what was just copied and when it should be cleared
    /// (hardening pack). No timer runs when the setting is off — the
    /// default — so nothing changes for anyone who didn't ask.
    /// </summary>
    private void ArmClipboardClear(string copiedText)
    {
        _clipboardCopiedText = null;
        _clipboardClearAtUtc = null;
        _clipboardTimer?.Stop();
        var minutes = ClipboardLifetime.Sanitize(_settings.ClipboardClearMinutes);
        if (!ClipboardLifetime.ShouldArm(minutes))
            return;
        _clipboardCopiedText = copiedText;
        _clipboardClearAtUtc = ClipboardLifetime.ClearAt(DateTimeOffset.UtcNow, minutes);
        _clipboardTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _clipboardTimer.Tick -= OnClipboardTimerTick;
        _clipboardTimer.Tick += OnClipboardTimerTick;
        _clipboardTimer.Start();
    }

    /// <summary>
    /// Clear the clipboard once the delay is up — but only when it still
    /// holds exactly the text this window copied. Anything else there is
    /// the user's own copy and is left untouched, which is the promise
    /// the Settings note makes.
    /// </summary>
    private void OnClipboardTimerTick(object? sender, EventArgs e)
    {
        if (_clipboardClearAtUtc is not { } clearAt)
        {
            _clipboardTimer?.Stop();
            return;
        }
        string? current = null;
        try
        {
            current = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            // The clipboard can be held by another app for a moment;
            // try again on the next tick rather than guessing.
            return;
        }
        if (!ClipboardLifetime.ShouldClear(DateTimeOffset.UtcNow, clearAt, _clipboardCopiedText, current))
        {
            // The deadline passed but the clipboard now holds something
            // else — the user's own copy wins, and this is over.
            if (DateTimeOffset.UtcNow >= clearAt)
                _clipboardClearAtUtc = null;
            return;
        }
        try
        {
            Clipboard.Clear();
            SetStatus("Copied prompt cleared from the clipboard.");
            _log.Info("Battle Lab: copied prompt cleared from the clipboard on schedule");
        }
        catch (Exception ex)
        {
            _log.Warn($"Battle Lab: clipboard clear failed: {ex.Message}");
        }
        _clipboardCopiedText = null;
        _clipboardClearAtUtc = null;
        _clipboardTimer?.Stop();
    }

    /// <summary>Stop the clipboard timer when the window closes (the clipboard itself is the user's business after that).</summary>
    private void StopClipboardTimer() => _clipboardTimer?.Stop();

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
        var dialog = new LogResultWindow(names, fallback, _lastCopiedModeId ?? CopyTarget.Id) { Owner = OwnerWindow };
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
        if (MessageBox.Show(OwnerWindow, $"Delete this journal entry for '{row.PromptName}'?",
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
        if (ShowFileDialog(dialog) != true)
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

    // ─────────────────────────── the side pane (v0.8.0) ───────────────────────────

    /// <summary>
    /// The window that owns this control's dialogs. As a window it was
    /// itself; as a pane it is the main window, so a file dialog or a
    /// message box still centres on something and does not outlive it.
    /// </summary>
    private Window? OwnerWindow =>
        Window.GetWindow(this) ?? System.Windows.Application.Current?.MainWindow;

    /// <summary>
    /// Reflow to the width the shell gave us: three columns when the
    /// pane is as wide as the old window, two when half a window is
    /// all there is, one tabbed column when it is narrower than that.
    /// Cheap to call on every resize — the tracker drops the calls that
    /// would not change the answer.
    /// </summary>
    public void ApplyArrangement(double paneWidth)
    {
        if (!_arrangementTracker.NeedsChange(paneWidth))
            return;
        Arrangement = SplitWorkspace.Arrange(paneWidth);
        PaneTabs.Visibility = Arrangement == SplitWorkspace.PaneArrangement.Tabbed
            ? Visibility.Visible
            : Visibility.Collapsed;

        switch (Arrangement)
        {
            case SplitWorkspace.PaneArrangement.ThreeColumns:
                PlaceThreeColumns();
                break;
            case SplitWorkspace.PaneArrangement.TwoColumns:
                PlaceTwoColumns();
                break;
            default:
                PlaceTabbed();
                break;
        }
        ShowActivePane();
    }

    /// <summary>The original window's shape: library | editor | journal across one row.</summary>
    private void PlaceThreeColumns()
    {
        LibraryColumn.Width = new GridLength(272);
        EditorColumn.Width = new GridLength(1, GridUnitType.Star);
        JournalColumn.Width = new GridLength(356);
        TopRow.Height = new GridLength(1, GridUnitType.Star);
        BottomRow.Height = new GridLength(0);

        Place(LibraryPanel, 0, 0);
        Place(EditorPanel, 0, 1);
        Place(JournalPanel, 0, 2);
        // Each panel owns exactly one column in this arrangement. This
        // must be reset here: the two-column arrangement gives the
        // journal a span of 2, and leaving it set would overlap the
        // third column when the window widens again.
        Grid.SetColumnSpan(LibraryPanel, 1);
        Grid.SetColumnSpan(EditorPanel, 1);
        Grid.SetColumnSpan(JournalPanel, 1);
        LibraryPanel.Margin = new Thickness(0, 0, 10, 0);
        EditorPanel.Margin = new Thickness(0, 0, 10, 0);
        JournalPanel.Margin = new Thickness(0);
        LibraryPanel.Visibility = Visibility.Visible;
        EditorPanel.Visibility = Visibility.Visible;
        JournalPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Half a window: library and editor side by side on top, journal
    /// stacked underneath. The journal is the part that survives being
    /// short better than the editor does — it is a scrolling list.
    /// </summary>
    private void PlaceTwoColumns()
    {
        LibraryColumn.Width = new GridLength(220);
        EditorColumn.Width = new GridLength(1, GridUnitType.Star);
        JournalColumn.Width = new GridLength(0);
        TopRow.Height = new GridLength(1, GridUnitType.Star);
        BottomRow.Height = new GridLength(1, GridUnitType.Star);

        Place(LibraryPanel, 0, 0);
        Place(EditorPanel, 0, 1);
        Place(JournalPanel, 1, 0);
        // The journal spans both columns now, so it must not keep the
        // third column's gap.
        Grid.SetColumnSpan(JournalPanel, 2);
        Grid.SetColumnSpan(LibraryPanel, 1);
        Grid.SetColumnSpan(EditorPanel, 1);
        LibraryPanel.Margin = new Thickness(0, 0, 10, 0);
        EditorPanel.Margin = new Thickness(0, 0, 0, 10);
        JournalPanel.Margin = new Thickness(0);
        LibraryPanel.Visibility = Visibility.Visible;
        EditorPanel.Visibility = Visibility.Visible;
        JournalPanel.Visibility = Visibility.Visible;
    }

    /// <summary>One column, chosen by the tab strip: the last resort that is still usable.</summary>
    private void PlaceTabbed()
    {
        LibraryColumn.Width = new GridLength(1, GridUnitType.Star);
        EditorColumn.Width = new GridLength(0);
        JournalColumn.Width = new GridLength(0);
        TopRow.Height = new GridLength(1, GridUnitType.Star);
        BottomRow.Height = new GridLength(0);

        Place(LibraryPanel, 0, 0);
        Place(EditorPanel, 0, 0);
        Place(JournalPanel, 0, 0);
        Grid.SetColumnSpan(LibraryPanel, 1);
        Grid.SetColumnSpan(EditorPanel, 1);
        Grid.SetColumnSpan(JournalPanel, 1);
        LibraryPanel.Margin = new Thickness(0);
        EditorPanel.Margin = new Thickness(0);
        JournalPanel.Margin = new Thickness(0);
        // Only the active one is visible; ShowActivePane decides which.
        LibraryPanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Collapsed;
        JournalPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Move a panel to one cell of the Lab grid — re-parenting keeps its state, so this is safe to repeat.</summary>
    private static void Place(FrameworkElement panel, int row, int column)
    {
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
    }

    /// <summary>Show the pane the tab strip names (only the tabbed arrangement hides anything).</summary>
    private void ShowActivePane()
    {
        if (Arrangement != SplitWorkspace.PaneArrangement.Tabbed)
            return;
        LibraryPanel.Visibility = ActivePane == 0 ? Visibility.Visible : Visibility.Collapsed;
        EditorPanel.Visibility = ActivePane == 1 ? Visibility.Visible : Visibility.Collapsed;
        JournalPanel.Visibility = ActivePane == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPaneTab(object sender, RoutedEventArgs e)
    {
        ActivePane = PromptsTab.IsChecked == true ? 0 : EditorTab.IsChecked == true ? 1 : 2;
        ShowActivePane();
    }

    /// <summary>
    /// Bring the tab strip to a particular part of the Lab. Copying a
    /// prompt opens the editor, so in the tabbed arrangement the pane
    /// follows the action rather than leaving the user on a list.
    /// </summary>
    public void FocusPane(int pane)
    {
        ActivePane = Math.Clamp(pane, 0, 2);
        PromptsTab.IsChecked = ActivePane == 0;
        EditorTab.IsChecked = ActivePane == 1;
        JournalTab.IsChecked = ActivePane == 2;
        ShowActivePane();
    }

    /// <summary>
    /// Show a file dialog owned by this control's window. The
    /// owner-taking overload dereferences the window it is given, and a
    /// control briefly detached from its window — mid reflow, or during
    /// shutdown — can supply none, so fall back to the ownerless
    /// overload rather than failing the click.
    /// </summary>
    private bool? ShowFileDialog(Microsoft.Win32.CommonDialog dialog) =>
        OwnerWindow is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();

    private void OnClosePane(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
