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
            _logText = "";
            AuditText.Text = $"Couldn't read the log file ({ex.Message}). It lives at {AppPaths.LogPath}.";
            FindingsText.Text = "";
            LogText.Text = "";
            return;
        }
        if (_logText.Length == 0)
        {
            AuditText.Text = "The log is empty right now — nothing has been written to it yet.";
            FindingsText.Text = "";
            LogText.Text = "";
            return;
        }
        LogText.Text = _logText;
        LogText.ScrollToEnd();
        // A refresh replaces the text under an existing query; re-run it
        // so the highlight and the count still describe what is on screen.
        if (!string.IsNullOrEmpty(FindBox.Text))
            RunSearch(FindBox.Text, forward: true);
        var lineCount = _logText.Split('\n').Length;
        var findings = RedactionAudit.Scan(_logText);
        AuditText.Text = RedactionAudit.Summarize(findings, lineCount);
        FindingsText.Text = findings.Count == 0
            ? ""
            : string.Join("\n", findings.Take(30).Select(f => $"Line {f.LineNumber}: {f.Description}.")) +
              (findings.Count > 30 ? $"\n…and {findings.Count - 30} more." : "");
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

    private void OnCopyRedacted(object sender, RoutedEventArgs e)
    {
        if (_logText.Length == 0)
            return;
        try
        {
            System.Windows.Clipboard.SetText(RedactionAudit.Redact(_logText));
            CopyNote.Text = "Copied — anything the check flagged was removed first.";
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
        var last = LogReading.LastError(_logText);
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
