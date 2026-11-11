using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ArenaDesktop.Core;
using TextBox = System.Windows.Controls.TextBox;

namespace ArenaDesktop;

/// <summary>
/// Fill-in-the-blanks (Battle Lab, v0.2.0): one text field per
/// <c>[blank]</c> in the prompt, shown at copy time. Fields left
/// empty keep their placeholder in the copied text — the copy is
/// never silently different from what the user saw.
/// </summary>
public partial class FillBlanksWindow : Window
{
    private readonly List<(string Name, TextBox Field)> _fields = new();

    public FillBlanksWindow(string promptName, IReadOnlyList<string> blanks)
    {
        InitializeComponent();
        Title = $"Fill in the blanks — {promptName}";
        foreach (var blank in blanks)
        {
            var label = new TextBlock
            {
                Text = $"[{blank}]",
                Margin = new Thickness(0, 10, 0, 0),
                FontWeight = FontWeights.SemiBold,
            };
            var field = new TextBox
            {
                Margin = new Thickness(0, 4, 0, 0),
                Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("HairlineBrush"),
            };
            AutomationProperties.SetName(field, $"Value for [{blank}]");
            FieldsPanel.Children.Add(label);
            FieldsPanel.Children.Add(field);
            _fields.Add((blank, field));
        }
        foreach (var (_, field) in _fields)
            field.TextChanged += (_, _) => UpdateRemaining();
        Loaded += (_, _) => _fields[0].Field.Focus();
        UpdateRemaining();
    }

    /// <summary>
    /// Hardening pack: keep "N blanks left as they were" current. The note
    /// names the blanks rather than counting them, so a user who skipped one
    /// sees WHICH before they press Copy instead of discovering the
    /// placeholder in the pasted text.
    /// </summary>
    private void UpdateRemaining()
    {
        var left = _fields
            .Where(f => string.IsNullOrWhiteSpace(f.Field.Text))
            .Select(f => f.Name)
            .ToList();
        // The TextBlock is the polite live region declared in XAML, so a
        // screen reader reads this text when it changes — no extra call.
        RemainingText.Text = left.Count == 0
            ? ""
            : left.Count == 1
                ? $"1 blank will stay as [{left[0]}] — fill it in to replace it."
                : $"{left.Count} blanks will stay as they are: {string.Join(", ", left.Select(b => $"[{b}]"))}.";
    }

    /// <summary>The values the user typed, keyed by blank name; blanks left empty are absent.</summary>
    public IReadOnlyDictionary<string, string> Values =>
        _fields.Where(f => !string.IsNullOrWhiteSpace(f.Field.Text))
               .ToDictionary(f => f.Name, f => f.Field.Text.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The filled prompt text for these values.</summary>
    public string Fill(string body) => PromptBlanks.Fill(body, Values);

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
