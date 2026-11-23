using System.Globalization;
using System.Windows;
using System.Windows.Input;
using ArenaDesktop.Core;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ArenaDesktop;

/// <summary>
/// The prompt pad (v0.9.0): a small window for one prompt at a time —
/// write or paste it, pick the Arena page, and "Copy &amp; open" puts the
/// text on the clipboard and brings that page forward. Pasting is the
/// user's own Ctrl+V; the app never types into Arena and never reads the
/// page, the same rule Battle Lab's copy button follows.
///
/// Everything the window knows lives in Core: <see cref="PromptPad"/>
/// decides what counts as the same prompt, how many are kept, what a
/// blank box does and what the status line says; <see cref="PromptPadStore"/>
/// owns the file. The window is only the surface — and the one thing it
/// deliberately does NOT own is the clipboard-clear timer, which the main
/// window keeps running after this window closes (see
/// <c>MainWindow.ArmPadClipboardClear</c>), so the opt-in "clear copied
/// prompts" setting keeps its promise for text copied here too.
/// </summary>
public partial class PromptPadWindow : Window
{
    /// <summary>One remembered prompt as the list shows it: the full text (for loading), a one-line preview, and when it was last used.</summary>
    public sealed class RecentRow
    {
        public string Text { get; init; } = "";
        public string Preview { get; init; } = "";
        public string When { get; init; } = "";
    }

    private readonly DiagnosticLog _log;
    private readonly Action<ArenaMode, string> _onCopied;
    private readonly PromptPadData _data;

    /// <summary>Handlers wired in XAML fire during construction; nothing may touch half-built state before this is true.</summary>
    private bool _ready;

    /// <summary>True once the prompt went to the clipboard: the draft it describes is gone, so Closing must not save it back.</summary>
    private bool _copied;

    public PromptPadWindow(DiagnosticLog log, Action<ArenaMode, string> onCopied)
    {
        InitializeComponent();
        _log = log;
        _onCopied = onCopied;
        _data = PromptPadStore.Load(AppPaths.PromptPadPath);
        ModeCombo.ItemsSource = ArenaModes.All;
        var target = PromptPad.TargetMode(_data);
        ModeCombo.SelectedItem = ArenaModes.All.FirstOrDefault(m => m.Id == target.Id) ?? ArenaModes.All[0];
        _ready = true;
        // v0.10.0: whatever was in the box when the pad last closed comes
        // back, so a half-written prompt is never lost to a stray Esc.
        Editor.Text = PromptPad.Draft(_data);
        Editor.CaretIndex = Editor.Text.Length;
        RefreshRecents();
        UpdateHint();
        UpdateCopyButton();
        // Closing is the one moment the box's contents are known final:
        // save then, not on every keystroke (prompt-pad.json is not a log).
        Closing += (_, _) => SaveDraft();
        Loaded += (_, _) =>
        {
            Editor.Focus();
            Editor.CaretIndex = Editor.Text.Length;
        };
    }

    /// <summary>
    /// Keep the unsent text for next time (v0.10.0). A draft that did not
    /// change writes nothing, and a copy clears it: the prompt it
    /// described is now in the remembered list, and reopening the pad
    /// with the same text twice would be a surprise, not a convenience.
    /// </summary>
    private void SaveDraft()
    {
        if (_copied)
            return;
        var text = (Editor.Text ?? "").Trim();
        if (string.Equals(text, _data.Draft, StringComparison.Ordinal))
            return;
        PromptPad.SetDraft(_data, text);
        Save();
    }

    private ArenaMode CurrentMode =>
        ModeCombo.SelectedItem as ArenaMode ?? PromptPad.TargetMode(_data);

    private void Save()
    {
        try
        {
            PromptPadStore.Save(AppPaths.PromptPadPath, _data);
        }
        catch (Exception ex)
        {
            // Never take the window down over a failed save: the prompt in
            // the box is what matters, and the user can still copy it.
            _log.Warn($"Prompt pad: saving remembered prompts failed: {ex.Message}");
        }
    }

    // ─────────────────────────── the box ───────────────────────────

