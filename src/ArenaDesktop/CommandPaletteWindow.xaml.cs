using System.Windows;
using System.Windows.Input;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// The command palette (v0.10.0): Ctrl+K, type a few letters, Enter.
///
/// The catalogue, the grouping and the search rule are Core's
/// (<see cref="CommandPalette"/>) — the same prefix-match rule Settings,
/// Help and the shortcut list use — so this window only filters a list
/// and reports which command the user chose. It does NOT run anything:
/// the main window dispatches the id after the dialog closes, which
/// keeps every command on the same path as its menu item (and means a
/// command that opens a window can never do it while this one is still
/// modal).
///
/// The keys beside a row come from <see cref="ShortcutBindings"/>, so a
/// remapped Settings or Home key shows up here as the key that actually
/// works, not as the catalogue default.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    /// <summary>One row: the command, its printed keys (may be empty), and the group it belongs to.</summary>
    public sealed class PaletteRow
    {
        public required PaletteCommand Command { get; init; }
        public string Title => Command.Title;
        public string Group => Command.Group;
        public string Keys { get; init; } = "";
    }

    private readonly AppSettings _settings;

    /// <summary>Handlers wired in XAML fire during construction; nothing may touch half-built state before this is true.</summary>
    private bool _ready;

    /// <summary>The id the user chose, or null when the palette was dismissed.</summary>
    public string? SelectedCommandId { get; private set; }

    public CommandPaletteWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _ready = true;
        Refresh(null);
        Loaded += (_, _) =>
        {
            Query.Focus();
            Query.SelectAll();
        };
    }

    /// <summary>Recompute the list for what is in the box and select the first match, so Enter is always the obvious next key.</summary>
    private void Refresh(string? query)
    {
        var rows = CommandPalette.Search(query)
            .Select(c => new PaletteRow { Command = c, Keys = HintFor(c) })
            .ToList();
        Results.ItemsSource = rows;
        SummaryText.Text = CommandPalette.SummaryText(rows.Count, CommandPalette.All.Count, query);
        RunButton.IsEnabled = rows.Count > 0;
        if (rows.Count > 0)
            Results.SelectedIndex = 0;
    }

    /// <summary>The printed key for a command: its effective (possibly remapped) gesture, or nothing when it has no key of its own.</summary>
    private string HintFor(PaletteCommand command)
    {
        if (command.ShortcutId is null)
            return "";
        try
        {
            return ShortcutBindings
                .EffectiveGesture(command.ShortcutId, _settings.ShortcutOverrides)
                .DisplayText();
        }
        catch (ArgumentException)
        {
            // A catalogue id that no longer names a shortcut is a bug,
            // not a reason to fail the window: show the row without keys.
            return "";
        }
    }

    // ─────────────────────────── keys ───────────────────────────

    private void OnQueryChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_ready)
            return;
        Refresh(Query.Text);
    }

    /// <summary>Down/Up walk the list while the caret stays in the box — the whole point is never reaching for the mouse.</summary>
    private void OnQueryPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up))
            return;
        MoveSelection(e.Key == Key.Down ? 1 : -1);
        e.Handled = true;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelected();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        var count = Results.Items.Count;
        if (count == 0)
            return;
        Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + delta, 0, count - 1);
        Results.ScrollIntoView(Results.SelectedItem);
    }

    private void OnResultsSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem is not null)
            Results.ScrollIntoView(Results.SelectedItem);
    }

    private void OnResultsDoubleClick(object sender, MouseButtonEventArgs e) => RunSelected();

    private void OnRun(object sender, RoutedEventArgs e) => RunSelected();

    /// <summary>Choose the highlighted command and close; the main window runs it once this dialog is gone.</summary>
    private void RunSelected()
    {
        if (Results.SelectedItem is not PaletteRow row)
            return;
        SelectedCommandId = row.Command.Id;
        DialogResult = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
