using System.IO;
using System.Windows;
using System.Windows.Controls;
using ArenaDesktop.Core;
using Microsoft.Win32;

namespace ArenaDesktop;

/// <summary>
/// Library search & export (slice D2): a thin window over Core's
/// LibrarySearch/LibraryExport and the app's own stores, loaded
/// fresh from disk each time it opens.
/// </summary>
public partial class LibraryWindow : Window
{
    private readonly DiagnosticLog _log;
    private readonly BattleLabData _battleLab;
    private readonly PromptPadData _promptPad;
    private readonly List<DownloadRecord> _downloads;
    private readonly IReadOnlyList<LibraryEntry> _index;

    public LibraryWindow(DiagnosticLog log)
    {
        InitializeComponent();
        _log = log;
        _battleLab = BattleLabStore.Load(AppPaths.BattleLabPath);
        _promptPad = PromptPadStore.Load(AppPaths.PromptPadPath);
        _downloads = DownloadHistoryStore.Load(AppPaths.DownloadsHistoryPath, cap: 1000);
        _index = LibrarySearch.BuildIndex(_battleLab, _promptPad, _downloads);
        ShowResults(_index);
        SearchBox.Focus();
    }

    private void ShowResults(IReadOnlyList<LibraryEntry> results)
    {
        ResultsList.ItemsSource = results.Select(e => new ResultRow(e)).ToList();
        CountText.Text = $"{results.Count} of {_index.Count} entries";
    }

    private sealed record ResultRow(LibraryEntry Entry)
    {
        public override string ToString() =>
            $"[{Entry.SourceName}] {Entry.Title}{(Entry.WhenUtc is { } w ? $" — {w:yyyy-MM-dd}" : "")}";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) =>
        ShowResults(LibrarySearch.Search(_index, SearchBox.Text));

    private void Export(bool json)
    {
        var document = LibraryExport.Build(_battleLab, _promptPad, _downloads);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = json ? "arenadesktop-library.json" : "arenadesktop-library.md",
            Filter = json ? "JSON (*.json)|*.json" : "Markdown (*.md)|*.md",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, json ? LibraryExport.ToJson(document) : LibraryExport.ToMarkdown(document));
            _log.Info($"Library exported ({(json ? "JSON" : "Markdown")})");
        }
        catch (Exception ex)
        {
            CountText.Text = $"Export failed: {ex.Message}";
        }
    }

    private void OnExportJson(object sender, RoutedEventArgs e) => Export(json: true);
    private void OnExportMarkdown(object sender, RoutedEventArgs e) => Export(json: false);
}
