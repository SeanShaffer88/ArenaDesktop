using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// Scratch comparison window (slice B3): picks configured BYOK/local
/// targets, fans one prompt out through Core's ScratchCompare, shows
/// the replies side by side. Thin shell over Core + ChatClient.
/// </summary>
public partial class CompareWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ApiKeyVault _vault;
    private readonly DiagnosticLog _log;
    private readonly Action<string>? _onCompleted;
    private CancellationTokenSource? _runCts;
    private string _lastMarkdown = "";

    private sealed class TargetRow
    {
        public required ChatProviderConfig Provider { get; init; }
        public required System.Windows.Controls.CheckBox Include { get; init; }
        public required System.Windows.Controls.TextBox Model { get; init; }
    }

    private readonly List<TargetRow> _rows = [];

    public CompareWindow(AppSettings settings, ApiKeyVault vault, DiagnosticLog log, Action<string>? onCompleted = null)
    {
        InitializeComponent();
        _settings = settings;
        _vault = vault;
        _log = log;
        _onCompleted = onCompleted;
        BuildTargetRows();
        if (_settings.ChatProviders.Count == 0)
            StatusText.Text = "No providers yet — add one in AI providers (BYOK) first.";
    }

    private void BuildTargetRows()
    {
        var panel = new StackPanel();
        foreach (var provider in _settings.ChatProviders)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var include = new System.Windows.Controls.CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(include, 0);
            var name = new TextBlock { Text = provider.DisplayName, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            var model = new System.Windows.Controls.TextBox { Text = provider.DefaultModel };
            Grid.SetColumn(model, 2);
            row.Children.Add(include);
            row.Children.Add(name);
            row.Children.Add(model);
            panel.Children.Add(row);
            _rows.Add(new TargetRow { Provider = provider, Include = include, Model = model });
        }
        TargetList.ItemsSource = new[] { panel };
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        var prompt = PromptBox.Text.Trim();
        if (prompt.Length == 0) { StatusText.Text = "Type a prompt first."; return; }
        var targets = _rows.Where(r => r.Include.IsChecked == true)
            .Select(r => new CompareTarget(r.Provider.DisplayName, r.Provider, r.Model.Text.Trim()))
            .Where(t => t.Model.Length > 0)
            .ToList();
        if (targets.Count == 0) { StatusText.Text = "Tick at least one target with a model name."; return; }

        _runCts = new CancellationTokenSource();
        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = $"Asking {targets.Count} target(s)…";
        ResultsGrid.Children.Clear();
        try
        {
            using var client = new ChatClient(new HttpClientHandler());
            var results = await ScratchCompare.RunAsync(prompt, targets,
                (target, request, ct) => client.CompleteAsync(target.Provider, _vault.GetKey(target.Provider.Id), request, ct),
                _runCts.Token);
            foreach (var result in results) ResultsGrid.Children.Add(ResultCard(result));
            _lastMarkdown = ScratchCompare.RenderMarkdown(prompt, results);
            var ok = results.Count(r => r.Succeeded);
            StatusText.Text = $"Done — {ok} of {results.Count} answered.";
            _log.Info($"Scratch comparison finished: {ok}/{results.Count} targets answered");
            _onCompleted?.Invoke($"Scratch comparison finished ({ok} of {results.Count} answered)");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Stopped.";
        }
        finally
        {
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private Border ResultCard(CompareResult result)
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock
        {
            Text = $"{result.Target.Name} · {result.Target.Model} · {result.Elapsed.TotalSeconds:0.0}s",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new System.Windows.Controls.TextBox
        {
            Text = result.Succeeded ? result.Text : $"Failed: {result.Error}",
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 6, 0, 0),
        });
        return new Border
        {
            Child = panel,
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(4),
        };
    }

    private void OnStop(object sender, RoutedEventArgs e) => _runCts?.Cancel();

    private void OnCopyMarkdown(object sender, RoutedEventArgs e)
    {
        if (_lastMarkdown.Length == 0) { StatusText.Text = "Nothing to copy yet — run a comparison first."; return; }
        System.Windows.Clipboard.SetText(_lastMarkdown);
        StatusText.Text = "Comparison copied as Markdown.";
    }
}
