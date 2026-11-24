using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>Completeness slice D1: the tab model.</summary>
public sealed class TabModelTests
{
    [Fact]
    public void Open_ActivatesAndCaps()
    {
        var set = new TabSet();
        var first = set.Open("https://arena.ai/");
        Assert.Equal(first.Id, set.ActiveTabId);
        for (var i = 0; i < TabSet.MaxTabs + 3; i++) set.Open($"https://arena.ai/{i}");
        Assert.Equal(TabSet.MaxTabs, set.Tabs.Count);
    }

    [Fact]
    public void Close_ActivatesNeighbourAndStacksForReopen()
    {
        var set = new TabSet();
        var a = set.Open("https://arena.ai/a");
        var b = set.Open("https://arena.ai/b");
        var c = set.Open("https://arena.ai/c");
        set.Activate(b.Id);
        var next = set.Close(b.Id);
        Assert.Equal(c.Id, next!.Id);
        Assert.Equal(c.Id, set.ActiveTabId);
        var reopened = set.ReopenClosed();
        Assert.Equal(b.Id, reopened!.Id);
        Assert.Equal(b.Id, set.ActiveTabId);
        Assert.Equal(a.Id, set.Tabs[0].Id);
    }

    [Fact]
    public void Close_LastTabLeavesNone_AndReopenRestores()
    {
        var set = new TabSet();
        var only = set.Open("https://arena.ai/only");
        Assert.Null(set.Close(only.Id));
        Assert.NotNull(set.ReopenClosed());
    }

    [Fact]
    public void Sanitize_RepairsPersistedState()
    {
        var set = new TabSet
        {
            ActiveTabId = "missing",
            Tabs =
            [
                new BrowserTab { Id = "x", Url = "https://arena.ai/" },
                new BrowserTab { Id = "x", Url = "https://arena.ai/2" },
                new BrowserTab { Id = "y", Url = "  " },
            ],
        };
        var repaired = TabSet.Sanitize(set);
        Assert.Equal(2, repaired.Tabs.Count);
        Assert.NotEqual(repaired.Tabs[0].Id, repaired.Tabs[1].Id);
        Assert.Equal(repaired.Tabs[0].Id, repaired.ActiveTabId);
    }

    [Fact]
    public void Settings_RoundTripsTabsPerProfile()
    {
        var settings = new AppSettings();
        settings.OpenTabsByProfile["default"] = new TabSet();
        settings.OpenTabsByProfile["default"].Open("https://arena.ai/battle", "Battle");
        var json = SettingsStore.Export(settings);
        Assert.True(SettingsStore.TryImport(json, out var back, out _));
        var tabs = back!.OpenTabsByProfile["default"];
        Assert.Single(tabs.Tabs);
        Assert.Equal("Battle", tabs.Tabs[0].Title);
        Assert.Equal("Battle", tabs.Tabs[0].DisplayTitle);
    }
}

/// <summary>Slice D4: the externalized string table.</summary>
public sealed class ShellStringsTests
{
    [Fact]
    public void Lookup_FallsBackToEnglishThenKey()
    {
        Assert.Equal("New tab", ShellStrings.Get("en", "tab.new"));
        Assert.Equal("New tab", ShellStrings.Get("de-DE", "tab.new")); // no German table yet: honest fallback
        Assert.Equal("missing.key", ShellStrings.Get("en", "missing.key"));
        Assert.Equal("New tab", ShellStrings.Get(null, "tab.new"));
    }

    [Fact]
    public void Culture_ResolutionAndSanitize()
    {
        Assert.Equal("en-US", ShellStrings.ResolveCulture("", "en-US"));
        Assert.Equal("en-US", ShellStrings.ResolveCulture("system", "en-US"));
        Assert.Equal("en", ShellStrings.ResolveCulture("en", "en-US"));
        Assert.Equal("", ShellStrings.SanitizeUiLanguage("de")); // not shipped yet
        Assert.Equal("en", ShellStrings.SanitizeUiLanguage("EN"));
        Assert.Equal("system", ShellStrings.SanitizeUiLanguage("System"));
    }
}
