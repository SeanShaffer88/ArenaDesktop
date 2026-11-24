using System.Text.Json;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// Simple Mode: the Core policy every door (menu, tray, keyboard,
/// command line, arenadesktop:// links) is judged by, and the
/// setting's persistence. The shell's hiding is wiring; what is
/// pinned here is the rule itself.
/// </summary>
public class SimpleModeTests
{
    [Fact]
    public void SimpleMode_DefaultsOff()
    {
        Assert.False(new AppSettings().SimpleMode);
    }

    [Fact]
    public void SimpleMode_SurvivesSettingsRoundTrip()
    {
        var settings = new AppSettings { SimpleMode = true };
        var json = JsonSerializer.Serialize(settings);
        var back = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(back);
        Assert.True(back!.SimpleMode);

        // Clone is the Settings window's edit copy — Cancel must be
        // able to discard a Simple Mode change like any other.
        Assert.True(settings.Clone().SimpleMode);
        Assert.False(new AppSettings().Clone().SimpleMode);
    }

    [Fact]
    public void SimpleMode_SurvivesSanitize()
    {
        var settings = new AppSettings { SimpleMode = true };
        Assert.True(settings.Sanitized().SimpleMode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void BattleLab_FollowsSimpleMode(bool simple, bool expected) =>
        Assert.Equal(expected, SimpleModePolicy.AllowsBattleLab(simple));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void PromptPad_FollowsSimpleMode(bool simple, bool expected) =>
        Assert.Equal(expected, SimpleModePolicy.AllowsPromptPad(simple));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CommandPalette_FollowsSimpleMode(bool simple, bool expected) =>
        Assert.Equal(expected, SimpleModePolicy.AllowsCommandPalette(simple));

    [Fact]
    public void TrayMenu_FullModeShowsEverything()
    {
        foreach (var id in SimpleModePolicy.TrayItemIds)
            Assert.True(SimpleModePolicy.IsTrayItemVisible(id, simpleMode: false), id);
    }

    [Fact]
    public void TrayMenu_SimpleModeKeepsOnlyOpenAndExit()
    {
        foreach (var id in SimpleModePolicy.TrayItemIds)
        {
            var expected = id is "open" or "exit";
            Assert.Equal(expected, SimpleModePolicy.IsTrayItemVisible(id, simpleMode: true));
        }
        // Separators and unknown items (null id) hide too — a menu of
        // Open, a stray separator, and Exit is not "only Open and Exit".
        Assert.False(SimpleModePolicy.IsTrayItemVisible(null, simpleMode: true));
        Assert.True(SimpleModePolicy.IsTrayItemVisible(null, simpleMode: false));
    }

    [Fact]
    public void TrayItemIds_CoverThePolicyLists()
    {
        // Every id the tray policy can keep must be an id the full
        // menu actually has, or Simple Mode would keep nothing.
        foreach (var id in SimpleModePolicy.SimpleTrayItemIds)
            Assert.Contains(id, SimpleModePolicy.TrayItemIds);
        Assert.Equal(2, SimpleModePolicy.SimpleTrayItemIds.Count);
    }

    [Theory]
    [InlineData("battle-lab-pane")]
    [InlineData("prompt-pad")]
    [InlineData("battle-next-queue")]
    public void PaletteCommands_HiddenInSimpleMode(string id) =>
        Assert.False(SimpleModePolicy.AllowsPaletteCommand(id, simpleMode: true));

    [Theory]
    [InlineData("settings")]
    [InlineData("downloads")]
    [InlineData("mode:battle")]
    [InlineData("help")]
    public void PaletteCommands_OrdinaryCommandsSurviveTheFilter(string id)
    {
        Assert.True(SimpleModePolicy.AllowsPaletteCommand(id, simpleMode: true));
        Assert.True(SimpleModePolicy.AllowsPaletteCommand(id, simpleMode: false));
    }

    [Fact]
    public void PaletteCommands_FullModeAllowsEverything()
    {
        foreach (var id in SimpleModePolicy.HiddenPaletteCommandIds)
            Assert.True(SimpleModePolicy.AllowsPaletteCommand(id, simpleMode: false));
    }
}
