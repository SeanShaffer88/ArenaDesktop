using System.Text.RegularExpressions;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.15.0: the Settings window's restyle — section cards, white titles with
/// a red rule, token-only fields and buttons, and OK/Cancel pinned below the
/// scroll area. These read the XAML as text (the WPF project cannot be
/// referenced from this suite), the same way the other drift guards do.
/// </summary>
public class SettingsStyleTests
{
    private static readonly string[] SectionNames =
    {
        "SectionBehavior", "SectionTray", "SectionProfiles", "SectionAppearance",
        "SectionZoom", "SectionAccessibility", "SectionDownloads", "SectionData",
        "SectionPrivacy",
    };

    [Fact]
    public void EverySectionIsACard_AndTheCardIsTheSectionItself()
    {
        var xaml = Source("src/ArenaDesktop/SettingsWindow.xaml");
        // The card style exists and paints from the shared tokens.
        Assert.Contains("<Style x:Key=\"SectionCard\" TargetType=\"StackPanel\">", xaml);
        Assert.Contains("Value=\"{StaticResource SurfaceBrush}\"", xaml);
        Assert.Contains("Value=\"{StaticResource HairlineBrush}\"", xaml);
        // Every section carries it…
        foreach (var section in SectionNames)
            Assert.Contains($"<StackPanel x:Name=\"{section}\" Style=\"{{StaticResource SectionCard}}\">", xaml);
        // …and no section is wrapped in a Border: the search filter toggles
        // the section StackPanel's Visibility, so a wrapper would leave an
        // empty card behind when a section is hidden.
        var panel = xaml.IndexOf("x:Name=\"SectionsPanel\"", StringComparison.Ordinal);
        var first = xaml.IndexOf("x:Name=\"SectionBehavior\"", StringComparison.Ordinal);
        Assert.True(panel >= 0 && first > panel, "SectionsPanel must precede the sections");
        Assert.DoesNotContain("<Border", xaml[panel..first]);
    }

    [Fact]
    public void SectionTitlesAreWhite_WithARedRuleUnderEach()
    {
        var xaml = Source("src/ArenaDesktop/SettingsWindow.xaml");
        var headerStyle = StyleBlock(xaml, "x:Key=\"SectionHeader\"");
        Assert.Contains("Value=\"{StaticResource TextBrush}\"", headerStyle);
        Assert.DoesNotContain("AccentBrush", headerStyle);
        // One short red rule under each of the nine titles.
        Assert.Equal(9, CountOccurrences(xaml, "Height=\"2\" Width=\"44\""));
    }

    [Fact]
    public void SettingsPaintsFromSharedTokensOnly_NoHardcodedColors()
    {
        var xaml = Source("src/ArenaDesktop/SettingsWindow.xaml");
        // Every color comes from an App.xaml token (recolored by ThemeManager
        // for the light theme and High Contrast). A literal hex here would
        // paint the same in every theme — the class of bug the High Contrast
        // audit test guards from the other side.
        Assert.False(Regex.IsMatch(xaml, "#[0-9A-Fa-f]{6}"),
            "SettingsWindow.xaml must not hardcode a color; use a shared token");
    }

    [Fact]
    public void WindowLocalStyles_DoNotLoseTheFocusVisual()
    {
        var xaml = Source("src/ArenaDesktop/SettingsWindow.xaml");
        // S6 #51: a window-local implicit style replaces App.xaml's, so each
        // one must re-apply the focus ring itself.
        foreach (var type in new[] { "TextBox", "ComboBox", "Button", "CheckBox", "RadioButton" })
            Assert.Contains("ShellFocusVisual", StyleBlock(xaml, $"TargetType=\"{type}\""));
    }

    [Fact]
    public void OkAndCancel_ArePinnedBelowTheScrollArea()
    {
        var xaml = Source("src/ArenaDesktop/SettingsWindow.xaml");
        var docked = xaml.IndexOf("DockPanel.Dock=\"Bottom\"", StringComparison.Ordinal);
        var scroller = xaml.IndexOf("x:Name=\"SettingsScroller\"", StringComparison.Ordinal);
        var ok = xaml.IndexOf("Content=\"OK\"", StringComparison.Ordinal);
        Assert.True(docked >= 0 && scroller > docked, "the button bar must dock below the scroll area");
        Assert.True(ok >= 0 && ok < scroller, "OK must sit outside the scrolling content");
        Assert.Equal(1, CountOccurrences(xaml, "Content=\"OK\""));
        Assert.Equal(1, CountOccurrences(xaml, "Content=\"Cancel\""));
    }

    [Fact]
    public void TheButtonTemplate_IsRounded_AndPaintsFromTokens()
    {
        var xaml = Source("src/ArenaDesktop/SettingsWindow.xaml");
        var start = xaml.IndexOf("<ControlTemplate TargetType=\"Button\">", StringComparison.Ordinal);
        Assert.True(start >= 0, "the Button style must carry its own template");
        var end = xaml.IndexOf("</ControlTemplate>", start, StringComparison.Ordinal);
        Assert.True(end > start, "the Button template is not closed");
        var template = xaml[start..end];
        Assert.Contains("CornerRadius=\"6\"", template);
        Assert.Contains("IsMouseOver", template);
        Assert.Contains("IsPressed", template);
        Assert.Contains("IsEnabled", template);
        Assert.False(Regex.IsMatch(template, "#[0-9A-Fa-f]{6}"),
            "the Button template must not hardcode a color");
    }

    private static string StyleBlock(string xaml, string marker)
    {
        var styleStart = xaml.IndexOf("<Style", StringComparison.Ordinal);
        while (styleStart >= 0)
        {
            var tagEnd = xaml.IndexOf('>', styleStart);
            Assert.True(tagEnd > styleStart, $"style {marker} has no closing bracket");
            if (xaml[styleStart..tagEnd].Contains(marker, StringComparison.Ordinal))
            {
                var end = xaml.IndexOf("</Style>", tagEnd, StringComparison.Ordinal);
                Assert.True(end > tagEnd, $"style {marker} is not closed");
                return xaml[styleStart..end];
            }
            styleStart = xaml.IndexOf("<Style", tagEnd, StringComparison.Ordinal);
        }
        throw new InvalidOperationException($"style {marker} not found");
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(SettingsStyleTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArenaDesktop.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "src", "ArenaDesktop")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root from the test assembly.");
    }
}
