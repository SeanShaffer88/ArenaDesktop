using System.Windows;
using System.Windows.Input;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// Terminal panel (slice C): commands run as processes in the
/// project folder through the same runner as the Git panel, output
/// streamed live, Stop kills the process. Completion is reported to
/// the host (notification centre / status) via the callback. This is
/// a native shell surface — nothing here is reachable from the web
/// view, and commands are the user's own, typed by the user.
/// </summary>
public partial class TerminalWindow : Window
{
    private readonly IProcessRunner _runner;
    private readonly Func<string?> _folder;
    private readonly DiagnosticLog _log;
    private readonly Action<string>? _onCompleted;
    private CancellationTokenSource? _runCts;
    private ShellJob? _currentJob;

    /// <summary>Every job this window has run (the record the notification centre reads).</summary>
    public List<ShellJob> Jobs { get; } = [];

    public TerminalWindow(IProcessRunner runner, Func<string?> folder, DiagnosticLog log, Action<string>? onCompleted = null)
    {
        InitializeComponent();
        _runner = runner;
        _folder = folder;
        _log = log;
        _onCompleted = onCompleted;
        FolderText.Text = folder() is { } f
            ? $"Commands run in: {f}"
            : "No project folder chosen yet — choose one in the Git panel; commands run in your home folder until then.";
        CommandBox.Focus();
    }

    private void OnCommandKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnRun(this, new RoutedEventArgs()); e.Handled = true; }
    }

    /// <summary>Run a command handed in from outside (a palette script command, slice D5).</summary>
    public void RunExternalCommand(string text, string? folder)
    {
        CommandBox.Text = text;
        _ = RunAsync(text, folder);
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        var text = CommandBox.Text.Trim();
        if (text.Length == 0 || _runCts is not null) return;
        await RunAsync(text, null);
    }

    private async Task RunAsync(string text, string? folderOverride)
    {
        if (_runCts is not null) return;
        var folder = folderOverride ?? _folder() ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var job = new ShellJob { Id = Guid.NewGuid().ToString("n"), CommandText = text, WorkingFolder = folder };
        Jobs.Add(job);
        _currentJob = job;
        AppendLine($"\n> {text}");
        CommandBox.Text = "";
        _runCts = new CancellationTokenSource();
        StopButton.IsEnabled = true;
        var progress = new Progress<string>(line =>
        {
            job.AppendLine(line);
            AppendLine(line);
        });
        try
        {
            var result = await _runner.RunAsync(ShellJob.SpecFor(text, folder), progress, _runCts.Token);
            job.Finish(result.ExitCode, result.Cancelled);
        }
        catch (Exception ex)
        {
            job.AppendLine(ex.Message);
            job.Finish(-1, cancelled: false);
        }
        AppendLine($"[{job.Summary}]");
        _log.Info($"Terminal: {job.Summary}");
        _onCompleted?.Invoke(job.Summary);
        StopButton.IsEnabled = false;
        _runCts?.Dispose();
        _runCts = null;
    }

    private void AppendLine(string line)
    {
        OutputBox.AppendText(line + "\n");
        OutputBox.ScrollToEnd();
    }

    private void OnStop(object sender, RoutedEventArgs e) => _runCts?.Cancel();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _runCts?.Cancel();
        base.OnClosing(e);
    }
}