    private void OnEditorTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_ready)
            return;
        UpdateHint();
    }

    /// <summary>The live line under the box: how big the prompt is, and what is still unfilled.</summary>
    private void UpdateHint()
    {
        var blanks = PromptBlanks.Extract(Editor.Text);
        var parts = new List<string>();
        if (blanks.Count > 0)
            parts.Add(blanks.Count == 1 ? "1 [blank] to fill" : $"{blanks.Count} [blanks] to fill");
        parts.Add($"{PromptPad.CharacterCount(Editor.Text.Length)} · Ctrl+Enter copies · Esc closes");
        HintText.Text = string.Join(" · ", parts);
        FillBlanksButton.Visibility = blanks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetHint(string text) => HintText.Text = text;

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopyAndOpen();
            e.Handled = true;
        }
    }

    // ─────────────────────────── the target page ───────────────────────────

    private void OnModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;
        if (ModeCombo.SelectedItem is ArenaMode mode)
        {
            PromptPad.WithMode(_data, mode.Id);
            Save();
        }
        UpdateCopyButton();
    }

    private void UpdateCopyButton()
    {
        var label = CurrentMode.Label;
        CopyButton.Content = $"Copy & open {label}";
        System.Windows.Automation.AutomationProperties.SetName(
            CopyButton, $"Copy the prompt and open {label}");
    }

    // ─────────────────────────── copying ───────────────────────────

    private void OnCopy(object sender, RoutedEventArgs e) => CopyAndOpen();

    /// <summary>
    /// Put the prompt on the clipboard and hand the page to the main
    /// window. The one rule is the same as Battle Lab's: the text that
    /// reaches the clipboard is exactly the text that was on screen, or
    /// nothing happens and the window says why.
    /// </summary>
    private void CopyAndOpen()
    {
        var text = (Editor.Text ?? "").Trim();
        if (!PromptPad.IsCopyable(text))
        {
            SetHint(PromptPad.EmptyStatus);
            return;
        }
        // A prompt long enough that Arena's own box may refuse it: say so
        // before the copy rather than letting the paste fail. The pad
        // does not truncate or refuse — it never decides for the user.
        if (PastedTextPolicy.NeedsWarning(text))
        {
            var answer = MessageBox.Show(this,
                PastedTextPolicy.WarningText(text) + "\n\nCopy it anyway?",
                "Long prompt — prompt pad", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                SetHint("Copy cancelled — nothing was put on the clipboard.");
                return;
            }
        }
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            _log.Warn($"Prompt pad: clipboard copy failed: {ex.Message}");
            SetHint("Copying to the clipboard failed — try again.");
            return;
        }
        var mode = CurrentMode;
        PromptPad.Remember(_data, text, DateTimeOffset.UtcNow);
        PromptPad.WithMode(_data, mode.Id);
        // The box is about to close with text still in it; that text is no
        // longer a draft, it is a remembered prompt (v0.10.0).
        PromptPad.SetDraft(_data, "");
        _copied = true;
        Save();
        _log.Info($"Prompt pad: copied {text.Length} characters and opened {mode.Label}");
        _onCopied(mode, text);
        Close();
    }

    // ─────────────────────────── remembered prompts ───────────────────────────

    private void RefreshRecents()
    {
        var rows = _data.Recents
            .Select(e => new RecentRow
            {
                Text = e.Text,
                Preview = PromptPad.Preview(e.Text),
                When = WhenText(e.UsedUtc),
            })
            .ToList();
        RecentsList.ItemsSource = rows;
        RecentsHeading.Text = PromptPad.RecentsSummary(rows.Count);
        RemoveButton.IsEnabled = rows.Count > 0;
        ClearButton.IsEnabled = rows.Count > 0;
    }

    /// <summary>"just now", "12 min ago", "14:32", "yesterday", "3 Nov" — readable, never a raw UTC stamp.</summary>
    private static string WhenText(DateTimeOffset usedUtc)
    {
        var local = usedUtc.ToLocalTime();
        var now = DateTimeOffset.Now;
        var delta = now - local;
        if (delta < TimeSpan.FromMinutes(1))
            return "just now";
        if (delta < TimeSpan.FromHours(1))
            return $"{(int)delta.TotalMinutes} min ago";
        if (local.Date == now.Date)
            return local.ToString("HH:mm", CultureInfo.CurrentCulture);
        if (local.Date == now.Date.AddDays(-1))
            return "yesterday";
        return local.ToString("d MMM", CultureInfo.CurrentCulture);
    }

    private void OnRecentDoubleClick(object sender, MouseButtonEventArgs e) => LoadSelectedRecent();

    private void OnRecentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        LoadSelectedRecent();
        e.Handled = true;
    }

    private void LoadSelectedRecent()
    {
        if (RecentsList.SelectedItem is not RecentRow row)
            return;
        Editor.Text = row.Text;
        Editor.CaretIndex = Editor.Text.Length;
        Editor.Focus();
        SetHint("Loaded that prompt into the box — Ctrl+Enter copies it.");
    }

    private void OnForgetRecent(object sender, RoutedEventArgs e)
    {
        if (RecentsList.SelectedItem is not RecentRow row)
            return;
        if (!PromptPad.Forget(_data, row.Text))
            return;
        Save();
        RefreshRecents();
        SetHint("Forgot that prompt — the text in the box is untouched.");
    }

    private void OnClearRecents(object sender, RoutedEventArgs e)
    {
        var count = _data.Recents.Count;
        if (count == 0)
            return;
        PromptPad.Clear(_data);
        Save();
        RefreshRecents();
        SetHint(count == 1
            ? "Forgot the remembered prompt — the text in the box is untouched."
            : $"Forgot all {count} remembered prompts — the text in the box is untouched.");
    }

    // ─────────────────────────── blanks ───────────────────────────

    /// <summary>
    /// Fill the prompt's [blanks] with the same dialog Battle Lab uses,
    /// then put the filled text back in the box — the user still presses
    /// Copy, so what they see is what will be pasted.
    /// </summary>
    private void OnFillBlanks(object sender, RoutedEventArgs e)
    {
        var blanks = PromptBlanks.Extract(Editor.Text);
        if (blanks.Count == 0)
            return;
        var dialog = new FillBlanksWindow("the prompt pad", blanks) { Owner = this };
        dialog.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(dialog);
        if (dialog.ShowDialog() != true)
            return;
        var filled = dialog.Fill(Editor.Text ?? "");
        Editor.Text = filled;
        Editor.CaretIndex = filled.Length;
        Editor.Focus();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
