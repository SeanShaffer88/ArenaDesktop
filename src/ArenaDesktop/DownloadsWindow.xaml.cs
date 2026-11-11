using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// Downloads: the live manager on top (S2 #13 — every active download
/// with its own progress, pause/resume where WebView2 allows it,
/// cancel, and retry for this session's failures) and the finished-
/// downloads history below (v0.4.0; S2 #16 search/filter/sort, S2 #17
/// Copy path / Open with… / Delete file). History loads with the
/// configured cap each time it is read so it matches Settings.
/// </summary>
public partial class DownloadsWindow : Window
{
    private readonly DiagnosticLog _log;
    private readonly DownloadTracker? _tracker;
    private readonly Func<string, Task>? _retryAsync;
    private readonly int _historyCap;
    private const int ActiveRefresh = 1;
    private const int HistoryRefresh = 2;
    private readonly DispatcherTimer _etaTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private int _pendingRefresh;
    private volatile bool _closed;
    private List<DownloadRecord> _allRecords = new();

    /// <summary>One history row: the record plus its pre-formatted detail line.</summary>
    public sealed class Row
    {
        public required DownloadRecord Record { get; init; }
        public string FileName => Record.FileName;
        public string Detail
        {
            get
            {
                var state = Record.State == DownloadRecordState.Failed
                    ? $"Failed{(string.IsNullOrWhiteSpace(Record.FailureReason) ? "" : $" ({Record.FailureReason})")} · "
                    : "";
                return $"{state}{DownloadText.FormatBytes(Record.SizeBytes)} · {Record.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {Record.FullPath}";
            }
        }
    }

    /// <summary>One active/session-failed row in the manager list.</summary>
    public sealed class ActiveRow
    {
        public required string Id { get; init; }
        public required string FileName { get; init; }
        public required string Detail { get; init; }
        public required DownloadActivityState State { get; init; }
        public bool IsSessionFailed { get; init; }
        public bool CanResume { get; init; }
    }

