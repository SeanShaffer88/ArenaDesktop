using System.Windows;
using ArenaDesktop.Core;
using MessageBox = System.Windows.MessageBox;

namespace ArenaDesktop;

/// <summary>
/// Log a result (Battle Lab, v0.2.0): pick the prompt (defaults to
/// the last one copied), pick the winner, say which mode it ran in
/// (defaults to the last page a prompt was copied to — the modes the
/// journal can filter by), add an optional note. The journal is
/// written by hand — nothing is read from the Arena page, so the
/// mode is declared here the same way the winner is.
/// </summary>
public partial class LogResultWindow : Window
{
    public LogResultWindow(IReadOnlyList<string> promptNames, string? defaultPromptName, string? defaultModeId = null)
    {
        InitializeComponent();
        foreach (var name in promptNames)
            PromptCombo.Items.Add(name);
        if (defaultPromptName is not null && promptNames.Contains(defaultPromptName))
            PromptCombo.SelectedItem = defaultPromptName;
        else if (PromptCombo.Items.Count > 0)
            PromptCombo.SelectedIndex = 0;
        // The modes a Battle Lab run can land on — the same three the
        // copy button targets.
        var modes = new[]
        {
            ArenaModes.All.First(m => m.Id == ArenaModes.BattleId),
            ArenaModes.All.First(m => m.Id == ArenaModes.DirectId),
            ArenaModes.All.First(m => m.Id == ArenaModes.SideBySideId),
        };
        foreach (var mode in modes)
            ModeCombo.Items.Add(mode);
        ModeCombo.SelectedItem = modes.FirstOrDefault(m => m.Id == defaultModeId) ?? modes[0];
        Loaded += (_, _) => PromptCombo.Focus();
    }

    public string PromptName => PromptCombo.SelectedItem as string ?? "";

    /// <summary>The mode the user says the battle ran in — a registry id, always one of the three offered.</summary>
    public string ModeId => (ModeCombo.SelectedItem as ArenaMode)?.Id ?? ArenaModes.BattleId;

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
