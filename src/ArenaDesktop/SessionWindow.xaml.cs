using System.Text;
using System.Windows;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// This session (enhancement pack): what the running app has actually
/// done — pages visited, time on Arena, mode jumps, downloads finished,
/// prompts copied — plus the reading time per mode. The numbers come
/// from Core's <see cref="SessionStats"/>, which lives in memory for
/// the life of the process: nothing is persisted, nothing is sent
/// anywhere, and nothing is read from the Arena page. "Copy summary"
/// puts the same lines on the clipboard for a bug report.
/// </summary>
public partial class SessionWindow : Window
{
    /// <summary>One row of the by-mode table.</summary>
    public sealed record ModeRow(string Label, string Duration);

    private readonly SessionStats _stats;
    private readonly string _annotation;

    public SessionWindow(SessionStats stats, string? annotation = null)
    {
        _stats = stats;
        _annotation = annotation ?? "";
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        if (_annotation.Length > 0)
        {
            AnnotationText.Text = _annotation;
            AnnotationText.Visibility = Visibility.Visible;
        }
        var lines = _stats.SummaryLines(DateTimeOffset.UtcNow);
        // The first three lines carry the counts; the last is the
        // privacy reminder, which the window's own subtitle already
        // says, so only the facts go in the big block.
        SummaryText.Text = string.Join(Environment.NewLine, lines.Take(Math.Min(3, lines.Count)));
        var byMode = _stats.TimeByMode();
        ModeList.ItemsSource = byMode
            .Select(t => new ModeRow(t.Label, SessionStats.FormatDuration(t.Duration)))
            .ToList();
        ModeHeadingText.Visibility = byMode.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = true;
    }

    /// <summary>The clipboard text: the same lines the window shows, in plain text.</summary>
    internal string SummaryTextForClipboard()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"ArenaDesktop — this session ({_stats.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm})");
        foreach (var line in _stats.SummaryLines(DateTimeOffset.UtcNow))
            builder.AppendLine(line);
        var byMode = _stats.TimeByMode();
        if (byMode.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Reading time by mode:");
            foreach (var entry in byMode)
                builder.AppendLine($"  {entry.Label}: {SessionStats.FormatDuration(entry.Duration)}");
        }
        if (_annotation.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine(_annotation);
        }
        return builder.ToString().TrimEnd();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            // Fully qualified: the project sets UseWindowsForms (the tray
            // needs it), so a bare Clipboard is ambiguous between the WPF
            // and WinForms types.
            System.Windows.Clipboard.SetText(SummaryTextForClipboard());
            CopyButton.Content = "Copied";
        }
        catch
        {
            // The clipboard can be held by another app; the summary is
            // still on screen, so this is never worth an error dialog.
            CopyButton.Content = "Couldn't copy";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