    public DownloadsWindow(
        DiagnosticLog log,
        DownloadTracker? tracker = null,
        Func<string, Task>? retryAsync = null,
        int historyCap = DownloadHistoryStore.MaxEntries)
    {
        _log = log;
        _tracker = tracker;
        _retryAsync = retryAsync;
        _historyCap = DownloadHistoryStore.SanitizeCap(historyCap);
        InitializeComponent();
        _etaTimer.Tick += (_, _) => RefreshActive();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && !_closed)
                RefreshActive();
            else
                _etaTimer.Stop();
        };
        if (_tracker is not null)
        {
            _tracker.Changed += OnTrackerChanged;
            _tracker.Completed += OnTrackerFinished;
            _tracker.Failed += OnTrackerFinished;
        }
        Refresh();
        RefreshActive();
    }

    private void OnTrackerChanged() => QueueRefresh(ActiveRefresh);

    private void OnTrackerFinished() => QueueRefresh(ActiveRefresh | HistoryRefresh);

    private void QueueRefresh(int refresh)
    {
        if (_closed || Dispatcher.HasShutdownStarted)
            return;
        // Keep one dispatcher operation pending, even when progress arrives in bursts.
        if (Interlocked.Or(ref _pendingRefresh, refresh) == 0)
            Dispatcher.InvokeAsync(ApplyPendingRefresh, DispatcherPriority.Background);
    }

    private void ApplyPendingRefresh()
    {
        var refresh = Interlocked.Exchange(ref _pendingRefresh, 0);
        if (_closed)
            return;
        if ((refresh & ActiveRefresh) != 0)
            RefreshActive();
        if ((refresh & HistoryRefresh) != 0)
            RefreshHistoryOnly();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _etaTimer.Stop();
        if (_tracker is not null)
        {
            _tracker.Changed -= OnTrackerChanged;
            _tracker.Completed -= OnTrackerFinished;
            _tracker.Failed -= OnTrackerFinished;
        }
        base.OnClosed(e);
    }

    private Row? Selected => DownloadsList.SelectedItem as Row;
    private ActiveRow? SelectedActive => ActiveList.SelectedItem as ActiveRow;

    // ───────────────────────── history ─────────────────────────

    private void Refresh()
    {
        _allRecords = DownloadHistoryStore.Load(AppPaths.DownloadsHistoryPath, _historyCap);
        ApplyHistoryQuery();
        UpdateButtons();
    }

    /// <summary>
    /// Reload history after the tracker writes a finished download.
    /// Keep a selected record if it is still in the filtered list.
    /// </summary>
    private void RefreshHistoryOnly()
    {
        var loaded = DownloadHistoryStore.Load(AppPaths.DownloadsHistoryPath, _historyCap);
        var changed = loaded.Count != _allRecords.Count || loaded.Zip(_allRecords).Any(pair =>
            pair.First.FullPath != pair.Second.FullPath
            || pair.First.CompletedUtc != pair.Second.CompletedUtc
            || pair.First.State != pair.Second.State);
        if (!changed)
            return;
        var selected = Selected?.Record;
        _allRecords = loaded;
        ApplyHistoryQuery();
        if (selected is not null)
        {
            DownloadsList.SelectedItem = DownloadsList.Items.Cast<Row>().FirstOrDefault(row =>
                row.Record.FullPath == selected.FullPath
                && row.Record.CompletedUtc == selected.CompletedUtc
                && row.Record.State == selected.State);
        }
    }

    private void ApplyHistoryQuery()
    {
        // ComboBox IsSelected in XAML can fire SelectionChanged while
        // InitializeComponent is still creating the other controls.
        if (DownloadsList is null || StateFilterCombo is null || SortCombo is null || NoteText is null
            || SizeFilterCombo is null)
            return;
        var filterTag = (StateFilterCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        var filter = filterTag switch
        {
            "Completed" => DownloadStateFilter.Completed,
            "Failed" => DownloadStateFilter.Failed,
            _ => DownloadStateFilter.All,
        };
        // Enhancement pack: two more orders (oldest and smallest first,
        // for cleaning up) beside the three the window has always had.
        var sortTag = (SortCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        var sort = sortTag switch
        {
            "OldestFirst" => DownloadsSortOrder.OldestFirst,
            "LargestFirst" => DownloadsSortOrder.LargestFirst,
            "SmallestFirst" => DownloadsSortOrder.SmallestFirst,
            "NameAscending" => DownloadsSortOrder.NameAscending,
            _ => DownloadsSortOrder.NewestFirst,
        };
        // Enhancement pack: the optional size band, composed with the
        // search, the state filter and the sort.
        var sizeTag = (SizeFilterCombo!.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        var band = DownloadSizeBands.Find(sizeTag);
        // Hardening pack: rebuilding the ItemsSource on every keystroke
        // destroyed the selection and the scroll position — typing a
        // second character threw the list back to the top. Reuse the rows
        // that are already on screen (same record => same Row, so the
        // container's selection survives) and only create the ones that
        // are new. The identity check costs one pass over the filtered
        // list and saves the whole visual tree from being thrown away.
        var filtered = DownloadsView.Apply(_allRecords, SearchText?.Text, filter, band, sort);
        var previous = DownloadsList.ItemsSource as IReadOnlyList<Row>;
        // A queue, not a dictionary: two records can legitimately share a
        // path, a timestamp and a state (the same file downloaded twice in
        // the same second), and keying a dictionary on that tuple would
        // throw. Rows are matched in order, which is also the order they
        // appear in, so the surviving row keeps its container.
        var reusable = new Dictionary<(string, DateTimeOffset, DownloadRecordState), Queue<Row>>();
        if (previous is not null)
        {
            foreach (var row in previous)
            {
                var key = (row.Record.FullPath, row.Record.CompletedUtc, row.Record.State);
                if (!reusable.TryGetValue(key, out var queue))
                    reusable[key] = queue = new Queue<Row>();
                queue.Enqueue(row);
            }
        }
        var rows = filtered
            .Select(record =>
            {
                var key = (record.FullPath, record.CompletedUtc, record.State);
                return reusable.TryGetValue(key, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : new Row { Record = record };
            })
            .ToList();
        var selectedBefore = Selected;
        // Same rows in the same order: leave the items alone entirely, so
        // the selection and the scroll offset stay exactly where they are.
        var unchanged = previous is not null
            && rows.Count == previous.Count
            && rows.Zip(previous).All(pair => ReferenceEquals(pair.First, pair.Second));
        if (!unchanged)
        {
            DownloadsList.ItemsSource = rows;
            if (selectedBefore is not null)
            {
                // Keep the user's place across a filter change: the same
                // record stays selected, so the action buttons stay live.
                DownloadsList.SelectedItem = rows.FirstOrDefault(row => ReferenceEquals(row, selectedBefore)
                    || (row.Record.FullPath == selectedBefore.Record.FullPath
                        && row.Record.CompletedUtc == selectedBefore.Record.CompletedUtc
                        && row.Record.State == selectedBefore.Record.State));
            }
        }
        UpdateButtons();
        if (_allRecords.Count == 0)
            NoteText.Text = "Nothing here yet — files you download from Arena will appear in this list.";
        else if (filtered.Count == 0)
            NoteText.Text = "No downloads match the current search/filter.";
        else
            NoteText.Text = $"Files Arena downloaded for you, kept on this PC only. Shown: {DownloadsView.SummaryText(filtered)}.";
    }

    private void OnSearchOrFilterChanged(object sender, object e) => ApplyHistoryQuery();

    /// <summary>
    /// Keyboard access to the history list (hardening pack). Every action
    /// here had a button, and every one of those buttons was reachable
    /// only by tabbing to it or reaching for the mouse: the list itself
    /// had no keys at all. Enter opens the file, Delete removes it from
    /// this PC (through the same confirmation as the button), Ctrl+C puts
    /// the path on the clipboard, and F5 re-reads the history.
    ///
    /// Handled only when the key does something, so typing in the search
    /// box (which shares the window) is never stolen.
    /// </summary>
    private void OnHistoryKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Selected is not { } row)
            return;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter:
                OnOpen(sender, e);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Delete:
                // The key must do whatever the visible button would do, or
                // a multi-selection would delete only the focused row and
                // silently leave the rest.
                if ((DownloadsList.SelectedItems?.Count ?? 0) >= 2)
                    OnDeleteSelected(sender, e);
                else
                    OnDeleteFile(sender, e);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.C when (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0:
                CopyPathOf(row);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.F5:
                Refresh();
                NoteText.Text = "History reloaded.";
                e.Handled = true;
                break;
        }
    }

    /// <summary>The path goes on the clipboard, with the same failure wording the Copy button uses.</summary>
    private void CopyPathOf(Row row)
    {
        try
        {
            System.Windows.Clipboard.SetText(row.Record.FullPath);
            NoteText.Text = "Path copied to the clipboard.";
        }
        catch (Exception ex)
        {
            _log.Warn($"Downloads window keyboard copy failed: {ex.Message}");
            NoteText.Text = "Couldn't copy — the clipboard is busy. Try again.";
        }
    }

    private void UpdateButtons()
    {
        var enabled = Selected is not null;
        OpenButton.IsEnabled = enabled;
        FolderButton.IsEnabled = enabled;
        CopyPathButton.IsEnabled = enabled;
        OpenWithButton.IsEnabled = enabled;
        DeleteFileButton.IsEnabled = enabled;
        // Enhancement pack 10: with several rows selected the single-file
        // button is replaced by the bulk one, rather than sitting beside
        // it. Two delete buttons side by side would be a guess about which
        // one the user meant, and the row is fixed-width — the pair would
        // clip at the window's minimum size.
        var count = DownloadsList?.SelectedItems?.Count ?? 0;
        var bulk = count >= 2;
        if (DeleteSelectedButton is not null)
        {
            DeleteSelectedButton.Visibility = bulk ? Visibility.Visible : Visibility.Collapsed;
            DeleteSelectedButton.IsEnabled = bulk;
            DeleteSelectedButton.Content = bulk ? $"Delete {count} selected…" : "Delete selected…";
        }
        DeleteFileButton.Visibility = bulk ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateButtons();

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;
        if (!File.Exists(row.Record.FullPath))
        {
            NoteText.Text = $"\"{row.Record.FileName}\" isn't at that location anymore (moved or deleted).";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(row.Record.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn($"Downloads window open failed: {ex.Message}");
            NoteText.Text = $"Couldn't open \"{row.Record.FileName}\".";
        }
    }

    private void OnShowInFolder(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;
        try
        {
            if (File.Exists(row.Record.FullPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Record.FullPath}\"")
                    { UseShellExecute = true });
            }
            else if (Path.GetDirectoryName(row.Record.FullPath) is { } dir && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            else
            {
                NoteText.Text = $"The folder for \"{row.Record.FileName}\" no longer exists.";
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Downloads window show-in-folder failed: {ex.Message}");
        }
    }

    /// <summary>Copy the full path of the selected download (S2 #17).</summary>
    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;
        try
        {
            System.Windows.Clipboard.SetText(row.Record.FullPath);
            NoteText.Text = $"Copied: {row.Record.FullPath}";
        }
        catch (Exception ex)
        {
            _log.Warn($"Downloads window copy-path failed: {ex.Message}");
            NoteText.Text = "Couldn't copy the path — the clipboard is busy.";
        }
    }

    /// <summary>
    /// Open-with dialog for the selected file (S2 #17): the documented
    /// shell entry point <c>rundll32 shell32.dll,OpenAs_RunDLL</c>.
    /// </summary>
    private void OnOpenWith(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;
        if (!File.Exists(row.Record.FullPath))
        {
            NoteText.Text = $"\"{row.Record.FileName}\" isn't at that location anymore (moved or deleted).";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("rundll32.exe",
                $"shell32.dll,OpenAs_RunDLL \"{row.Record.FullPath}\""));
        }
        catch (Exception ex)
        {
            _log.Warn($"Downloads window open-with failed: {ex.Message}");
            NoteText.Text = $"Couldn't open the Open-with dialog for \"{row.Record.FileName}\".";
        }
    }

    /// <summary>
    /// Delete the selected file from disk (S2 #17): confirmed first, and
    /// the history entry goes with it — a history row for a file the user
    /// just deleted from inside this window would only confuse.
    /// </summary>
    private void OnDeleteFile(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row)
            return;
        var invoker = FocusReturn.Capture();
        var confirm = System.Windows.MessageBox.Show(this,
            $"Delete \"{row.Record.FileName}\" from this PC?\n\n" +
            "The file is deleted (not sent to the Recycle Bin by this action — Windows deletes it directly) " +
            "and its entry is removed from this list. This can't be undone from ArenaDesktop.",
            "Delete downloaded file", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            FocusReturn.Restore(invoker);
            return;
        }
        try
        {
            if (File.Exists(row.Record.FullPath))
                File.Delete(row.Record.FullPath);
            DownloadHistoryStore.Remove(AppPaths.DownloadsHistoryPath, row.Record, _historyCap);
            _log.Info($"Downloaded file deleted from Downloads window: {row.Record.FileName}");
            NoteText.Text = $"Deleted \"{row.Record.FileName}\".";
        }
        catch (Exception ex)
        {
            _log.Warn($"Downloads window delete-file failed: {ex.Message}");
            NoteText.Text = $"Couldn't delete \"{row.Record.FileName}\" ({ex.Message}).";
        }
        Refresh();
    }

    /// <summary>
    /// Delete every selected file and its history entry in one go
    /// (enhancement pack 10).
    ///
    /// The single-file path asks "Delete "x" from this PC?" — fine for one
    /// file, useless for forty, where the user cannot see what they are
    /// about to lose. This confirmation names the count and the total size,
    /// and says how many of the files are already gone, so the number is
    /// about what will actually happen.
    ///
    /// One history write for the whole batch (<see cref="DownloadHistoryStore.RemoveMany"/>)
    /// rather than one per file, and per-file failures are collected and
    /// reported instead of aborting the rest — a file held open by another
    /// program must not block the other thirty-nine.
    /// </summary>
    private void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        var rows = DownloadsList.SelectedItems
            .OfType<Row>()          // the list only ever holds Row (see Refresh)
            .ToList();
        if (rows.Count < 2)
            return;

        var present = rows.Where(r => File.Exists(r.Record.FullPath)).ToList();
        var missing = rows.Count - present.Count;
        var bytes = present.Sum(r => r.Record.SizeBytes);
        var invoker = FocusReturn.Capture();
        var confirm = System.Windows.MessageBox.Show(this,
            $"Delete these {rows.Count} files from this PC?\n\n" +
            (present.Count > 0
                ? $"{present.Count} {(present.Count == 1 ? "file" : "files")} on disk ({DownloadText.FormatBytes(bytes)}) " +
                  "will be deleted permanently — not sent to the Recycle Bin. "
                : "") +
            (missing > 0 ? $"{missing} already {(missing == 1 ? "is" : "are")} missing. " : "") +
            "Their entries are removed from this list. This can't be undone from ArenaDesktop.",
            "Delete selected files", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            FocusReturn.Restore(invoker);
            return;
        }

        // Track failures by RECORD IDENTITY, not by file name: two
        // downloads of the same name into different folders are two
        // ordinary rows, and matching on the name would drop the entry of
        // a file that was never deleted.
        var failedKeys = new HashSet<(string, DateTimeOffset)>();
        var failedNames = new List<string>();
        foreach (var row in present)
        {
            try
            {
                File.Delete(row.Record.FullPath);
            }
            catch (Exception ex)
            {
                failedKeys.Add((row.Record.FullPath, row.Record.CompletedUtc));
                failedNames.Add(row.Record.FileName);
                _log.Warn($"Downloads window bulk delete failed for {row.Record.FileName}: {ex.Message}");
            }
        }
        // Only drop the entries whose file is actually gone; a failed
        // delete keeps its row so the user can retry it.
        var cleared = rows
            .Where(r => !failedKeys.Contains((r.Record.FullPath, r.Record.CompletedUtc)))
            .ToList();
        var removed = DownloadHistoryStore.RemoveMany(AppPaths.DownloadsHistoryPath, cleared.Select(r => r.Record), _historyCap);
        _log.Info($"Downloads window: bulk delete removed {removed} entries ({failedNames.Count} failed)");

        NoteText.Text = failedNames.Count == 0
            ? $"Deleted {removed} {(removed == 1 ? "entry" : "entries")}."
            : $"Deleted {removed}; {failedNames.Count} couldn't be deleted ({string.Join(", ", failedNames.Take(3))}{(failedNames.Count > 3 ? ", …" : "")}).";
        Refresh();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        var invoker = FocusReturn.Capture();
        var confirm = System.Windows.MessageBox.Show(this,
            "Clear the download history?\n\nThis only removes the list — the downloaded files themselves stay where they are.",
            "Clear download history", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            FocusReturn.Restore(invoker);
            return;
        }
        try
        {
            DownloadHistoryStore.Clear(AppPaths.DownloadsHistoryPath);
            _log.Info("Download history cleared");
        }
        catch (Exception ex)
        {
            _log.Warn($"Download history clear failed: {ex.Message}");
        }
        Refresh();
    }

    // ───────────────────────── active manager (S2 #13–15) ─────────────────────────

    private void RefreshActive()
    {
        if (_tracker is null)
        {
            ActiveList.ItemsSource = new List<ActiveRow>();
            UpdateActiveButtons();
            return;
        }
        var selectedId = SelectedActive?.Id;
        var now = DateTimeOffset.Now;
        var downloads = _tracker.Snapshot();
        // A displayed relative ETA changes with the clock even without tracker events.
        _etaTimer.IsEnabled = IsVisible && downloads.Any(download =>
            download.State == DownloadActivityState.InProgress && download.EstimatedEnd > now);
        var rows = downloads
            .Select(d => new ActiveRow
            {
                Id = d.Id,
                FileName = d.FileName,
                Detail = DownloadText.ActiveDetail(d, now),
                State = d.State,
                CanResume = d.CanResume,
            })
            .ToList();
        rows.AddRange(_tracker.SessionFailed.Select(f => new ActiveRow
        {
            Id = f.Id,
            FileName = f.FileName,
            Detail = "Failed this session — Retry starts the download again",
            State = DownloadActivityState.Failed,
            IsSessionFailed = true,
        }));
        ActiveList.ItemsSource = rows;
        if (selectedId is not null)
            ActiveList.SelectedItem = rows.FirstOrDefault(r => r.Id == selectedId);
        ActiveNote.Text = rows.Count == 0
            ? "Nothing is downloading right now."
            : $"{rows.Count} item(s) — Pause/Resume use WebView2's own download controls; Retry is available for this session's failures only.";
        UpdateActiveButtons();
    }

    private void UpdateActiveButtons()
    {
        var row = SelectedActive;
        PauseButton.IsEnabled = row is { State: DownloadActivityState.InProgress, IsSessionFailed: false };
        ResumeButton.IsEnabled = row is { State: DownloadActivityState.Paused, IsSessionFailed: false };
        CancelButton.IsEnabled = row is { IsSessionFailed: false } &&
                                  row.State is DownloadActivityState.InProgress or DownloadActivityState.Paused;
        RetryButton.IsEnabled = row is { IsSessionFailed: true } && _retryAsync is not null;
    }

    private void OnActiveSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateActiveButtons();

    private void OnPauseDownload(object sender, RoutedEventArgs e)
    {
        if (SelectedActive is { } row) _tracker?.Pause(row.Id);
        RefreshActive();
    }

    private void OnResumeDownload(object sender, RoutedEventArgs e)
    {
        if (SelectedActive is { } row) _tracker?.Resume(row.Id);
        RefreshActive();
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e)
    {
        if (SelectedActive is { } row) _tracker?.Cancel(row.Id);
        RefreshActive();
    }

    private async void OnRetryDownload(object sender, RoutedEventArgs e)
    {
        if (SelectedActive is not { IsSessionFailed: true } row || _retryAsync is null)
            return;
        RetryButton.IsEnabled = false;
        try
        {
            await _retryAsync(row.Id);
            NoteText.Text = $"Retrying \"{row.FileName}\" — watch the active list / status bar for progress.";
        }
        catch (Exception ex)
        {
            _log.Warn($"Retry download failed: {ex.Message}");
            NoteText.Text = $"Couldn't retry \"{row.FileName}\".";
        }
        RefreshActive();
    }
}
