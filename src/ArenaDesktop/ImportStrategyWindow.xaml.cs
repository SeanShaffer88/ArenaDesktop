using System.Windows;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// Import strategy (Battle Lab): one choice per import, applied to
/// every title clash in the file. The merge itself lives in Core
/// (<see cref="BattleLabLibrary.ImportPrompts(string, BattleLabImportStrategy)"/>);
/// this dialog only carries the choice, and Skip — the long-standing
/// merge behavior — is the default.
/// </summary>
public partial class ImportStrategyWindow : Window
{
    public ImportStrategyWindow() => InitializeComponent();

    public BattleLabImportStrategy Strategy =>
        ReplaceRadio.IsChecked == true ? BattleLabImportStrategy.Replace
        : KeepBothRadio.IsChecked == true ? BattleLabImportStrategy.KeepBoth
        : BattleLabImportStrategy.Skip;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
