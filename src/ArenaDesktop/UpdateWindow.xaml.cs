using System.Diagnostics;
using System.Windows;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// The update window (v0.13.0, S9 #81/#83): download the new installer
/// in-app with progress, verify its SHA-256 against the release's
/// SHA256SUMS.txt, and launch it — only ever on the user's click.
/// Nothing here is automatic before that click: no auto-download, no
/// auto-launch, and a hash mismatch deletes the file with a plain
/// explanation. Since the seamless-update port the click is the LAST
/// one: Install launches the installer silently (/S) and the
/// installer itself closes the app (the --exit-for-update handshake),
/// installs, and reopens it — this window deliberately does not exit
/// the app itself, so the installer's running-instance detection
/// still sees the app and knows to relaunch it. Snooze
/// (tomorrow / next week) also lives here, writing the timestamp the
/// Core schedule consults before any automatic reminder.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly GitHubRelease _release;
    private readonly AppSettings _settings;
    private readonly DiagnosticLog _log;
    private readonly Action _persistSettings;
    private CancellationTokenSource? _downloadCts;
    private string? _verifiedInstallerPath;

    public UpdateWindow(GitHubRelease release, AppSettings settings, DiagnosticLog log,
        Action persistSettings)
    {
        _release = release;
        _settings = settings;
        _log = log;
        _persistSettings = persistSettings;
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        HeadlineText.Text = $"ArenaDesktop {_release.Tag} is available";
        // v0.3.0: show what changed in the offered version before the
        // user decides — the notes rode in on the check's release
        // payload. No notes (empty body, older flow) = the window
        // looks exactly as it did before they existed. The notes
        // themselves are never logged, only that they were there.
        var notes = ReleaseNotesFormatter.Format(_release.Notes);
        if (notes.Length > 0)
        {
            NotesHeadingText.Visibility = Visibility.Visible;
            NotesText.Visibility = Visibility.Visible;
            NotesText.Text = notes;
        }
        _log.Info($"Update window: {_release.Tag} offered, release notes {(notes.Length > 0 ? "shown" : "not available")}");
        if (_settings.UpdateSnoozedUntilUtc is { } until && DateTimeOffset.UtcNow < until)
            SnoozeNoteText.Text = UpdateSchedule.DescribeSnooze(until);
        Closing += (_, _) => _downloadCts?.Cancel();
    }

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        DownloadButton.Visibility = Visibility.Collapsed;
        SnoozeDayButton.Visibility = Visibility.Collapsed;
        SnoozeWeekButton.Visibility = Visibility.Collapsed;
        CancelDownloadButton.Visibility = Visibility.Visible;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.IsIndeterminate = true;
        StatusText.Text = "Downloading update…";
        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<string>(text =>
        {
            StatusText.Text = text;
            var pctAt = text.IndexOf('%');
            if (pctAt > 0)
            {
                var start = pctAt;
                while (start > 0 && char.IsDigit(text[start - 1]))
                    start--;
                if (start < pctAt && int.TryParse(text.AsSpan(start, pctAt - start), out var pct))
                {
                    DownloadProgress.IsIndeterminate = false;
                    DownloadProgress.Value = pct;
                }
            }
        });
        var result = await UpdateService.DownloadAndVerifyAsync(
            _release, AppPaths.UpdatesDir, _log, progress, _downloadCts.Token);
        CancelDownloadButton.Visibility = Visibility.Collapsed;
        DownloadProgress.Visibility = Visibility.Collapsed;
        switch (result.Outcome)
        {
            case UpdateDownloadOutcome.Verified:
                _verifiedInstallerPath = result.InstallerPath;
                StatusText.Text = "Downloaded and verified — the installer's fingerprint matches the " +
                                  "fingerprint published with the release. Choose Install when you're ready: " +
                                  "ArenaDesktop will close and the installer will update it in place " +
                                  "(your sign-in and settings carry over).";
                InstallButton.Visibility = Visibility.Visible;
                break;
            default:
                StatusText.Text = result.Message ?? "The update couldn't be downloaded.";
                DownloadButton.Visibility = Visibility.Visible;
                SnoozeDayButton.Visibility = Visibility.Visible;
                SnoozeWeekButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e) => _downloadCts?.Cancel();

    private void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_verifiedInstallerPath is null)
            return;
        var newVersion = UpdateInfo.ParseTag(_release.Tag)?.ToString(3) ?? _release.Tag;
        // S9 #84: rotate the kept installers BEFORE the installer takes
        // over — after it starts, this process is on its way out.
        UpdateService.PrepareRollback(_settings, AppPaths.UpdatesDir, newVersion, _verifiedInstallerPath, _log);
        _persistSettings();
        // Seamless update: the status line goes up BEFORE the
        // installer starts — it is the last thing the user sees from
        // this version, and it says exactly what is about to happen.
        InstallButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "Updating — ArenaDesktop will close and reopen on the new version.";
        try
        {
            // Silent (/S): no wizard. The installer detects the
            // running instance (process check), asks it to close via
            // --exit-for-update, installs, and relaunches it. This
            // process must NOT exit first: if it did, the installer
            // would find nothing running and would skip the relaunch.
            // This window closes when the app does.
            Process.Start(new ProcessStartInfo(_verifiedInstallerPath)
            {
                UseShellExecute = true,
                Arguments = "/S",
            });
            _log.Info($"Update installer launched silently ({_release.Tag}) — it will close ArenaDesktop and reopen it on the new version");
        }
        catch (Exception ex)
        {
            _log.Warn($"Launching the update installer failed: {ex.Message}");
            StatusText.Text = "The installer couldn't be started. The verified installer is kept in the " +
                              $"updates folder ({AppPaths.UpdatesDir}) — you can run it from there.";
            InstallButton.Visibility = Visibility.Visible;
            return;
        }
    }

    private void OnSnoozeTomorrow(object sender, RoutedEventArgs e) => Snooze(UpdateSnooze.Tomorrow);

    private void OnSnoozeNextWeek(object sender, RoutedEventArgs e) => Snooze(UpdateSnooze.NextWeek);

    private void Snooze(UpdateSnooze choice)
    {
        var until = UpdateSchedule.SnoozeUntil(DateTimeOffset.UtcNow, choice);
        _settings.UpdateSnoozedUntilUtc = until;
        _persistSettings();
        _log.Info($"Update reminders snoozed until {until:yyyy-MM-dd}");
        Close();
    }

    private void OnOpenReleasePage(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_release.PageUrl) { UseShellExecute = true }); }
        catch { /* no default browser — nothing sensible to do */ }
    }
}
