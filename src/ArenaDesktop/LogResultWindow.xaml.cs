using System.Windows;
using ArenaDesktop.Core;
using MessageBox = System.Windows.MessageBox;

namespace ArenaDesktop;

/// <summary>
/// Log a result (Battle Lab, v0.2.0): pick the prompt (defaults to
/// the last one copied), pick the winner, add an optional note. The
/// journal is written by hand — nothing is read from the Arena page.
/// </summary>
public partial class LogResultWindow : Window
{
    public LogResultWindow(IReadOnlyList<string> promptNames, string? defaultPromptName)
    {
        InitializeComponent();
        foreach (var name in promptNames)
            PromptCombo.Items.Add(name);
        if (defaultPromptName is not null && promptNames.Contains(defaultPromptName))
            PromptCombo.SelectedItem = defaultPromptName;
        else if (PromptCombo.Items.Count > 0)
            PromptCombo.SelectedIndex = 0;
        Loaded += (_, _) => PromptCombo.Focus();
    }

    public string PromptName => PromptCombo.SelectedItem as string ?? "";

    public BattleOutcome Outcome =>
        BWonRadio.IsChecked == true ? BattleOutcome.BWins
        : TieRadio.IsChecked == true ? BattleOutcome.Tie
        : BattleOutcome.AWins;

    public string Note => NoteText.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PromptName))
        {
            MessageBox.Show(this, "Pick the prompt that was run first.", "Log a result",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
