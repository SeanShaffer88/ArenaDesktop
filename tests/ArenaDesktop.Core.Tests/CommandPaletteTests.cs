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
            "zoom-in", "zoom-out", "print", "save-as-pdf", "copy-address",
            "open-in-browser", "open-in-new-window",
            // v0.11.0: the rest of the ⋯ menu.
            "quick-ask", "connection-security", "view-log", "open-log-folder",
            "export-support-bundle", "about", "lock-now",
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
        // "reset" finds the 100% preset, which is the Ctrl+0 action.
        Assert.Contains(CommandPalette.Search("zoom reset"), c => c.Id == "zoom-preset:actual");
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
        // One match says so in the singular, rather than "1 of N commands".
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

    // ──────────────────────── zoom presets (v0.11.0) ────────────────────────

    [Fact]
    public void EveryZoomPresetHasACommand()
    {
        foreach (var preset in ZoomPresets.All)
        {
            var command = CommandPalette.ById(CommandPalette.ZoomPresetId(preset));
            Assert.NotNull(command);
            Assert.Equal($"Zoom to {preset.Display}", command!.Title);
            Assert.Equal("Zoom", command.Group);
            Assert.Equal(preset, CommandPalette.ZoomPresetFor(command.Id));
        }
    }

    [Fact]
    public void TheHundredPercentPresetIsTheCtrlZeroAction()
    {
        // One action, one row: 100% is Ctrl+0, so it carries that key
        // instead of a second "reset zoom" command that did the same thing.
        var actual = ZoomPresets.All.Single(p => Math.Abs(p.Factor - 1.0) < 0.001);
        var command = CommandPalette.ById(CommandPalette.ZoomPresetId(actual))!;
        Assert.Equal("zoom-reset", command.ShortcutId);
        Assert.Null(CommandPalette.ById("zoom-reset"));
        // The other levels have no key of their own, and say nothing.
        Assert.All(
            ZoomPresets.All.Where(p => p != actual),
            p => Assert.Null(CommandPalette.ById(CommandPalette.ZoomPresetId(p))!.ShortcutId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zoom-preset:")]
    [InlineData("zoom-preset:nonsense")]
    [InlineData("zoom-reset")]
    [InlineData("mode:half")]
    public void ZoomPresetFor_ReturnsNullForAnythingElse(string? id) =>
        Assert.Null(CommandPalette.ZoomPresetFor(id));

    // ──────────────────────── availability (v0.11.0) ────────────────────────

    [Fact]
    public void Available_NullPredicateKeepsEveryCommand() =>
        Assert.Equal(CommandPalette.All.Count, CommandPalette.Available(null).Count);

    [Fact]
    public void Available_FiltersTheCatalogue()
    {
        var available = CommandPalette.Available(c => c.Group == "Zoom");
        Assert.NotEmpty(available);
        Assert.All(available, c => Assert.Equal("Zoom", c.Group));
    }

    [Fact]
    public void Search_AppliesAvailabilityBeforeMatching()
    {
        // The shell hides a command that cannot work; a hidden command
        // must not be findable by name either, or the palette would show
        // a row that only refuses.
        var without = CommandPalette.Search("lock", c => c.Id != "lock-now");
        Assert.DoesNotContain(without, c => c.Id == "lock-now");

        var onlyLock = CommandPalette.Search(null, c => c.Id == "lock-now");
        Assert.Single(onlyLock);
        Assert.Equal("lock-now", onlyLock[0].Id);
    }

    [Fact]
    public void Grouped_AppliesAvailabilityToo()
    {
        var grouped = CommandPalette.Grouped(null, c => c.Id != "lock-now");
        Assert.DoesNotContain(grouped.SelectMany(g => g.Commands), c => c.Id == "lock-now");
        Assert.Equal(CommandPalette.All.Count - 1, grouped.Sum(g => g.Commands.Count));
    }

    [Fact]
    public void AvailabilityIsTheShellsCall_NotCores()
    {
        // Core stores no "is there a lock?" fact of its own: the predicate
        // comes from the shell, and the shell's answer decides both what
        // is listed and what the summary counts.
        var shell = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        Assert.Contains("private bool IsPaletteCommandAvailable(PaletteCommand command)", shell);
        Assert.Contains("AppLock.IsLockActive(_settings)", shell);
        Assert.Contains("ZoomPresets.IsAvailable(preset, _settings.ExtendedZoomRange)", shell);
        Assert.Contains("new CommandPaletteWindow(_settings, IsPaletteCommandAvailable)", shell);
        var window = Source("src/ArenaDesktop/CommandPaletteWindow.xaml.cs");
        Assert.Contains("CommandPalette.Available(_isAvailable).Count", window);
    }

    // ──────────────────────── the menus are all reachable ────────────────────────

    /// <summary>
    /// Every clickable item in the ⋯ menu, mapped to the palette command
    /// that does the same thing. This dictionary IS the v0.11.0 promise:
    /// the palette covers the menu, and a new menu item without a command
    /// fails this test until someone either adds one or writes down why
    /// it is exempt.
    /// </summary>
    private static readonly Dictionary<string, string> MenuCoveredByPalette = new()
    {
        ["OnSettings"] = "settings",
        ["OnDownloads"] = "downloads",
        ["OnBattleLab"] = "battle-lab-pane",
        ["OnPromptPad"] = "prompt-pad",
        ["OnFieldPass"] = "field-pass",
        // v0.14.0: the queue's head without opening the pane.
        ["OnBattleNextQueued"] = "battle-next-queue",
        ["OnShortcuts"] = "shortcuts",
        ["OnDiagnostics"] = "diagnostics",
        ["OnSessionWindow"] = "session",
        ["OnHelp"] = "help",
        ["OnCheckForUpdates"] = "check-updates",
        ["OnConnectionSecurity"] = "connection-security",
        ["OnViewLog"] = "view-log",
        ["OnOpenLogFolder"] = "open-log-folder",
        ["OnExportSupportBundle"] = "export-support-bundle",
        ["OnAbout"] = "about",
        ["OnLockNow"] = "lock-now",
        ["OnToggleFocusMode"] = "focus-mode",
        ["OnTogglePin"] = "always-on-top",
        ["OnSelectStandardLayout"] = "layout-standard",
        ["OnSelectLeftRailLayout"] = "layout-left-rail",
        ["OnCycleLayout"] = "layout-cycle",
        ["OnFind"] = "find",
        ["OnBack"] = "back",
        ["OnForward"] = "forward",
        ["OnHome"] = "home",
        ["OnReload"] = "reload",
        ["OnRestoreClosedPage"] = "restore-closed",
        ["OnZoomIn"] = "zoom-in",
        ["OnZoomOut"] = "zoom-out",
        ["OnPrint"] = "print",
        ["OnSaveAsPdf"] = "save-as-pdf",
        ["OnCopyPageAddress"] = "copy-address",
        ["OnOpenInBrowser"] = "open-in-browser",
        ["OnOpenInNewWindow"] = "open-in-new-window",
    };

    /// <summary>
    /// Menu handlers that are deliberately not palette commands, each with
    /// the reason. Anything added here is a decision, not an oversight.
    /// </summary>
    private static readonly Dictionary<string, string> MenuExemptFromPalette = new()
    {
        // The palette itself, and the two buttons that open a menu.
        ["OnCommandPalette"] = "it opens the palette; a palette row for it would be a loop",
        ["OnMenu"] = "opens the ⋯ menu this list is derived from",
        ["OnRailMenu"] = "opens the ⋯ menu in the Left Rail layout",
        ["OnRailProfiles"] = "opens the Left Rail profile flyout",
        // Parameterised rows: one handler, nine modes, covered by mode:<id>.
        ["OnArenaMode"] = "parameterised by its Tag; covered by the nine mode: commands",
        ["OnRailArenaMode"] = "the Left Rail's copy of the mode rows",
        // Components of a surface that is opened from the palette.
        ["OnFindClose"] = "a control in the find bar, which the find command opens",
        ["OnFindNext"] = "a control in the find bar",
        ["OnFindPrevious"] = "a control in the find bar",
        ["OnFindOptionChanged"] = "a checkbox in the find bar",
        ["OnFindOptionsPopup"] = "a control in the find bar",
        ["OnCancelDownload"] = "a button in the downloads surface, which the downloads command opens",
        // State-dependent rows: present only while a mode is pinned.
        ["OnPinCurrentMode"] = "offered only while browsing a mode, and it changes state the palette does not model",
        ["OnUnpinMode"] = "the same row, while something is pinned",
        ["OnGoPinnedMode"] = "the status bar's jump to the pinned mode",
        // Recovery prompts own their buttons.
        ["OnWatchdogReload"] = "a button on the recovery overlay",
        ["OnWatchdogWait"] = "a button on the recovery overlay",
        // The zoom-preset rows share one handler with a Tag; the palette
        // generates one command per preset instead.
        ["OnZoomPreset"] = "one handler for five rows; the palette has one command per preset",
        ["OnToggleCompactFocusLayout"] = "the checkable twin of the layout-compact command",
    };

    [Fact]
    public void EveryMenuItemHandlerIsACoveredPaletteCommand_OrAWrittenExemption()
    {
        var xaml = Source("src/ArenaDesktop/MainWindow.xaml");
        var handlers = System.Text.RegularExpressions.Regex
            .Matches(xaml, "Click=\"(On[A-Za-z]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(handlers);

        foreach (var handler in handlers)
        {
            var covered = MenuCoveredByPalette.ContainsKey(handler);
            var exempt = MenuExemptFromPalette.ContainsKey(handler);
            Assert.True(covered || exempt,
                $"{handler} is a menu item with no palette command and no written exemption");
            Assert.False(covered && exempt, $"{handler} is both covered and exempt");
        }
    }

    [Fact]
    public void EveryMappingNamesARealCommand_AndNoMappingIsStale()
    {
        foreach (var (_, id) in MenuCoveredByPalette)
        {
            Assert.NotNull(CommandPalette.ById(id));
        }
        // Exemptions must carry a reason — a silent exemption is how this
        // list would rot.
        Assert.All(MenuExemptFromPalette, pair =>
            Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{pair.Key} has no reason"));

        // And no stale entries: a mapping for a handler the XAML no longer
        // has means a menu item was removed and the promise was not.
        var xaml = Source("src/ArenaDesktop/MainWindow.xaml");
        foreach (var handler in MenuCoveredByPalette.Keys.Concat(MenuExemptFromPalette.Keys))
        {
            Assert.Contains($"Click=\"{handler}\"", xaml);
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
            // Generated ids carry their argument in the id and are
            // dispatched by prefix, so the case is the prefix itself.
            if (command.Id.StartsWith(CommandPalette.ModePrefix, StringComparison.Ordinal)
                || command.Id.StartsWith(CommandPalette.ZoomPresetPrefix, StringComparison.Ordinal))
                continue;
            Assert.Contains($"case \"{command.Id}\"", shell);
        }
        Assert.Contains("CommandPalette.ModePrefix", shell);
        Assert.Contains("CommandPalette.ZoomPresetFor(id)", shell);
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
