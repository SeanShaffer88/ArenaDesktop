using System.Windows;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// The Diagnostics window (S8 #73/#74/#77/#78; S10 #89/#90/#93 made it
/// the full panel): a read-only report the main window's provider
/// composes on demand (runtime health, proxy, memory, startup
/// timings, file paths — and since S10 also architecture, Windows and
/// .NET versions, the active profile, the update channel, data sizes,
/// and the most recent errors, redacted). Refresh re-asks the
/// provider; Copy puts the report on the clipboard. The S10 actions
/// (export support bundle, open today's log, copy last error) are
/// optional delegates from the main window, shown only when supplied.
/// The field-pass launch (v0.2.0) is one more: the pass is modeless
/// and this window is modal, so the delegate closes this window and
/// the main window opens the pass once this one has unwound.
/// The mode-route check (v0.3.0) follows the same delegate pattern:
/// the main window owns the probing (and the log), this window owns
/// the button and keeps the returned section at the end of the
/// report across refreshes, so Copy report carries it too.
/// </summary>
public partial class DiagnosticsWindow : Window
{
    private readonly Func<string> _reportProvider;
    private readonly Action? _onRollback;
    private readonly Action? _onExportBundle;
    private readonly Action? _onOpenLog;
    private readonly Func<string?>? _lastErrorProvider;
    private readonly Action? _onFieldPass;
    private readonly Func<Task<string>>? _onCheckModeRoutes;
    private string? _routeSection;

    /// <summary>
    /// <paramref name="rollbackLabel"/> + <paramref name="onRollback"/>
    /// (v0.13.0, S9 #84) show the "Reinstall previous version" button
    /// when the updater has a usable kept installer; the owner window
    /// owns the confirm + launch + exit. The S10 delegates
    /// (<paramref name="onExportBundle"/>, <paramref name="onOpenLog"/>,
    /// <paramref name="lastErrorProvider"/>) light up the support
    /// actions row; the main window supplies all three.
    /// <paramref name="onFieldPass"/> (v0.2.0) lights up the Field pass
    /// launch in the same row; the main window supplies it.
    /// </summary>
    public DiagnosticsWindow(Func<string> reportProvider,
        string? rollbackLabel = null, Action? onRollback = null,
        Action? onExportBundle = null, Action? onOpenLog = null,
        Func<string?>? lastErrorProvider = null,
        Action? onFieldPass = null,
        Func<Task<string>>? onCheckModeRoutes = null)
    {
        _reportProvider = reportProvider;
        _onRollback = onRollback;
        _onExportBundle = onExportBundle;
        _onOpenLog = onOpenLog;
        _lastErrorProvider = lastErrorProvider;
        _onFieldPass = onFieldPass;
        _onCheckModeRoutes = onCheckModeRoutes;
        InitializeComponent();
        if (onCheckModeRoutes is not null)
            CheckRoutesButton.Visibility = Visibility.Visible;
        if (rollbackLabel is not null && onRollback is not null)
        {
            RollbackButton.Content = rollbackLabel;
            RollbackButton.Visibility = Visibility.Visible;
        }
        if (onFieldPass is not null)
            FieldPassButton.Visibility = Visibility.Visible;
        if (onExportBundle is not null)
            ExportBundleButton.Visibility = Visibility.Visible;
        if (onOpenLog is not null)
            OpenLogButton.Visibility = Visibility.Visible;
        if (lastErrorProvider is not null)
            CopyLastErrorButton.Visibility = Visibility.Visible;
        RefreshReport();
    }

    private void OnRollback(object sender, RoutedEventArgs e) => _onRollback?.Invoke();

    private void OnFieldPass(object sender, RoutedEventArgs e) => _onFieldPass?.Invoke();

    private void OnExportBundle(object sender, RoutedEventArgs e) => _onExportBundle?.Invoke();

    private void OnOpenLog(object sender, RoutedEventArgs e) => _onOpenLog?.Invoke();

    /// <summary>Copy just the newest error, redacted (S10 #93).</summary>
    private void OnCopyLastError(object sender, RoutedEventArgs e)
    {
        var last = _lastErrorProvider?.Invoke();
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
            CopyNote.Text = "Couldn't copy — the clipboard is busy.";
        }
    }

    private void RefreshReport()
    {
        try
        {
            ReportText.Text = _reportProvider();
        }
        catch (Exception ex)
        {
            ReportText.Text = $"Diagnostics couldn't be collected ({ex.Message}). The log has the details.";
        }
        // The mode-route section (v0.3.0) is collected on demand, not
        // by the provider — re-attach the latest one so a Refresh (or
        // a Copy) never silently drops it.
        if (_routeSection is not null)
            ReportText.Text += Environment.NewLine + Environment.NewLine + _routeSection;
    }

    /// <summary>
    /// Mode routes (v0.3.0): run the main window's probe and put its
    /// formatted section at the end of the report. The probing is
    /// reachability only — the delegate's contract is a finished
    /// section of text, so this window never sees a response body.
    /// </summary>
    private async void OnCheckModeRoutes(object sender, RoutedEventArgs e)
    {
        if (_onCheckModeRoutes is null)
            return;
        CheckRoutesButton.IsEnabled = false;
        CopyNote.Text = "Checking the mode routes…";
        try
        {
            _routeSection = await _onCheckModeRoutes();
            RefreshReport();
            ReportText.ScrollToEnd();
            CopyNote.Text = "Mode route check finished — the results are at the end of the report.";
        }
        catch (Exception ex)
        {
            CopyNote.Text = $"The mode route check failed ({ex.Message}). The log has the details.";
        }
        finally
        {
            CheckRoutesButton.IsEnabled = true;
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => RefreshReport();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ReportText.Text);
            CopyNote.Text = "Copied.";
        }
        catch
        {
            CopyNote.Text = "Couldn't copy — the clipboard is busy.";
        }
    }
}
