using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using ArenaDesktop.Core;
// UseWindowsForms is on for the tray icon, so both frameworks are in scope.
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace ArenaDesktop;

/// <summary>
/// The window that replaced the old "see the log" message box (v0.13.0).
/// It shows what threw and where, offers the details on the clipboard, and
/// can open the folder the log lives in. Deliberately dependency-free: it
/// may be built while the app is in a state nobody designed for, so it takes
/// finished strings and does no work of its own beyond clipboard and shell.
/// </summary>
public partial class CrashWindow : Window
{
    private readonly string _package;
    private readonly string _logPath;
    private readonly Action? _onRestartSafeMode;

    public CrashWindow(string headline, string summary, string where, string logPath, string package,
        Action? onRestartSafeMode = null)
    {
        InitializeComponent();
        _package = package;
        _logPath = logPath;
        _onRestartSafeMode = onRestartSafeMode;
        if (onRestartSafeMode is not null)
            SafeModeButton.Visibility = Visibility.Visible;
        HeadlineText.Text = headline;
        SummaryText.Text = summary;
        WhereText.Text = where;
        LogPathText.Text = logPath;
        DetailsBox.Text = package;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        // Esc closes: a crash dialog must never become another thing to fight.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => CopyButton.Focus();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_package);
            CopiedNote.Text = "Copied.";
        }
        catch (Exception ex)
        {
            // The clipboard can be held by another process; say so rather
            // than pretending it worked. The details are in the box either
            // way, so the report is never lost.
            CopiedNote.Text = $"Could not copy ({ex.GetType().Name}) — select the text instead.";
        }
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = System.IO.Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            CopiedNote.Text = $"Could not open the folder ({ex.GetType().Name}).";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnRestartSafeMode(object sender, RoutedEventArgs e)
    {
        if (_onRestartSafeMode is null)
            return;
        Close();
        _onRestartSafeMode();
    }
}

/// <summary>
/// One place that knows how to turn an exception into <see cref="CrashWindow"/>
/// — and, if even that fails, back into the plain message box. A handler that
/// exists to survive a crash must not be able to cause one (v0.13.0).
/// </summary>
public static class CrashPresenter
{
    /// <summary>Show the crash report for <paramref name="exception"/>; never throws.</summary>
    public static void Show(Window? owner, DiagnosticLog log, Exception exception,
        string headline = "ArenaDesktop hit an unexpected error.", bool offerSafeMode = false)
    {
        string package;
        try
        {
            package = CrashReport.Package(
                Version(), RuntimeInformation.OSDescription, DateTimeOffset.Now, log.Path,
                exception, CrashReport.TailFromFile(log.Path));
        }
        catch
        {
            package = CrashReport.Summary(exception);
        }
        ShowPackage(owner, log, headline, CrashReport.Summary(exception), CrashReport.Where(exception), package,
            offerSafeMode);
    }

    /// <summary>
    /// Show a report built without a live exception — for the cases where
    /// constructing the window is what failed ("Settings could not open").
    /// </summary>
    public static void ShowText(Window? owner, DiagnosticLog log, string headline, string summary)
    {
        string package;
        try
        {
            package = CrashReport.PackageForText(
                Version(), RuntimeInformation.OSDescription, DateTimeOffset.Now, log.Path,
                summary, CrashReport.TailFromFile(log.Path));
        }
        catch
        {
            package = summary;
        }
        ShowPackage(owner, log, headline, summary, "", package);
    }

    private static void ShowPackage(Window? owner, DiagnosticLog log, string headline,
        string summary, string where, string package, bool offerSafeMode = false)
    {
        try
        {
            var window = new CrashWindow(headline, summary, where, log.Path, package,
                offerSafeMode ? RestartInSafeMode : null);
            if (owner is { IsLoaded: true } && !ReferenceEquals(owner, window))
                window.Owner = owner;
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            // Last resort: the message box from before, with the one fact that
            // makes a report worth reading.
            try
            {
                log.Error($"Crash dialog failed: {ex}");
                MessageBox.Show(
                    $"{headline}\n\n{summary}\n\nDetails were written to the log:\n{log.Path}",
                    "ArenaDesktop", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch
            {
                // Nothing left to try.
            }
        }
    }

    /// <summary>Relaunch with <c>--safe-mode</c> and close this instance (v0.14.0).</summary>
    private static void RestartInSafeMode()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;
            Process.Start(new ProcessStartInfo(exe, "--safe-mode") { UseShellExecute = true });
            System.Windows.Application.Current?.Shutdown();
        }
        catch
        {
            // Nothing to fall back to: the dialog is already gone, the log has
            // this failure, and the user can restart by hand.
        }
    }

    private static string Version() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? typeof(CrashPresenter).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";
}
