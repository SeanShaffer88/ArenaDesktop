using System.Windows;
using System.Windows.Controls;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// Notification centre window (slice D3): the durable store, read/
/// unread, per-category muting (a settings change, saved by the
/// host). Thin over Core's NotificationStore.
/// </summary>
public partial class NotificationsWindow : Window
{
    private readonly NotificationStore _store;
    private readonly AppSettings _settings;
    private readonly Action _onSettingsChanged;

    public NotificationsWindow(NotificationStore store, AppSettings settings, Action onSettingsChanged)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;
        BuildCategoryToggles();
        Refresh();
    }

    private void BuildCategoryToggles()
    {
        foreach (var category in Enum.GetValues<NotificationCategory>())
        {
            var box = new System.Windows.Controls.CheckBox
            {
                Content = category.ToString(),
                IsChecked = NotificationStore.IsCategoryEnabled(_settings.MutedNotificationCategories, category),
                Margin = new Thickness(0, 0, 10, 0),
                Tag = category,
            };
            box.Click += OnCategoryToggled;
            System.Windows.Automation.AutomationProperties.SetName(box, $"Notifications for {category}");
            CategoryPanel.Children.Add(box);
        }
    }

    private void OnCategoryToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { Tag: NotificationCategory category } box) return;
        var muted = _settings.MutedNotificationCategories.ToList();
        if (box.IsChecked == true) muted.RemoveAll(m => m == category.ToString());
        else if (!muted.Contains(category.ToString())) muted.Add(category.ToString());
        _settings.MutedNotificationCategories = NotificationStore.SanitizeMuted(muted);
        _onSettingsChanged();
    }

    private void Refresh()
    {
        EntriesList.ItemsSource = _store.Entries.Select(en => new EntryRow(en)).ToList();
        UnreadText.Text = _store.UnreadCount == 0 ? "All read." : $"{_store.UnreadCount} unread";
    }

    private sealed record EntryRow(NotificationEntry Entry)
    {
        public override string ToString() =>
            $"{(Entry.Read ? "" : "● ")}{Entry.CreatedUtc.LocalDateTime:MM-dd HH:mm} [{Entry.Category}] {Entry.Title} — {Entry.Body}";
    }

    private void OnMarkAllRead(object sender, RoutedEventArgs e)
    {
        _store.MarkAllRead();
        Refresh();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _store.Clear();
        Refresh();
    }
}
