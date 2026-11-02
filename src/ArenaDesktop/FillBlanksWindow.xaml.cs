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
        Loaded += (_, _) => _fields[0].Field.Focus();
    }

    /// <summary>The values the user typed, keyed by blank name; blanks left empty are absent.</summary>
    public IReadOnlyDictionary<string, string> Values =>
        _fields.Where(f => !string.IsNullOrWhiteSpace(f.Field.Text))
               .ToDictionary(f => f.Name, f => f.Field.Text.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The filled prompt text for these values.</summary>
    public string Fill(string body) => PromptBlanks.Fill(body, Values);

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
