using System.Windows;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// The keyboard shortcuts window (v0.10.0, S6 #52): Ctrl+/ (or "?")
/// opens this searchable list of every shortcut the shell binds. The
/// rows come from Core's <see cref="ShortcutCatalog"/> — the single
/// source of truth — with the user's remappings (#53) applied, so what
/// the window shows is what the keys actually do. Remappable rows say
/// where they can be changed (Settings → Keyboard &amp; accessibility).
/// </summary>
public partial class ShortcutsWindow : Window
{
    /// <summary>
    /// One display row (enhancement pack: it carries the catalog id and its
    /// group, so rows can be listed by area). GroupHeading is the area's
    /// title on the first row of each group and empty on the rest, which is
    /// how the one flat list shows its sections without a group template.
    /// </summary>
    public sealed record Row(string Id, string Name, string Keys, string Kind, string Group, string GroupHeading = "");

    private readonly List<Row> _allRows = new();

    public ShortcutsWindow(AppSettings settings)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        ShellTextScaling.Apply(this, settings.ShellFontScale);

        var effective = ShortcutBindings.Effective(settings.ShortcutOverrides);
        foreach (var action in ShortcutCatalog.All)
        {
            if (action.Remappable && effective.TryGetValue(action.Id, out var gesture))
            {
                _allRows.Add(new Row(
                    action.Id,
                    action.Name,
                    gesture.DisplayText(),
                    "Can be changed in Settings",
                    ShortcutCheatSheet.GroupFor(action)));
            }
            else
            {
                _allRows.Add(new Row(action.Id, action.Name, action.DisplayGestures, "Fixed",
                    ShortcutCheatSheet.GroupFor(action)));
            }
        }
        ApplyFilter();
    }

    /// <summary>
    /// Enhancement pack: filtering goes through Core's
    /// <see cref="ShortcutCheatSheet"/>, so the search matches names,
    /// KEY TEXT and group titles with the same whole-word prefix rule
    /// Settings and Help use — and the rows are listed area by area, in
    /// Core's order, instead of one long run.
    /// </summary>
    private void ApplyFilter()
    {
        var query = SearchText.Text;
        var byId = _allRows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var rows = new List<Row>();
        foreach (var (group, actions) in ShortcutCheatSheet.Grouped(query))
        {
            var first = true;
            foreach (var action in actions)
            {
                if (!byId.TryGetValue(action.Id, out var row))
                    continue;
                rows.Add(first ? row with { GroupHeading = group } : row);
                first = false;
            }
        }
        ShortcutList.ItemsSource = rows;
        SummaryText.Text = string.IsNullOrWhiteSpace(query)
            ? "Every shortcut ArenaDesktop itself binds, grouped by area. (Keys pressed inside the Arena page — like Enter to send — are Arena's own.) Three of these can be changed, in Settings → Keyboard & accessibility."
            : rows.Count == 0
                ? "No shortcut matches that search."
                : $"{rows.Count} of {_allRows.Count} shortcuts match. Click a row to copy its keys.";
    }

    /// <summary>Enhancement pack: clicking a row copies "Name: keys" — the quickest way to answer "how do I…?" without retyping.</summary>
    private void OnCopyRow(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ShortcutList.SelectedItem is not Row row)
            return;
        try
        {
            Clipboard.SetText($"{row.Name}: {row.Keys}");
            SummaryText.Text = $"Copied \u201c{row.Name}: {row.Keys}\u201d.";
        }
        catch
        {
            SummaryText.Text = "Couldn't copy — the clipboard is busy.";
        }
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ApplyFilter();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
