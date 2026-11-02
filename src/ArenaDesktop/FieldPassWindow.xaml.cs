using System.Windows;
using System.Windows.Media;
using ArenaDesktop.Core;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace ArenaDesktop;

/// <summary>
/// The field pass (v0.2.0): nine guided checks that prove
/// ArenaDesktop on this PC — layouts, Battle Lab, mode jumps, a
/// battle + journal entry, downloads, the tray + hotkey, the app
/// lock, profile switching, and the update check. Every check is a
/// shell check the user performs by hand; the window only records
/// their verdicts and notes, saved as they go by
/// <see cref="FieldPassSession"/> (there is no Save button). "Open
/// feature" buttons dispatch the main window's existing open
/// commands; nothing here ever injects into or reads from the Arena
/// page, and "Copy summary" renders the pass from Core as plain
/// markdown for a bug report.
/// </summary>
public partial class FieldPassWindow : Window
{
    private static readonly Brush PassedBrush = new SolidColorBrush(Color.FromRgb(0x63, 0xB3, 0x7A));
    private static readonly Brush ProblemBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x4B));
    private static readonly Brush SkippedBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static readonly Brush TodoBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x6E));
    private static readonly Brush CurrentBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x1A, 0x1A));

    private readonly DiagnosticLog _log;
    private readonly FieldPassSession _session;
    private readonly Action<FieldPassTarget> _openTarget;
    private readonly string _appVersion;

    private bool _populating;
    private int _currentIndex;

    /// <summary>One step-list row: its marker, colour, and current-step highlight.</summary>
    public sealed class StepRow
    {
        public required FieldPassStep Step { get; init; }
        public required int Number { get; init; }
        public required string Marker { get; init; }
        public required Brush MarkerBrush { get; init; }
        public required bool IsCurrent { get; init; }
        public string Label => $"{Number}. {Step.Title}";
        public Brush RowBackground => IsCurrent ? CurrentBrush : System.Windows.Media.Brushes.Transparent;
        public Thickness RowBorderThickness => IsCurrent ? new Thickness(1) : new Thickness(0);
    }

    public FieldPassWindow(DiagnosticLog log, Action<FieldPassTarget> openTarget, string appVersion)
    {
        _log = log;
        _openTarget = openTarget;
        _appVersion = appVersion;
        _session = FieldPassSession.Open(AppPaths.FieldPassPath);
        // Resume where the last pass left off (the first unmarked
        // check); a finished pass reopens on its last check.
        _currentIndex = Math.Min(_session.FirstUnfinishedIndex, FieldPassCatalog.Steps.Count - 1);
        InitializeComponent();
        Closed += OnWindowClosed;
        RefreshAll(selectCurrent: true);
    }

    private FieldPassStep CurrentStep => FieldPassCatalog.Steps[_currentIndex];

    // ─────────────────────────── rendering ───────────────────────────

    private void RefreshAll(bool selectCurrent)
    {
        RefreshSteps();
        ShowStep();
        if (selectCurrent)
        {
            _populating = true;
            StepList.SelectedIndex = _currentIndex;
            _populating = false;
        }
    }

    private void RefreshSteps()
    {
        var rows = new List<StepRow>();
        for (var i = 0; i < FieldPassCatalog.Steps.Count; i++)
        {
            var step = FieldPassCatalog.Steps[i];
            var verdict = _session.VerdictFor(step.Id);
            rows.Add(new StepRow
            {
                Step = step,
                Number = i + 1,
                IsCurrent = i == _currentIndex,
                Marker = verdict switch
                {
                    FieldPassVerdict.Pass => "✓",
                    FieldPassVerdict.Problem => "!",
                    FieldPassVerdict.Skipped => "–",
                    _ => "○",
                },
                MarkerBrush = verdict switch
                {
                    FieldPassVerdict.Pass => PassedBrush,
                    FieldPassVerdict.Problem => ProblemBrush,
                    FieldPassVerdict.Skipped => SkippedBrush,
                    _ => TodoBrush,
                },
            });
        }
        _populating = true;
        StepList.ItemsSource = rows;
        StepList.SelectedIndex = _currentIndex;
        _populating = false;

        var counts = _session.Counts;
        ProgressBar.Maximum = FieldPassCatalog.Steps.Count;
        ProgressBar.Value = counts.Done;
        CountsText.Text = $"{counts.Done} done · {counts.Remaining} to go";
        ProgressText.Text = FieldPassSummary.ProgressText(_session, _currentIndex + 1);
    }

    private void ShowStep()
    {
        var step = CurrentStep;
        var record = _session.RecordFor(step.Id);
        StepTitle.Text = $"Check {_currentIndex + 1} — {step.Title}";
        InstructionsText.Text = step.Instructions;
        SuccessText.Text = step.SuccessLine;
        if (step.Target == FieldPassTarget.None)
        {
            OpenFeatureButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            OpenFeatureButton.Visibility = Visibility.Visible;
            OpenFeatureButton.Content = FieldPassTargetText.OpenLabel(step.Target);
        }
        _populating = true;
        NoteText.Text = record?.Note ?? "";
        _populating = false;
        VerdictText.Text = record is null || record.Verdict == FieldPassVerdict.None
            ? "Not marked yet — you can change a mark at any time."
            : record.RecordedUtc is { } when
                ? $"Marked as {FieldPassVerdictText.Label(record.Verdict).ToLowerInvariant()} · {when.ToLocalTime():yyyy-MM-dd HH:mm} — you can change it at any time."
                : $"Marked as {FieldPassVerdictText.Label(record.Verdict).ToLowerInvariant()} — you can change it at any time.";
    }

    /// <summary>Keep a typed note when leaving a step — notes save as you go, verdict or not.</summary>
    private void FlushNote()
    {
        try { _session.SetNote(CurrentStep.Id, NoteText.Text); }
        catch (Exception ex) { _log.Warn($"Field pass note save failed: {ex.Message}"); }
    }

    // ─────────────────────────── events ───────────────────────────

    private void OnStepSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_populating || StepList.SelectedItem is not StepRow row)
            return;
        FlushNote();
        _currentIndex = row.Number - 1;
        RefreshSteps();
        ShowStep();
    }

    private void OnOpenFeature(object sender, RoutedEventArgs e)
    {
        if (CurrentStep.Target != FieldPassTarget.None)
            _openTarget(CurrentStep.Target);
    }

    private void OnPass(object sender, RoutedEventArgs e) => Mark(FieldPassVerdict.Pass);

    private void OnProblem(object sender, RoutedEventArgs e) => Mark(FieldPassVerdict.Problem);

    private void OnSkip(object sender, RoutedEventArgs e) => Mark(FieldPassVerdict.Skipped);

    private void Mark(FieldPassVerdict verdict)
    {
        try { _session.SetVerdict(CurrentStep.Id, verdict, NoteText.Text); }
        catch (Exception ex)
        {
            _log.Warn($"Field pass save failed: {ex.Message}");
            StatusText.Text = "Couldn't save that mark — see the log.";
            return;
        }
        // Advance to the next unmarked check; when none is left after
        // this one, the list simply stays put and shows the new mark.
        var next = _session.FirstUnfinishedIndex;
        if (next >= FieldPassCatalog.Steps.Count)
            next = _currentIndex;
        _currentIndex = next;
        RefreshAll(selectCurrent: true);
        StatusText.Text = "";
    }

    private void OnCopySummary(object sender, RoutedEventArgs e)
    {
        FlushNote();
        try
        {
            Clipboard.SetText(FieldPassSummary.Render(_session, _appVersion, DateTimeOffset.Now));
            StatusText.Text = "Summary copied — paste it into a bug report or keep it with your notes.";
        }
        catch
        {
            StatusText.Text = "Couldn't copy — the clipboard is busy.";
        }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Start the field pass over? Every verdict and note is cleared. This can't be undone.",
                "Start over", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _session.Reset();
        _currentIndex = 0;
        RefreshAll(selectCurrent: true);
        StatusText.Text = "Fresh pass — nothing is marked.";
    }

    private void OnWindowClosed(object? sender, EventArgs e) => FlushNote();
}
