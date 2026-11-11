using System.Diagnostics;
using System.IO;
using System.Windows;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// The in-app log viewer (v0.11.0, S7 #66): a read-only tail of the
/// diagnostic log plus the redaction audit run over exactly what's on
/// screen. The audit is the honest part — the app's logging rules say
/// addresses are host+path only and no tokens are ever written, and
/// this window PROVES it on the real file (or says plainly that it
/// found something, by line number). "Copy redacted log" copies the
/// text with flagged spans removed, so a log can be pasted into a bug
/// report without hand-scrubbing.
/// </summary>
public partial class LogViewerWindow : Window
{
    private string _logText = "";
    // Hardening pack: find state. The matches are recomputed only when the
    // query changes; Next/Previous just move the index, so a 1 MB log is
    // scanned once per keystroke rather than once per press.
    private IReadOnlyList<LogSearch.Match> _matches = Array.Empty<LogSearch.Match>();
    private int _matchIndex = -1;
    private string _lastQuery = "";

    /// <summary>
    /// Levels the filter is showing (enhancement pack 10). Empty means
    /// "everything", which is how the viewer opens — the filter is a
    /// narrowing tool, never something the user has to undo to see their
    /// log back.
    /// </summary>
    private readonly HashSet<LogLevel> _visibleLevels = new();

    /// <summary>Set while <see cref="ResyncChipsToShowEverything"/> writes the boxes, so its own writes aren't treated as clicks.</summary>
    private bool _syncingChips;

    /// <summary>The full text as read, before the level filter. Find runs against this, then the visible view.</summary>
    private string _fullLogText = "";

