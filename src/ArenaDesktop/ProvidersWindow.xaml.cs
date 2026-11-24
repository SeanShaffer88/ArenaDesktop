using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// BYOK provider management (completeness slice B1/B2): provider
/// configs live in settings; keys live only in the ApiKeyVault
/// (DPAPI-protected blobs in their own file). This window is a thin
/// editor over those two Core stores.
/// </summary>
public partial class ProvidersWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ApiKeyVault _vault;
    private readonly Action _onChanged;
    private readonly DiagnosticLog _log;
    private bool _populating;

    public ProvidersWindow(AppSettings settings, ApiKeyVault vault, Action onChanged, DiagnosticLog log)
    {
        InitializeComponent();
        _settings = settings;
        _vault = vault;
        _onChanged = onChanged;
        _log = log;
        RefreshList();
    }

    private ChatProviderConfig? Selected =>
        ProviderList.SelectedItem is ProviderRow row ? row.Config : null;

    private sealed record ProviderRow(ChatProviderConfig Config)
    {
        public override string ToString() =>
            $"{Config.DisplayName} — {Config.DefaultModel}".TrimEnd(' ', '—');
    }

    private void RefreshList()
    {
        var selectedId = Selected?.Id;
        ProviderList.ItemsSource = _settings.ChatProviders.Select(p => new ProviderRow(p)).ToList();
        ProviderList.SelectedItem = _settings.ChatProviders
            .FirstOrDefault(p => p.Id == selectedId) is { } match
            ? ProviderList.ItemsSource.Cast<ProviderRow>().First(r => r.Config == match)
            : ProviderList.ItemsSource.Cast<ProviderRow>().FirstOrDefault();
        Populate();
    }

    private void Populate()
    {
        _populating = true;
        try
        {
            var p = Selected;
            NameBox.Text = p?.DisplayName ?? "";
            BaseUrlBox.Text = p?.BaseUrl ?? "";
            ModelBox.Text = p?.DefaultModel ?? "";
            KeyBox.Password = "";
            KeyStatus.Text = p is null
                ? ""
                : p.RequiresKey
                    ? $"Key: {ChatProviderCatalog.MaskKey(_vault.GetKey(p.Id))}"
                    : "This provider needs no key.";
            ModelsStatus.Text = "";
            var enabled = p is not null;
            NameBox.IsEnabled = BaseUrlBox.IsEnabled = ModelBox.IsEnabled = enabled;
        }
        finally { _populating = false; }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Populate();

    /// <summary>Harvest the edit boxes into the selected config (on selection change / close).</summary>
    private void Harvest()
    {
        if (_populating || Selected is not { } p) return;
        p.DisplayName = NameBox.Text.Trim();
        p.BaseUrl = BaseUrlBox.Text.Trim();
        p.DefaultModel = ModelBox.Text.Trim();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Harvest();
        _settings.ChatProviders = ChatProviderCatalog.Sanitize(_settings.ChatProviders);
        _onChanged();
        base.OnClosing(e);
    }

    private void Add(ChatProviderKind kind)
    {
        Harvest();
        _settings.ChatProviders.Add(ChatProviderCatalog.Preset(kind));
        _settings.ChatProviders = ChatProviderCatalog.Sanitize(_settings.ChatProviders);
        _onChanged();
        RefreshList();
        ProviderList.SelectedIndex = ProviderList.Items.Count - 1;
    }

    private void OnAddOpenAi(object sender, RoutedEventArgs e) => Add(ChatProviderKind.OpenAI);
    private void OnAddAnthropic(object sender, RoutedEventArgs e) => Add(ChatProviderKind.Anthropic);
    private void OnAddGoogle(object sender, RoutedEventArgs e) => Add(ChatProviderKind.Google);
    private void OnAddOllama(object sender, RoutedEventArgs e) => Add(ChatProviderKind.Ollama);
    private void OnAddLmStudio(object sender, RoutedEventArgs e) => Add(ChatProviderKind.LmStudio);

    private void OnRemoveProvider(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        _settings.ChatProviders.Remove(p);
        _vault.RemoveKey(p.Id);
        _onChanged();
        RefreshList();
    }

    private void OnSaveKey(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        var key = KeyBox.Password.Trim();
        if (key.Length == 0) { KeyStatus.Text = "Paste a key first."; return; }
        try
        {
            _vault.SetKey(p.Id, key);
            KeyBox.Password = "";
            KeyStatus.Text = $"Key: {ChatProviderCatalog.MaskKey(_vault.GetKey(p.Id))}";
            _log.Info($"BYOK key saved for provider {p.DisplayName} (host only: {HostOf(p.BaseUrl)})");
        }
        catch (Exception ex)
        {
            KeyStatus.Text = $"Could not save the key: {ex.Message}";
        }
    }

    private void OnRemoveKey(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        _vault.RemoveKey(p.Id);
        KeyStatus.Text = "Key: not set";
    }

    private async void OnListModels(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        Harvest();
        ModelsStatus.Text = "Asking the provider…";
        try
        {
            using var client = new ChatClient(new HttpClientHandler());
            var models = await client.ListModelsAsync(p, _vault.GetKey(p.Id));
            ModelsStatus.Text = models.Count == 0
                ? "The provider returned no models."
                : $"Models: {string.Join(", ", models.Take(12))}{(models.Count > 12 ? $" … and {models.Count - 12} more" : "")}";
        }
        catch (Exception ex)
        {
            ModelsStatus.Text = ex.Message;
        }
    }

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "invalid URL";
}
