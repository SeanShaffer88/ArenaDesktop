using System.Windows;
using System.Windows.Controls;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>Editor for user script commands (slice D5). Edits settings in place; the host saves.</summary>
public partial class ScriptsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private bool _populating;
    private ScriptCommand? _editing;

    public ScriptsWindow(AppSettings settings, Action onChanged)
    {
        InitializeComponent();
        _settings = settings;
        _onChanged = onChanged;
        RefreshList();
    }

    private ScriptCommand? Selected =>
        ScriptList.SelectedItem is ScriptRow row ? row.Command : null;

    private sealed record ScriptRow(ScriptCommand Command)
    {
        public override string ToString() => $"{Command.Name} — {Command.Command}";
    }

    private void RefreshList()
    {
        var selectedId = Selected?.Id;
        ScriptList.ItemsSource = _settings.ScriptCommands.Select(c => new ScriptRow(c)).ToList();
        ScriptList.SelectedItem = ScriptList.ItemsSource.Cast<ScriptRow>()
            .FirstOrDefault(r => r.Command.Id == selectedId) ?? ScriptList.ItemsSource.Cast<ScriptRow>().FirstOrDefault();
        Populate();
    }

    private void Populate()
    {
        _populating = true;
        try
        {
            var c = Selected;
            _editing = c;
            NameBox.Text = c?.Name ?? "";
            CommandBox.Text = c?.Command ?? "";
            FolderBox.Text = c?.WorkingFolder ?? "";
        }
        finally { _populating = false; }
    }

    private void Harvest()
    {
        if (_populating || _editing is not { } c) return;
        c.Name = NameBox.Text.Trim();
        c.Command = CommandBox.Text.Trim();
        c.WorkingFolder = string.IsNullOrWhiteSpace(FolderBox.Text) ? null : FolderBox.Text.Trim();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Harvest(); // into the row being left (_editing), before Populate re-points it
        Populate();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Harvest();
        _settings.ScriptCommands = ScriptCommandCatalog.Sanitize(_settings.ScriptCommands);
        _onChanged();
        base.OnClosing(e);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        Harvest();
        _settings.ScriptCommands.Add(new ScriptCommand { Name = "New command", Command = "" });
        _settings.ScriptCommands = ScriptCommandCatalog.Sanitize(_settings.ScriptCommands);
        // Sanitize drops empty commands; add a placeholder that survives.
        if (_settings.ScriptCommands.All(c => c.Name != "New command"))
            _settings.ScriptCommands.Add(new ScriptCommand { Name = "New command", Command = "echo hello" });
        _onChanged();
        RefreshList();
        ScriptList.SelectedIndex = ScriptList.Items.Count - 1;
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } c) return;
        _settings.ScriptCommands.Remove(c);
        _onChanged();
        RefreshList();
    }
}