    public LogViewerWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        CopyNote.Text = "";
        try
        {
            _logText = File.Exists(AppPaths.LogPath)
                ? File.ReadAllText(AppPaths.LogPath)
                : "";
        }
        catch (Exception ex)
        {
            ResetToEmpty();
            AuditText.Text = $"Couldn't read the log file ({ex.Message}). It lives at {AppPaths.LogPath}.";
            FindingsText.Text = "";
            return;
        }
        if (_logText.Length == 0)
        {
            ResetToEmpty();
            AuditText.Text = "The log is empty right now — nothing has been written to it yet.";
            FindingsText.Text = "";
            return;
        }
        // _fullLogText is the file as read and _logText is what the level
        // filter leaves visible. The audit always scans the FULL text: a
        // redaction finding must not disappear because the user filtered
        // that line's level out.
        _fullLogText = _logText;
        RebuildLevelChips();
        ApplyLevelFilter(scrollToEnd: true);
        var lineCount = _fullLogText.Split('\n').Length;
        var findings = RedactionAudit.Scan(_fullLogText);
        AuditText.Text = RedactionAudit.Summarize(findings, lineCount);
        FindingsText.Text = findings.Count == 0
            ? ""
            : string.Join("\n", findings.Take(30).Select(f => $"Line {f.LineNumber}: {f.Description}.")) +
              (findings.Count > 30 ? $"\n…and {findings.Count - 30} more." : "");
    }

    // ─────────────────────── level filter (enhancement pack 10) ───────────────────────

    /// <summary>
    /// Build one toggle per level, with its count. Rebuilt on every reload
    /// so the numbers describe the file that is on screen; the user's
    /// current selection is preserved by matching on the level itself, not
    /// on the control.
    /// </summary>
    private void RebuildLevelChips()
    {
        if (LevelChips is null)
            return;
        LevelChips.Children.Clear();
        LevelChips.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Show",
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new System.Windows.Thickness(0, 0, 8, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
        });
        var counts = LogLevelFilter.Counts(_fullLogText);
        foreach (var level in LogLevelFilter.Selectable)
        {
            var count = counts.TryGetValue(level, out var n) ? n : 0;
            var chip = new System.Windows.Controls.CheckBox
            {
                // Nothing selected means everything is shown, so an empty
                // selection must not render as "no levels ticked".
                IsChecked = _visibleLevels.Count == 0 || _visibleLevels.Contains(level),
                Content = $"{LogLevelFilter.Label(level)} ({count})",
                Margin = new System.Windows.Thickness(0, 0, 12, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Tag = level,
                // A level with nothing in it stays clickable. Disabling it
                // would leave a CHECKED but dead box whenever the filter is
                // unset (every box renders ticked then), so the user would
                // see a control they cannot switch off. It is harmless to
                // tick: the chip produces no lines either way, and
                // OnLevelChipChanged still treats "every box ticked" as
                // show-everything.
                IsEnabled = true,
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, $"{LogLevelFilter.Label(level)}, {count} lines");
            chip.Checked += OnLevelChipChanged;
            chip.Unchecked += OnLevelChipChanged;
            LevelChips.Children.Add(chip);
        }
    }

    /// <summary>
    /// A chip was toggled. "All four ticked" is stored as an empty
    /// selection — the same state as "none ticked" would otherwise be,
    /// except that none-ticked must never mean an empty log. Rather than
    /// choosing between those, the rule is: the filter is the set of
    /// levels to SHOW, and ticking everything resets to show-everything.
    /// </summary>
    private void OnLevelChipChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingChips)
            return;
        _visibleLevels.Clear();
        var ticked = 0;
        var total = 0;
        foreach (var child in LevelChips.Children.OfType<System.Windows.Controls.CheckBox>())
        {
            if (child.Tag is not LogLevel level)
                continue;
            total++;
            if (child.IsChecked == true)
            {
                _visibleLevels.Add(level);
                ticked++;
            }
        }
        // All four ticked and none ticked both describe "show everything":
        // an empty view is never a useful answer to a click, and the second
        // case is reachable by unticking the last box.
        if (ticked == total || ticked == 0)
        {
            _visibleLevels.Clear();
            if (ticked == 0)
                ResyncChipsToShowEverything();
        }
        ApplyLevelFilter(scrollToEnd: false);
    }

    /// <summary>
    /// After "untick the last box", put every box back to ticked — the
    /// state that actually matches what is on screen. Without this the
    /// chips would all read unchecked while the full log is displayed.
    /// </summary>
    private void ResyncChipsToShowEverything()
    {
        _syncingChips = true;
        foreach (var chip in LevelChips.Children.OfType<System.Windows.Controls.CheckBox>())
            chip.IsChecked = true;
        _syncingChips = false;
    }

    /// <summary>
    /// Show the lines whose level is selected. The full text stays in
    /// memory (<see cref="_fullLogText"/>) so turning a chip back on is
    /// instant and the redaction audit never has to re-read the file.
    /// </summary>
    private void ApplyLevelFilter(bool scrollToEnd)
    {
        var text = _visibleLevels.Count == 0
            ? _fullLogText
            : LogLevelFilter.Apply(_fullLogText, _visibleLevels);
        _logText = text;
        LogText.Text = text;
        if (scrollToEnd)
            LogText.ScrollToEnd();
        // The filtered view changes what find searches, so re-run it —
        // otherwise a match count could describe lines that are no longer
        // on screen.
        if (!string.IsNullOrEmpty(FindBox.Text))
            RunSearch(FindBox.Text, forward: true);
    }

    // ───────────────────────── find (hardening pack) ─────────────────────────

    /// <summary>
    /// Ctrl+F moves to the find box, the same key the page's own finder
    /// uses — the log viewer is a text surface, and a reader's hands
    /// expect the same chord. F3 repeats the last search, and Escape in
    /// the box (handled there) clears it.
    /// </summary>
    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F
            && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            FindBox.Focus();
            FindBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.F3 && _matches.Count > 0)
        {
            StepSearch(forward: (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0);
            e.Handled = true;
        }
    }

    private void OnFindChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        RunSearch(FindBox.Text, forward: true);

    private void OnFindKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            StepSearch(forward: (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0);
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            // Escape in the find box clears the search rather than closing
            // the window — the window's Close button is the way out.
            FindBox.Text = "";
            LogText.Focus();
            e.Handled = true;
        }
    }

    private void OnFindNext(object sender, RoutedEventArgs e) => StepSearch(forward: true);

    private void OnFindPrevious(object sender, RoutedEventArgs e) => StepSearch(forward: false);

    /// <summary>
    /// Recompute the match set for <paramref name="query"/> and select a
    /// match. Typing starts from the top of the log, which is where a
    /// reader is looking when they begin a search; pressing Enter or the
    /// arrows moves from there.
    /// </summary>
    private void RunSearch(string? query, bool forward)
    {
        if (!string.Equals(query ?? "", _lastQuery, StringComparison.Ordinal))
        {
            _lastQuery = query ?? "";
            _matches = LogSearch.FindAll(_logText, _lastQuery);
            _matchIndex = -1;
        }
        if (_matches.Count == 0)
        {
            FindStatus.Text = LogSearch.Describe(0, -1);
            LogText.Select(0, 0);
            return;
        }
        _matchIndex = forward ? 0 : LogSearch.Step(_matches.Count, -1, forward: false);
        SelectCurrent();
    }

    private void StepSearch(bool forward)
    {
        if (_matches.Count == 0)
        {
            // Enter on an empty result set re-runs the search, so a refresh
            // that added the line the user was looking for is picked up.
            RunSearch(FindBox.Text, forward);
            return;
        }
        _matchIndex = LogSearch.Step(_matches.Count, _matchIndex, forward);
        SelectCurrent();
    }

    private void SelectCurrent()
    {
        if (_matchIndex < 0 || _matchIndex >= _matches.Count)
            return;
        var match = _matches[_matchIndex];
        LogText.Select(match.Index, match.Length);
        LogText.ScrollToLine(LogText.GetLineIndexFromCharacterIndex(match.Index));
        FindStatus.Text = LogSearch.Describe(_matches.Count, _matchIndex);
        // Selecting the match is what moves the caret; without focus the
        // highlight is drawn but the reader cannot continue from it.
        LogText.Focus();
    }

    /// <summary>Clear every view when there is no log to show, so a stale filter cannot linger over a stale count.</summary>
    private void ResetToEmpty()
    {
        _logText = "";
        _fullLogText = "";
        _visibleLevels.Clear();
        LogText.Text = "";
        if (LevelChips is not null)
            LevelChips.Children.Clear();
    }

    private void OnCopyRedacted(object sender, RoutedEventArgs e)
    {
        // The FULL log, not the filtered view: this button's job is "give
        // me something I can paste into a bug report", and a report missing
        // every line the user happened to filter out is worse than useless.
        // The level filter narrows what is on screen; it does not decide
        // what gets attached.
        if (_fullLogText.Length == 0)
            return;
        try
        {
            System.Windows.Clipboard.SetText(RedactionAudit.Redact(_fullLogText));
            CopyNote.Text = _visibleLevels.Count > 0
                ? "Copied the whole log (all levels) — anything the check flagged was removed first."
                : "Copied — anything the check flagged was removed first.";
        }
        catch
        {
            CopyNote.Text = "Couldn't copy — the clipboard is busy. Try again.";
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload();

    /// <summary>Copy just the newest error, redacted (S10 #93) — the one line a bug report usually needs.</summary>
    private void OnCopyLastError(object sender, RoutedEventArgs e)
    {
        // The newest error in the FILE, not in the filtered view: with the
        // Errors chip off, searching the visible text would report "no
        // errors" about a log full of them.
        var last = LogReading.LastError(_fullLogText);
        if (last is null)
        {
            CopyNote.Text = "No errors in the current log — nothing to copy.";
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(last);
            CopyNote.Text = "Copied the most recent error (redacted).";
        }
        catch
        {
            CopyNote.Text = "Couldn't copy — the clipboard is busy. Try again.";
        }
    }

    /// <summary>Open the log file itself in the default text editor (S10 #93).</summary>
    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(AppPaths.LogPath))
            {
                CopyNote.Text = "There's no log file yet — it appears once something is written to it.";
                return;
            }
            Process.Start(new ProcessStartInfo(AppPaths.LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CopyNote.Text = $"Couldn't open the log file ({ex.Message}). It lives at {AppPaths.LogPath}.";
        }
    }
}
