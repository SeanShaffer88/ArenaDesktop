using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// Shell layout (v0.2.0, ported from the MuseDesktop parity target):
/// three layouts — Standard, Compact Focus, Left Rail — with Compact
/// Focus the fresh-install default. The invariants: Standard is
/// exactly the long-standing chrome numbers (no silent change for
/// existing users), Compact Focus is slimmer and trades the status
/// bar for the pill, Left Rail pins the same slim strip, keeps the
/// status bar, and takes a 56px rail — and a SAVED layout always
/// wins, because only the no-saved-value default changed.
/// </summary>
public class ShellLayoutTests
{
    [Fact]
    public void Standard_MatchesTheLongStandingChromeExactly()
    {
        var layout = ShellLayoutMetrics.For(ShellLayout.Standard);
        Assert.Equal(40, layout.ToolbarHeight);
        Assert.Equal(24, layout.StatusBarHeight);
        Assert.True(layout.StatusBarVisible);
        Assert.False(layout.StatusPillVisible);
        Assert.Equal(0, layout.RailWidth);
        Assert.False(layout.RailVisible);
    }

    [Fact]
    public void CompactFocus_SlimToolbarBarHiddenPillShown()
    {
        var layout = ShellLayoutMetrics.For(ShellLayout.CompactFocus);
        Assert.Equal(30, layout.ToolbarHeight);
        Assert.True(layout.ToolbarHeight < ShellLayoutMetrics.For(ShellLayout.Standard).ToolbarHeight);
        Assert.False(layout.StatusBarVisible);
        Assert.True(layout.StatusPillVisible);
        Assert.Equal(0, layout.StatusBarHeight);
        Assert.Equal(0, layout.RailWidth);
        Assert.False(layout.RailVisible);
    }

    [Fact]
    public void CompactFocus_PageHeightGainIsToolbarPlusStatusBar()
    {
        // (40-30) toolbar + the whole 24px status bar.
        Assert.Equal(34, ShellLayoutMetrics.PageHeightGain(ShellLayout.CompactFocus));
        Assert.Equal(0, ShellLayoutMetrics.PageHeightGain(ShellLayout.Standard));
    }

    [Fact]
    public void LeftRail_SlimStripStatusBarKeptRail56()
    {
        var layout = ShellLayoutMetrics.For(ShellLayout.LeftRail);
        Assert.Equal(30, layout.ToolbarHeight);
        Assert.True(layout.StatusBarVisible);
        Assert.False(layout.StatusPillVisible);
        Assert.Equal(24, layout.StatusBarHeight);
        Assert.Equal(56, layout.RailWidth);
        Assert.True(layout.RailVisible);
    }

    [Fact]
    public void LeftRail_PageHeightGainIsTheStripSavingOnly()
    {
        // (40-30) toolbar; the status bar stays.
        Assert.Equal(10, ShellLayoutMetrics.PageHeightGain(ShellLayout.LeftRail));
    }

    [Theory]
    [InlineData(ShellLayout.Standard, ShellLayout.CompactFocus)]
    [InlineData(ShellLayout.CompactFocus, ShellLayout.LeftRail)]
    [InlineData(ShellLayout.LeftRail, ShellLayout.Standard)]
    [InlineData((ShellLayout)99, ShellLayout.Standard)]
    public void Cycle_StepsThroughAllThreeLayouts(ShellLayout from, ShellLayout expected)
    {
        Assert.Equal(expected, ShellLayoutCycle.Next(from));
    }

    [Fact]
    public void Settings_DefaultIsCompactFocus()
    {
        // v0.2.0: a FRESH install — no saved settings at all — starts
        // in Compact Focus.
        Assert.Equal(ShellLayout.CompactFocus, new AppSettings().ShellLayout);
    }

    [Fact]
    public void Settings_MissingFileLoadsCompactFocus()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Assert.Equal(ShellLayout.CompactFocus, SettingsStore.Load(path).ShellLayout);
    }

    [Theory]
    [InlineData(ShellLayout.Standard)]
    [InlineData(ShellLayout.CompactFocus)]
    [InlineData(ShellLayout.LeftRail)]
    public void Settings_SavedLayoutAlwaysWins(ShellLayout saved)
    {
        // The persisted value round-trips through the real store and
        // is never overridden by the fresh-install default — existing
        // users keep the layout they saved, Standard included.
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        try
        {
            SettingsStore.Save(path, new AppSettings { ShellLayout = saved });
            Assert.Equal(saved, SettingsStore.Load(path).ShellLayout);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_UndefinedLayoutRepairsToTheDefault()
    {
        var settings = new AppSettings { ShellLayout = (ShellLayout)99 }.Sanitized();
        Assert.Equal(ShellLayout.CompactFocus, settings.ShellLayout);
    }

    [Fact]
    public void Settings_CompactFocusSurvivesSanitize()
    {
        var settings = new AppSettings { ShellLayout = ShellLayout.CompactFocus }.Sanitized();
        Assert.Equal(ShellLayout.CompactFocus, settings.ShellLayout);
    }

    [Fact]
    public void Settings_LeftRailSurvivesSanitize()
    {
        var settings = new AppSettings { ShellLayout = ShellLayout.LeftRail }.Sanitized();
        Assert.Equal(ShellLayout.LeftRail, settings.ShellLayout);
    }

    [Fact]
    public void Reset_ReturnsLayoutToTheFreshInstallDefault()
    {
        var settings = new AppSettings { ShellLayout = ShellLayout.LeftRail };
        SettingsReset.ApplyDefaults(settings);
        Assert.Equal(ShellLayout.CompactFocus, settings.ShellLayout);
    }
}
