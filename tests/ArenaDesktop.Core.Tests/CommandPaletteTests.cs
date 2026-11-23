using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.10.0 — the command palette: one list for the ~30 commands that
/// otherwise live in four menus, plus a home for the layout cycle that
/// had no key at all. The catalogue is data, so it is pinned here rather
/// than discovered by pressing Ctrl+K: every mode appears, every printed
/// key resolves to a real catalog action, an unknown id is null rather
/// than a crash, and the search answers the same prefix-per-term rule the
/// rest of the app uses.
/// </summary>
public class CommandPaletteTests
{
    // ──────────────────────── the catalogue ────────────────────────

    [Fact]
    public void EveryArenaModeHasACommand()
    {
        foreach (var mode in ArenaModes.All)
        {
            var command = CommandPalette.ById(CommandPalette.ModePrefix + mode.Id);
            Assert.NotNull(command);
            Assert.Equal($"Go to {mode.Label}", command!.Title);
            Assert.Equal("Arena modes", command.Group);
            Assert.Equal($"mode-{mode.Id}", command.ShortcutId);
        }
    }

    [Fact]
    public void ModeCommandsAppearInRegistryOrder()
    {
        var modeCommands = CommandPalette.All
            .Where(c => c.Id.StartsWith(CommandPalette.ModePrefix, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(ArenaModes.All.Count, modeCommands.Count);
        for (var i = 0; i < ArenaModes.All.Count; i++)
        {
            Assert.Equal(CommandPalette.ModePrefix + ArenaModes.All[i].Id, modeCommands[i].Id);
        }
    }

    [Fact]
    public void EveryShortcutId_ResolvesToACatalogAction()
    {
        // A dangling id would print nothing beside the row and hide a
        // typo; the palette promises the key that actually works.
        foreach (var command in CommandPalette.All.Where(c => c.ShortcutId is not null))
        {
            var action = ShortcutCatalog.Find(command.ShortcutId!);
            Assert.True(action is not null, $"{command.Id} names unknown shortcut '{command.ShortcutId}'");
            Assert.True(
                ShortcutGesture.TryParse(action!.Gestures[0], out _),
                $"{command.ShortcutId} has an unparseable default gesture");
        }
    }

    [Fact]
    public void IdsAreUnique_AndTitlesAreNotEmpty()
    {
        Assert.Equal(CommandPalette.All.Count, CommandPalette.All.Select(c => c.Id).Distinct().Count());
        Assert.All(CommandPalette.All, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Title));
            Assert.False(string.IsNullOrWhiteSpace(c.Group));
            Assert.False(string.IsNullOrWhiteSpace(c.Keywords));
        });
    }

    [Fact]
    public void EveryCommandBelongsToAKnownGroup()
    {
        Assert.All(CommandPalette.All, c => Assert.Contains(c.Group, CommandPalette.GroupOrder));
        // And every group in the order is actually used, so the list has
        // no phantom heading.
        foreach (var group in CommandPalette.GroupOrder)
        {
            Assert.Contains(CommandPalette.All, c => c.Group == group);
        }
    }

    [Fact]
    public void EveryShellCommandTheMainWindowDispatchesExists()
    {
        // The ids MainWindow.ExecutePaletteCommand switches on. Kept in
        // one place here so a renamed command fails the suite instead of
        // silently doing nothing when someone runs it.
        string[] dispatching =
        [
            "settings", "downloads", "battle-lab-pane", "prompt-pad", "field-pass",
            "shortcuts", "diagnostics", "session", "help", "check-updates",
            "focus-mode", "always-on-top",
            "layout-standard", "layout-compact", "layout-left-rail", "layout-cycle",
            "find", "back", "forward", "home", "reload", "restore-closed",
            "zoom-in", "zoom-out", "zoom-reset", "print", "copy-address", "open-in-browser",
        ];
        foreach (var id in dispatching)
        {
            Assert.NotNull(CommandPalette.ById(id));
        }
    }

    // ──────────────────────── lookup ────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-such-command")]
    [InlineData("mode:no-such-mode")]
    public void ById_ReturnsNullForAnythingUnknown(string? id) =>
        Assert.Null(CommandPalette.ById(id));

    [Fact]
    public void ById_IsExact()
    {
        Assert.Equal("settings", CommandPalette.ById("settings")!.Id);
        Assert.Null(CommandPalette.ById("Settings"));
        Assert.Null(CommandPalette.ById("setting"));
    }

    // ──────────────────────── search ────────────────────────

    [Fact]
    public void BlankQueryListsEverything()
    {
        Assert.Equal(CommandPalette.All.Count, CommandPalette.Search(null).Count);
        Assert.Equal(CommandPalette.All.Count, CommandPalette.Search("").Count);
        Assert.Equal(CommandPalette.All.Count, CommandPalette.Search("  \t ").Count);
    }

    [Fact]
    public void SearchIsCaseInsensitive()
    {
        var lower = CommandPalette.Search("zoom").Select(c => c.Id).ToList();
        var upper = CommandPalette.Search("ZOOM").Select(c => c.Id).ToList();
        Assert.Equal(lower, upper);
        Assert.NotEmpty(lower);
    }

    [Fact]
    public void EveryTermMustMatch_SoMoreWordsNarrow()
    {
        var broad = CommandPalette.Search("zoom").Count;
        var narrow = CommandPalette.Search("zoom reset").Count;
        Assert.True(narrow <= broad, "adding a term must never widen the list");
        Assert.Contains(CommandPalette.Search("zoom reset"), c => c.Id == "zoom-reset");
    }

    [Fact]
    public void TermsArePrefixMatchedPerWord()
    {
        // "sett" prefixes "settings"; "ettings" does not prefix any word.
        Assert.Contains(CommandPalette.Search("sett"), c => c.Id == "settings");
        Assert.Empty(CommandPalette.Search("ettings"));
    }

    [Fact]
    public void ModesAreFoundByTheirLabel()
    {
        foreach (var mode in ArenaModes.All)
        {
            var matches = CommandPalette.Search(mode.Label);
            Assert.Contains(matches, c => c.Id == CommandPalette.ModePrefix + mode.Id);
        }
    }

    [Fact]
    public void KeywordsFindCommandsThatTheTitleDoesNotName()
    {
        // The palette is how people discover features: "paste" should
        // find the prompt pad even though its title never says "paste".
        Assert.Contains(CommandPalette.Search("paste"), c => c.Id == "prompt-pad");
        Assert.Contains(CommandPalette.Search("rotate"), c => c.Id == "layout-cycle");
    }

    [Fact]
    public void NonsenseMatchesNothing() =>
        Assert.Empty(CommandPalette.Search("zzzz-no-such-word"));

    // ──────────────────────── grouping ────────────────────────

    [Fact]
    public void GroupedForBlankQuery_CoversEveryCommandInOrder()
    {
        var grouped = CommandPalette.Grouped(null);
        Assert.Equal(CommandPalette.GroupOrder.Where(g => grouped.Any(x => x.Group == g)), grouped.Select(g => g.Group));
        Assert.Equal(CommandPalette.All.Count, grouped.Sum(g => g.Commands.Count));
        // Modes lead, because they are the nine things used every day.
        Assert.Equal("Arena modes", grouped[0].Group);
    }

    [Fact]
    public void GroupedDropsEmptyGroups()
    {
        var grouped = CommandPalette.Grouped("zoom");
        Assert.Contains(grouped, g => g.Group == "Zoom");
        Assert.All(grouped, g => Assert.NotEmpty(g.Commands));
        Assert.DoesNotContain(grouped, g => g.Group == "Arena modes");
    }

    // ──────────────────────── the summary line ────────────────────────

    [Fact]
    public void SummarySaysHowManyAndHowToLeave()
    {
        var blank = CommandPalette.SummaryText(CommandPalette.All.Count, CommandPalette.All.Count, null);
        Assert.Contains($"{CommandPalette.All.Count} commands", blank);
        Assert.Contains("Esc closes", blank);
        Assert.Contains("Enter runs", blank);
    }

    [Fact]
    public void SummaryForMatchesCountsAgainstTheTotal()
    {
        var total = CommandPalette.All.Count;
        Assert.Contains($"3 of {total}", CommandPalette.SummaryText(3, total, "zoom"));
        // One match says so in the singular, rather than "1 of 37 commands".
        Assert.Contains("1 of", CommandPalette.SummaryText(1, total, "rotate"));
        Assert.Contains("Enter runs it", CommandPalette.SummaryText(1, total, "rotate"));
    }

    [Fact]
    public void SummaryForNoMatchesIsInformation_NotAnError()
    {
        var none = CommandPalette.SummaryText(0, CommandPalette.All.Count, "  zzzz  ");
        Assert.Contains("No command matches", none);
        Assert.Contains("zzzz", none);
        Assert.Contains("Esc closes", none);
        // Calm wording: no exclamation mark, and no apology theatre.
        Assert.DoesNotContain("!", none);
        Assert.DoesNotContain("Oops", none, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", none, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EverySummaryFitsTheStatusLine()
    {
        // The status line is announced through one polite live region and
        // the app keeps those under 140 characters.
        string[] queries = ["", "zoom", "rotate", "zzzz", "a very long query that matches nothing at all here"];
        foreach (var query in queries)
        {
            var shown = CommandPalette.Search(query).Count;
            Assert.True(
                CommandPalette.SummaryText(shown, CommandPalette.All.Count, query).Length <= 140,
                $"summary for '{query}' is too long for the status line");
        }
    }

    // ──────────────────────── the shell contract ────────────────────────

    [Fact]
    public void Shell_DispatchesEveryCatalogIdItClaimsTo()
    {
        var shell = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        // Every non-mode id in the catalogue has a case in the shell's
        // dispatch switch: a command that lists but does nothing is worse
        // than a command that is not listed.
        foreach (var command in CommandPalette.All)
        {
            if (command.Id.StartsWith(CommandPalette.ModePrefix, StringComparison.Ordinal))
                continue;
            Assert.Contains($"case \"{command.Id}\"", shell);
        }
        Assert.Contains("CommandPalette.ModePrefix", shell);
    }

    [Fact]
    public void Shell_OpensThePaletteFromEveryDoor()
    {
        var shell = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        Assert.Contains("public void OpenCommandPaletteWindow()", shell);
        Assert.Contains("openCommandPalette: OpenCommandPaletteWindow", shell);
        Assert.Contains("LaunchIntentKind.OpenCommandPalette", shell);
        // Ctrl+K with no shift opens it; the lock rule comes first.
        Assert.Contains("e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control", shell);
        var xaml = Source("src/ArenaDesktop/MainWindow.xaml");
        Assert.Contains("Click=\"OnCommandPalette\"", xaml);
        Assert.Contains("InputGestureText=\"Ctrl+K\"", xaml);
        var tray = Source("src/ArenaDesktop/TrayController.cs");
        Assert.Contains("Command palette", tray);
        // A lock hides the palette behind the lock screen, so the tray
        // item must not stay clickable while locked.
        Assert.Contains("_commandPaletteItem.Enabled = !locked", tray);
        var app = Source("src/ArenaDesktop/App.xaml.cs");
        Assert.Contains("LaunchIntentKind.OpenCommandPalette", app);
    }

    [Fact]
    public void Shell_GivesTheLayoutCycleAKey_AndAMenuItem()
    {
        var shell = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        Assert.Contains("ShellLayoutCycle.Next(_settings.ShellLayout)", shell);
        Assert.Contains("Key.K\n            && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)", shell);
        var xaml = Source("src/ArenaDesktop/MainWindow.xaml");
        Assert.Contains("Header=\"Cycle layout\"", xaml);
        Assert.Contains("InputGestureText=\"Ctrl+Shift+K\"", xaml);
    }

    [Fact]
    public void ShortcutSheetListsThePalette()
    {
        var action = ShortcutCatalog.Find("command-palette");
        Assert.NotNull(action);
        Assert.Equal("Ctrl+K", action!.Gestures[0]);
        // It is an in-app key: the page never sees it, and it is not
        // offered as a global hotkey.
        Assert.Equal(ShortcutScope.InApp, action.Scope);
        Assert.False(action.Remappable);
    }

    // ──────────────────────── the command line and the link ────────────────────────

    [Theory]
    [InlineData("--palette")]
    [InlineData("--command-palette")]
    [InlineData("--PALETTE")]
    public void Flag_AsksForThePalette(string flag)
    {
        var options = CommandLine.Parse([flag]);
        Assert.True(options.OpenCommandPalette);
        Assert.Empty(options.Unknown);
        Assert.Equal(LaunchIntentKind.OpenCommandPalette, LaunchIntent.FromCommandLine(options).Kind);
    }

    [Fact]
    public void Flag_IsDocumentedInTheUsageText()
    {
        var text = CommandLineUsage.Text("0.10.0");
        Assert.Contains("--palette", text);
        Assert.Contains("--command-palette", text);
        Assert.Contains("arenadesktop://palette", text);
    }

    [Fact]
    public void Link_OpensThePalette()
    {
        var parsed = LaunchIntent.ParseProtocolUri("arenadesktop://palette", ["default"]);
        Assert.NotNull(parsed.Intent);
        Assert.Equal(LaunchIntentKind.OpenCommandPalette, parsed.Intent!.Kind);
    }

    [Fact]
    public void Link_TakesNoArgument()
    {
        // Like every other route: a path after the route is refused
        // rather than half-understood.
        var parsed = LaunchIntent.ParseProtocolUri("arenadesktop://palette/settings", ["default"]);
        Assert.Null(parsed.Intent);
        Assert.NotNull(parsed.Refusal);
    }

    /// <summary>
    /// A repo file's text with line endings normalised to LF. The CI
    /// checkout is CRLF on Windows, and an assertion that spans two
    /// source lines must not depend on which one it got — the first
    /// version of this file failed on exactly that.
    /// </summary>
    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(CommandPaletteTests).Assembly.Location)!);
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
