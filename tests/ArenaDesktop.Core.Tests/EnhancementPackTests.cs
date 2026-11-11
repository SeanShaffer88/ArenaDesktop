using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// The enhancement pack (v0.4.0): twenty small, self-contained
/// improvements across find, zoom, session facts, error wording,
/// shortcuts, the Battle Lab, downloads, startup, and settings. Every
/// rule lives in Core so the shell only draws it; these tests pin the
/// rules — sizes, thresholds, ordering, and the wording people read.
/// </summary>
public class EnhancementPackTests
{
    // ─────────────────────── 1. find options ───────────────────────

    [Fact]
    public void FindOptions_UsableTermsAndTheirRefusals()
    {
        Assert.True(FindOptionsPolicy.IsUsableTerm("leaderboard", 200, out var reason));
        Assert.Null(reason);
        Assert.True(FindOptionsPolicy.IsUsableTerm("a+b*", 200, out _)); // a literal, not a pattern
        Assert.False(FindOptionsPolicy.IsUsableTerm(null, 200, out var blank));
        Assert.Equal("Type something to find.", blank);
        Assert.False(FindOptionsPolicy.IsUsableTerm("   ", 200, out _));
        Assert.False(FindOptionsPolicy.IsUsableTerm(new string('x', 201), 200, out var tooLong));
        Assert.Contains("too long", tooLong);
    }

    [Fact]
    public void FindOptions_SummaryNamesOnlyTheOptionsThatAreOn()
    {
        Assert.Equal("", FindOptionsPolicy.OptionsSummary(false, false));
        Assert.Equal("Whole word", FindOptionsPolicy.OptionsSummary(true, false));
        Assert.Equal("Match case", FindOptionsPolicy.OptionsSummary(false, true));
        Assert.Equal("Whole word · Match case", FindOptionsPolicy.OptionsSummary(true, true));
    }

    [Fact]
    public void FindOptions_ProgressTextInvitesNoInvention()
    {
        Assert.Equal("No results", FindOptionsPolicy.ProgressText(0, -1));
        Assert.Equal("1 match", FindOptionsPolicy.ProgressText(1, -1));
        Assert.Equal("12 matches", FindOptionsPolicy.ProgressText(12, -1));
        // WebView2's active index is 0-based; people count from 1.
        Assert.Equal("1 of 4", FindOptionsPolicy.ProgressText(4, 0));
        Assert.Equal("4 of 4", FindOptionsPolicy.ProgressText(4, 3));
        // An index outside the matches (a stale callback) never prints "0 of 4" or "5 of 4".
        Assert.Equal("4 matches", FindOptionsPolicy.ProgressText(4, 9));
    }

    [Fact]
    public void FindBar_RepeatsTheSameTermWithoutRestarting()
    {
        Assert.False(FindBarKeys.NeedsRestart("arena", " arena "));
        Assert.True(FindBarKeys.NeedsRestart("arena", "arena.ai"));
        Assert.True(FindBarKeys.NeedsRestart("", "x"));
        Assert.Equal("", FindBarKeys.NormalizeTerm(null));
        Assert.Contains("nothing to step through", FindBarKeys.NextTooltip(0));
        Assert.DoesNotContain("nothing", FindBarKeys.NextTooltip(3));
    }

    // ─────────────────────── 2. zoom presets ───────────────────────

    [Fact]
    public void ZoomPresets_AreRealStepsAndMatchTheirLabels()
    {
        foreach (var preset in ZoomPresets.All)
        {
            Assert.Contains(preset.Factor, ZoomMath.Steps);
            Assert.Equal(ZoomMath.FormatPercent(preset.Factor), preset.Display);
        }
        Assert.Equal("100% (actual size)", ZoomPresets.Find("actual")!.Label);
        Assert.Null(ZoomPresets.Find("nope"));
        Assert.Null(ZoomPresets.Find(null));
    }

    [Fact]
    public void ZoomPresets_AvailabilityFollowsTheRange()
    {
        Assert.All(ZoomPresets.All, p => Assert.True(ZoomPresets.IsAvailable(p, extendedRange: false)));
        var items = ZoomPresets.MenuItems(extendedRange: false);
        Assert.Equal(ZoomPresets.All.Count, items.Count);
        Assert.All(items, i => Assert.True(i.Available));
        // The menu never offers a factor the range would clamp away from.
        foreach (var (preset, available) in items)
            Assert.Equal(available, preset.Factor <= ZoomMath.Max);
    }

    // ─────────────────────── 3. session stats ───────────────────────

    [Fact]
    public void SessionStats_CountsWhatHappened_AndNothingElse()
    {
        var start = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        var stats = new SessionStats(start);
        stats.RecordPage(ArenaModes.LeaderboardId);
        stats.RecordPage("not-a-mode"); // lands under "Not recorded", never invented
        stats.RecordModeJump();
        stats.RecordModeJump();
        stats.RecordDownloadCompleted();
        stats.RecordPromptCopied();
        stats.AddTime(TimeSpan.FromMinutes(90));
        Assert.Equal(2, stats.PagesVisited);
        Assert.Equal(2, stats.ModeJumps);
        Assert.Equal(1, stats.DownloadsCompleted);
        Assert.Equal(1, stats.PromptsCopied);
        Assert.Equal(TimeSpan.FromMinutes(90), stats.TimeOnArena);
        var byMode = stats.TimeByMode();
        Assert.Equal(2, byMode.Count);
        Assert.Contains(byMode, t => t.Label == "Leaderboard" && t.Duration == TimeSpan.FromMinutes(90));
        Assert.Contains(byMode, t => t.Label == "Not recorded");
    }

    [Fact]
    public void SessionStats_IgnoresImpossibleDurations()
    {
        var stats = new SessionStats();
        Assert.Equal(TimeSpan.Zero, stats.TimeOnArena);
        stats.AddTime(TimeSpan.FromSeconds(-5));
        stats.AddTime(TimeSpan.FromHours(9)); // a sleep gap is not attention
        Assert.Equal(TimeSpan.Zero, stats.TimeOnArena);
        Assert.Empty(stats.Visits);
    }

    [Fact]
    public void SessionStats_VisitListIsCapped()
    {
        var stats = new SessionStats();
        for (var i = 0; i < SessionStats.MaxVisits + 20; i++)
            stats.RecordPage();
        Assert.Equal(SessionStats.MaxVisits, stats.Visits.Count);
        Assert.Equal(SessionStats.MaxVisits + 20, stats.PagesVisited);
    }

    [Fact]
    public void SessionStats_SummaryNamesTheLimitsOfTheNumbers()
    {
        var start = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        var stats = new SessionStats(start);
        stats.RecordPage(ArenaModes.BattleId);
        stats.AddTime(TimeSpan.FromMinutes(12));
        var lines = stats.SummaryLines(start.AddMinutes(20));
        Assert.Contains(lines, l => l.Contains("20 min ago"));
        Assert.Contains(lines, l => l.StartsWith("On Arena: 12 min"));
        Assert.Contains(lines, l => l.Contains("this session only"));
    }

    [Theory]
    [InlineData(0, 0, 0, "none")]
    [InlineData(0, 0, 30, "under a minute")]
    [InlineData(0, 3, 0, "3 min")]
    [InlineData(1, 2, 0, "1 h 2 min")]
    [InlineData(2, 0, 0, "2 h")]
    public void SessionStats_FormatDuration_RoundsDownToMinutes(int hours, int minutes, int seconds, string expected) =>
        Assert.Equal(expected, SessionStats.FormatDuration(new TimeSpan(hours, minutes, seconds)));

    // ─────────────────────── 4. error code lookup ───────────────────────

    [Fact]
    public void ErrorLookup_KnownCodesGetTheirOwnWords()
    {
        var dns = ErrorCodeLookup.For("HostNameNotResolved");
        Assert.Equal("HOSTNAMENOTRESOLVED", dns.Code);
        Assert.Contains("address", dns.Cause);
        Assert.Equal(dns.Line, ErrorCodeLookup.LineFor("hostnamenotresolved"));
        Assert.Contains("date and time", ErrorCodeLookup.For("CertificateExpired").NextStep);
    }

    [Fact]
    public void ErrorLookup_UnknownAndNullGetTheGenericEntry()
    {
        foreach (var status in new[] { null, "", "   ", "SOMETHINGNEW" })
        {
            var entry = ErrorCodeLookup.For(status);
            Assert.Equal("UNKNOWN", entry.Code);
            Assert.Contains("didn't load", entry.Cause);
        }
    }

    [Fact]
    public void ErrorLookup_EveryEntryIsFullyWritten()
    {
        Assert.All(ErrorCodeLookup.All, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Code));
            Assert.False(string.IsNullOrWhiteSpace(e.Cause));
            Assert.False(string.IsNullOrWhiteSpace(e.NextStep));
        });
        Assert.Equal(ErrorCodeLookup.All.Count, ErrorCodeLookup.All.Select(e => e.Code).Distinct().Count());
    }

    // ─────────────────────── 5. shortcut cheat sheet ───────────────────────

    [Fact]
    public void CheatSheet_GroupsCoverTheWholeCatalogExactlyOnce()
    {
        var grouped = ShortcutCheatSheet.Grouped(null);
        var flattened = grouped.SelectMany(g => g.Actions).ToList();
        Assert.Equal(ShortcutCatalog.All.Count, flattened.Count);
        Assert.Equal(ShortcutCatalog.All.Count, flattened.Distinct().Count());
        Assert.All(grouped, g => Assert.Contains(g.Group, ShortcutCheatSheet.GroupOrder));
    }

    [Fact]
    public void CheatSheet_SearchesNameGesturesAndGroup()
    {
        Assert.Contains(ShortcutCheatSheet.Search("zoom"), a => a.Id == "zoom-in");
        Assert.Contains(ShortcutCheatSheet.Search("ctrl+f"), a => a.Id == "find");
        Assert.Contains(ShortcutCheatSheet.Search("arena modes"), a => a.Id == "mode-battle");
        Assert.Empty(ShortcutCheatSheet.Search("zanzibar"));
        Assert.Equal(ShortcutCatalog.All.Count, ShortcutCheatSheet.Search("  ").Count);
    }

    [Fact]
    public void CheatSheet_SummaryCountsTheFilter()
    {
        Assert.Equal($"{ShortcutCatalog.All.Count} shortcuts · click a row to copy its keys",
            ShortcutCheatSheet.SummaryText(ShortcutCatalog.All.Count, ShortcutCatalog.All.Count));
        Assert.Contains("2 of", ShortcutCheatSheet.SummaryText(2, ShortcutCatalog.All.Count));
    }

    // ─────────────────────── 6. pasted-text policy ───────────────────────

    [Fact]
    public void PastedText_WarnsOnlyPastTheLimit_AndSaysTheSize()
    {
        var under = new string('a', PastedTextPolicy.WarningLength);
        var over = new string('a', PastedTextPolicy.WarningLength + 1);
        Assert.False(PastedTextPolicy.NeedsWarning(under));
        Assert.True(PastedTextPolicy.NeedsWarning(over));
        Assert.False(PastedTextPolicy.NeedsWarning(null));
        Assert.Contains((PastedTextPolicy.WarningLength + 1).ToString("N0"), PastedTextPolicy.WarningText(over));
        Assert.Contains(".txt", PastedTextPolicy.WarningText(over));
    }

    [Fact]
    public void PastedText_PreviewCollapsesWhitespaceAndEllipsizes()
    {
        Assert.Equal("one two three", PastedTextPolicy.Preview("one   two\n\nthree  "));
        var longText = string.Join(' ', Enumerable.Repeat("word", 200));
        var preview = PastedTextPolicy.Preview(longText);
        Assert.True(preview.Length <= PastedTextPolicy.PreviewLength + 1);
        Assert.EndsWith("…", preview);
        Assert.Equal("", PastedTextPolicy.Preview("   "));
    }

    [Fact]
    public void PastedText_ExportFileNameIsSafeAndDated()
    {
        // Built with the PC's own offset so the export's local-date stamp is deterministic.
        var localNoon = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Local);
        var now = new DateTimeOffset(localNoon);
        Assert.Equal("Code review 2026-11-11.txt", PastedTextPolicy.ExportFileName("Code review", now));
        Assert.Equal("what-now 2026-11-11.txt", PastedTextPolicy.ExportFileName("what?/now", now));
        Assert.Equal("prompt 2026-11-11.txt", PastedTextPolicy.ExportFileName("   ", now));
        Assert.DoesNotContain(Path.DirectorySeparatorChar, PastedTextPolicy.ExportFileName("a/b", now));
    }

    // ─────────────────────── 7. downloads sort and bands ───────────────────────

    private static DownloadRecord Record(string name, long size, DownloadRecordState state = DownloadRecordState.Completed, int day = 1) =>
        new()
        {
            FileName = name,
            FullPath = @"C:\Downloads\" + name,
            SizeBytes = size,
            CompletedUtc = new DateTimeOffset(2026, 11, day, 12, 0, 0, TimeSpan.Zero),
            State = state,
        };

    [Fact]
    public void DownloadsView_ComposesSearchStateSizeAndSort()
    {
        var records = new[]
        {
            Record("diagram.png", 2 * DownloadSizeBands.Mb, day: 1),
            Record("diagram-big.png", 40 * DownloadSizeBands.Mb, day: 2),
            Record("diagram-failed.png", 50 * DownloadSizeBands.Mb, DownloadRecordState.Failed, day: 3),
            Record("notes.txt", 2 * DownloadSizeBands.Mb, day: 4),
        };

        var big = DownloadsView.Apply(records, "diagram", DownloadStateFilter.All, DownloadSizeBands.Find("over-10mb"), DownloadsSortOrder.NewestFirst);
        Assert.Equal(new[] { "diagram-failed.png", "diagram-big.png" }, big.Select(r => r.FileName));

        var completed = DownloadsView.Apply(records, "diagram", DownloadStateFilter.Completed, null, DownloadsSortOrder.SmallestFirst);
        Assert.Equal(new[] { "diagram.png", "diagram-big.png" }, completed.Select(r => r.FileName));

        var oldest = DownloadsView.Apply(records, null, DownloadStateFilter.All, null, DownloadsSortOrder.OldestFirst);
        Assert.Equal("diagram.png", oldest[0].FileName);
        Assert.Equal("notes.txt", oldest[^1].FileName);

        Assert.NotEmpty(DownloadsView.Apply(records, null, DownloadStateFilter.All, null, DownloadsSortOrder.LargestFirst));
    }

    [Fact]
    public void DownloadsView_BandsDoNotOverlapAndExcludeTheMax()
    {
        Assert.True(DownloadSizeBands.Find("under-1mb")!.Contains(DownloadSizeBands.Mb - 1));
        Assert.False(DownloadSizeBands.Find("under-1mb")!.Contains(DownloadSizeBands.Mb));
        Assert.True(DownloadSizeBands.Find("1-10mb")!.Contains(DownloadSizeBands.Mb));
        Assert.False(DownloadSizeBands.Find("1-10mb")!.Contains(10 * DownloadSizeBands.Mb));
        Assert.True(DownloadSizeBands.Find("over-10mb")!.Contains(10 * DownloadSizeBands.Mb));
        Assert.True(DownloadSizeBands.Find("over-100mb")!.Contains(100 * DownloadSizeBands.Mb));
        // Every size lands in at least one of the first three bands.
        foreach (var size in new long[] { 0, 1, DownloadSizeBands.Mb, 5 * DownloadSizeBands.Mb, 10 * DownloadSizeBands.Mb, 999 * DownloadSizeBands.Mb })
            Assert.Contains(DownloadSizeBands.All.Take(3), b => b.Contains(size));
        Assert.Null(DownloadSizeBands.Find("nope"));
    }

    [Fact]
    public void DownloadsView_SummaryShowsCountAndSize()
    {
        var shown = new[] { Record("a.png", 1024), Record("b.png", 2048) };
        var summary = DownloadsView.SummaryText(shown);
        Assert.StartsWith("2 files · ", summary);
        Assert.Contains("KB", summary);
        Assert.Equal(3072, DownloadsView.TotalBytes(shown));
    }

    // ─────────────────────── 8. folder health ───────────────────────

    [Fact]
    public void FolderCheck_HealthyIsAWritableFolderWithSpace()
    {
        var good = new DownloadsFolderCheck(@"C:\Users\me\Downloads", true, true, 20L * 1024 * 1024 * 1024, null);
        Assert.True(good.Healthy);
        Assert.False(good.LowSpace);
        Assert.Contains("is ready", good.Line);
        Assert.Contains("free", good.Line);
    }

    [Fact]
    public void FolderCheck_EveryUnhealthyCaseSaysWhatToDo()
    {
        var missing = new DownloadsFolderCheck(@"D:\gone", false, false, null, null);
        Assert.False(missing.Healthy);
        Assert.Contains("doesn't exist", missing.Line);

        var readOnly = new DownloadsFolderCheck(@"C:\Program Files\x", true, false, 1L << 40, null);
        Assert.Contains("can't be written", readOnly.Line);

        var full = new DownloadsFolderCheck(@"C:\Downloads", true, true, 100L * 1024 * 1024, null);
        Assert.True(full.LowSpace);
        Assert.Contains("only", full.Line);
        Assert.False(full.Healthy);

        var failed = DownloadsFolderCheck.Failed(@"C:\Downloads", "access denied");
        Assert.False(failed.Healthy);
        Assert.Contains("access denied", failed.Line);

        var systemFolder = new DownloadsFolderCheck(null, false, false, null, null);
        Assert.Contains("not checked", systemFolder.Line);
        Assert.False(systemFolder.Healthy);
    }

    // ─────────────────────── 9. startup trend ───────────────────────

    private static StartupTimings Run(long? firstPageMs, int daysAgo = 0) => new()
    {
        StartedUtc = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero).AddDays(-daysAgo),
        FirstNavigationMs = firstPageMs,
    };

    [Fact]
    public void StartupTrend_NeedsEnoughRunsBeforeClaimingAnything()
    {
        Assert.Contains("No completed startup", StartupTrend.Summary(Array.Empty<StartupTimings>()));
        Assert.Contains("few more launches", StartupTrend.Summary(new[] { Run(1200) }));
        Assert.Contains("few more launches", StartupTrend.Summary(new[] { Run(1200), Run(null), Run(1300), Run(1250) }));
    }

    [Fact]
    public void StartupTrend_ReportsSlowerFasterAndSteady()
    {
        // Newest first: recent runs 3000 ms; older ones 1000 ms.
        var slower = new[] { Run(3000), Run(3100), Run(2900), Run(1000), Run(1000), Run(1100), Run(900), Run(1000) };
        Assert.Contains("slower", StartupTrend.Summary(slower));
        var faster = new[] { Run(1000), Run(1000), Run(1100), Run(3000), Run(3100), Run(2900), Run(3000), Run(3100) };
        Assert.Contains("faster", StartupTrend.Summary(faster));
        var steady = new[] { Run(2000), Run(2050), Run(1950), Run(2000), Run(2000), Run(2100), Run(1900), Run(2000) };
        Assert.Contains("steady", StartupTrend.Summary(steady));
    }

    // ─────────────────────── 10. duplicate prompts ───────────────────────

    [Fact]
    public void Duplicates_SameTextGroupsIgnoreCaseWhitespaceAndName()
    {
        var prompts = new[]
        {
            new BattlePrompt { Id = "a", Name = "One", Body = "Explain  quantum\nentanglement" },
            new BattlePrompt { Id = "b", Name = "Two", Body = "explain quantum entanglement " },
            new BattlePrompt { Id = "c", Name = "Three", Body = "Something else" },
        };
        var groups = DuplicatePrompts.SameTextGroups(prompts);
        Assert.Single(groups);
        Assert.Equal(new[] { "One", "Two" }, groups[0]);
    }

    [Fact]
    public void Duplicates_NamesGroupWithTheirCategories()
    {
        var prompts = new[]
        {
            new BattlePrompt { Id = "a", Name = "Compare", Category = "Writing", Body = "x" },
            new BattlePrompt { Id = "b", Name = " compare ", Category = "Coding", Body = "y" },
        };
        var names = DuplicatePrompts.SameNameGroups(prompts);
        Assert.Single(names);
        Assert.Equal(new[] { "Writing", "Coding" }, names[0]);
    }

    [Fact]
    public void Duplicates_ReportSaysNothingWhenTheLibraryIsClean()
    {
        var clean = new[] { new BattlePrompt { Id = "a", Name = "A", Body = "one" }, new BattlePrompt { Id = "b", Name = "B", Body = "two" } };
        Assert.Null(DuplicatePrompts.Report(clean));
        var dupes = new[]
        {
            new BattlePrompt { Id = "a", Name = "A", Body = "same" },
            new BattlePrompt { Id = "b", Name = "B", Body = "same" },
        };
        var report = DuplicatePrompts.Report(dupes);
        Assert.NotNull(report);
        Assert.Contains("identical text", report);
        Assert.Contains("A / B", report);
    }

    [Fact]
    public void Duplicates_EmptyBodiesAreNotDuplicatesOfEachOther()
    {
        var prompts = new[]
        {
            new BattlePrompt { Id = "a", Name = "A", Body = "   " },
            new BattlePrompt { Id = "b", Name = "B", Body = "" },
        };
        Assert.Empty(DuplicatePrompts.SameTextGroups(prompts));
        Assert.Equal("", DuplicatePrompts.Fold(" \n\t "));
    }

    // ─────────────────────── 11. pinned mode ───────────────────────

    [Fact]
    public void PinnedMode_SanitizesToRegistryIdsOnly()
    {
        Assert.Equal(ArenaModes.BattleId, PinnedMode.Sanitize("Battle"));
        Assert.Equal(ArenaModes.SideBySideId, PinnedMode.Sanitize("side by side"));
        Assert.Null(PinnedMode.Sanitize("nope"));
        Assert.Null(PinnedMode.Sanitize(null));
        Assert.Equal("Leaderboard", PinnedMode.Resolve("leaderboard")!.Label);
        Assert.Null(PinnedMode.Resolve("nope"));
    }

    [Fact]
    public void PinnedMode_LabelAndTooltipNameTheAction()
    {
        Assert.Null(PinnedMode.StatusLabel(null));
        Assert.Null(PinnedMode.StatusLabel("nope"));
        Assert.Equal("Pinned: Code", PinnedMode.StatusLabel("code"));
        var mode = ArenaModes.All.First(m => m.Id == ArenaModes.CodeId);
        Assert.Contains("go there", PinnedMode.Tooltip(mode));
    }

    [Fact]
    public void PinnedMode_IsAValidatedPreferenceThatResetsWithTheRest()
    {
        var settings = new AppSettings { PinnedModeId = "not a mode" }.Sanitized();
        Assert.Null(settings.PinnedModeId);
        settings.PinnedModeId = "video";
        settings.DimToolbarWhenReading = true;
        SettingsReset.ApplyDefaults(settings);
        Assert.Null(settings.PinnedModeId);
        Assert.False(settings.DimToolbarWhenReading);
        Assert.Equal("leaderboard", new AppSettings { PinnedModeId = " leaderboard " }.Sanitized().PinnedModeId);
        // Both ride the ordinary settings round trip (export → import).
        var exported = SettingsStore.Export(new AppSettings { PinnedModeId = "agent", DimToolbarWhenReading = true });
        Assert.True(SettingsStore.TryImport(exported, out var imported, out var importError), importError);
        Assert.Equal("agent", imported!.PinnedModeId);
        Assert.True(imported.DimToolbarWhenReading);
        // A file from before the pack has neither field and reads as "off".
        var legacy = new AppSettings().Sanitized();
        Assert.Null(legacy.PinnedModeId);
        Assert.False(legacy.DimToolbarWhenReading);
    }

    // ─────────────────────── 12. session report block ───────────────────────

    [Fact]
    public void SessionReport_StatesWhatTheNumbersAreAndAreNot()
    {
        var stats = new SessionStats(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));
        stats.RecordPage(ArenaModes.BattleId);
        stats.AddTime(TimeSpan.FromMinutes(5));
        var lines = SessionReport.Lines(stats, DateTimeOffset.UtcNow, "Battle");
        Assert.StartsWith("Session (this run only", lines[0]);
        Assert.Contains(lines, l => l.Contains("Pages 1"));
        Assert.Contains(lines, l => l.Contains("Pinned mode: Battle"));
        Assert.DoesNotContain(SessionReport.Lines(stats, DateTimeOffset.UtcNow, null), l => l.Contains("Pinned"));
    }

    // ─────────────────────── 13. quiet shell ───────────────────────

    [Fact]
    public void ShellQuiet_DimsOnlyWhileReadingWithTheSettingOn()
    {
        Assert.Equal(1.0, ShellQuiet.ToolbarOpacity(false, true, false));
        Assert.Equal(1.0, ShellQuiet.ToolbarOpacity(true, false, false));
        Assert.Equal(1.0, ShellQuiet.ToolbarOpacity(true, true, true));
        Assert.Equal(ShellQuiet.DimmedToolbarOpacity, ShellQuiet.ToolbarOpacity(true, true, false));
        Assert.InRange(ShellQuiet.DimmedToolbarOpacity, 0.6, 0.99);
        Assert.True(ShellQuiet.ToolbarOpacity(true, true, false) < ShellQuiet.ToolbarOpacity(true, true, true));
        Assert.Contains("never", ShellQuiet.DimSettingNote);
    }

    // ────────────────── 13b. update-check failure copy ──────────────────

    [Fact]
    public void UpdateFailure_SaysWhatWentWrongAndWhatToDoNext()
    {
        var unreachable = UpdateFailureText.Line(UpdateOutcomeKind.Unreachable);
        Assert.Contains("couldn't reach GitHub", unreachable);
        Assert.Contains("Check your connection", unreachable);
        Assert.Contains("keeps working offline", unreachable);
        var badData = UpdateFailureText.StatusLine(UpdateOutcomeKind.BadData);
        Assert.Contains("release list", badData);
        Assert.Contains("Try again", badData);
        // Every kind has words, and no line ever claims success.
        foreach (var kind in Enum.GetValues<UpdateOutcomeKind>())
        {
            var line = UpdateFailureText.Line(kind);
            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.EndsWith(".", line);
        }
        Assert.NotEqual(UpdateFailureText.Line(UpdateOutcomeKind.Unreachable),
            UpdateFailureText.Line(UpdateOutcomeKind.BadData));
        // The honest catch-all is a failure, never a success.
        Assert.Contains("didn't finish", UpdateFailureText.Explain(UpdateOutcomeKind.Failed));
        Assert.Contains("Try again later", UpdateFailureText.NextStep(UpdateOutcomeKind.Failed));
    }

    // ─────────────────────── 14. download notification text ───────────────────────

    [Fact]
    public void DownloadNotification_NamesTheFileSizeAndFolder()
    {
        Assert.Equal("Downloaded chart.png", DownloadNotificationText.Title("chart.png", 1));
        Assert.Equal("3 downloads finished", DownloadNotificationText.Title("chart.png", 3));
        var body = DownloadNotificationText.Body(2048, @"C:\Users\me\Downloads", 1);
        Assert.Contains("saved to", body);
        Assert.Contains(@"C:\Users\me\Downloads", body);
        var batch = DownloadNotificationText.Body(5 * 1024 * 1024, null, 3);
        Assert.Contains("in total", batch);
        Assert.Contains("Downloads folder", batch);
        // An unknown size (the history save hasn't landed) is never
        // reported as 0 B — the line simply leaves the number out.
        var unknown = DownloadNotificationText.Body(null, null, 1);
        Assert.Equal("Saved to your Downloads folder", unknown);
        Assert.DoesNotContain("0 B", DownloadNotificationText.Body(null, @"C:\temp", 2));
    }

    // ─────────────────────── 15. arrival notice ───────────────────────

    [Fact]
    public void ArrivalNotice_NamesTheModeAndItsKeys()
    {
        var mode = ArenaModes.All.First(m => m.Id == ArenaModes.LeaderboardId);
        Assert.Equal("Arrived at Leaderboard · Ctrl+4", ArrivalNotice.ForMode(mode));
        Assert.Equal(ArrivalNotice.ForMode(mode), ArrivalNotice.ForModeId("leaderboard"));
        Assert.Equal("Page loaded", ArrivalNotice.ForModeId("nope"));
        Assert.True(ArrivalNotice.DisplayDuration > TimeSpan.FromSeconds(2));
        Assert.True(ArrivalNotice.DisplayDuration < TimeSpan.FromSeconds(8));
    }

    // ─────────────────────── 16. startup restore ───────────────────────

    [Fact]
    public void StartupRestore_OnlyReusesUsableArenaPages()
    {
        Assert.True(StartupRestore.IsUsableLastPage("https://arena.ai/text/direct"));
        Assert.True(StartupRestore.IsUsableLastPage("https://arena.ai/chat/abc"));
        Assert.False(StartupRestore.IsUsableLastPage(null));
        Assert.False(StartupRestore.IsUsableLastPage("http://arena.ai/"));
        Assert.False(StartupRestore.IsUsableLastPage("https://accounts.google.com/signin"));
        Assert.False(StartupRestore.IsUsableLastPage("https://evil.example/x"));
        Assert.False(StartupRestore.IsUsableLastPage("not a url"));
    }

    [Fact]
    public void StartupRestore_SafeModeWinsAndFallbacksAreTotal()
    {
        var leaderboard = ArenaModes.All.First(m => m.Id == ArenaModes.LeaderboardId);
        Assert.Equal(UrlPolicy.HomeUrl, StartupRestore.Resolve(true, "https://arena.ai/chat/abc", leaderboard));
        Assert.Equal("https://arena.ai/chat/abc", StartupRestore.Resolve(false, "https://arena.ai/chat/abc", leaderboard));
        Assert.Equal(leaderboard.Url, StartupRestore.Resolve(false, "https://example.com", leaderboard));
        Assert.Equal(leaderboard.Url, StartupRestore.Resolve(false, null, leaderboard));
        Assert.Contains("Safe mode", StartupRestore.RestoredNote(true, true));
        Assert.Contains("left off", StartupRestore.RestoredNote(false, true));
        Assert.Equal("", StartupRestore.RestoredNote(false, false));
    }

    // ─────────────────────── 17. run queue rebase ───────────────────────

    [Fact]
    public void RunQueue_MovesWithinBoundsOnly()
    {
        var queue = new List<string> { "a", "b", "c" };
        Assert.True(RunQueue.Move(queue, 0, 1));
        Assert.Equal(new[] { "b", "a", "c" }, queue);
        Assert.False(RunQueue.Move(queue, 0, -1));
        Assert.False(RunQueue.Move(queue, 2, 1));
        Assert.False(RunQueue.Move(queue, 9, -1));
        // The library's reorder is this same rule (no file bound, so
        // nothing is written): a step other than one place is refused
        // and the order is left exactly as it was.
        var library = new BattleLabLibrary(new BattleLabData());
        foreach (var name in new[] { "A", "B", "C" })
            library.AddPrompt(name, "General", $"body {name}");
        foreach (var prompt in library.Prompts.ToList())
            library.Enqueue(prompt.Id);
        Assert.True(library.MoveInQueue(0, 1));
        Assert.Equal(new[] { "B", "A", "C" }, library.QueuePrompts.Select(p => p.Name));
        Assert.False(library.MoveInQueue(0, 2));
        Assert.False(library.MoveInQueue(0, -1));
        Assert.Equal(new[] { "B", "A", "C" }, library.QueuePrompts.Select(p => p.Name));
        Assert.False(RunQueue.Move(queue, -1, 1));
        Assert.False(RunQueue.Move(queue, 1, 0));
        Assert.Equal(new[] { "b", "a", "c" }, queue);
    }

    [Fact]
    public void RunQueue_PositionLabelAndOrphanCount()
    {
        Assert.Equal("2 of 5 · Compare wars", RunQueue.PositionLabel(1, 5, "Compare wars"));
        Assert.Contains("deleted prompt", RunQueue.PositionLabel(0, 2, "  "));
        var prompts = new[] { new BattlePrompt { Id = "a" }, new BattlePrompt { Id = "b" } };
        Assert.Equal(1, RunQueue.Orphans(new[] { "a", "b", "gone" }, prompts));
        Assert.Equal(0, RunQueue.Orphans(Array.Empty<string>(), prompts));
    }

    // ─────────────────────── diagnostics additions ───────────────────────

    [Fact]
    public void DiagnosticsReport_RendersTheSessionAndTrendBlocksOnlyWhenCollected()
    {
        var data = new DiagnosticsData
        {
            AppVersion = "0.4.0",
            SettingsPath = @"C:\Data\settings.json",
            LogPath = @"C:\Data\arenadesktop.log",
            ProfileDir = @"C:\Data\WebView2",
            SessionLines = new[] { "Session (this run only — nothing is saved)", "  Pages 3" },
            StartupTrendLine = "Startup is steady — first page about 1,900 ms over the last 3 launches.",
        };
        var text = DiagnosticsReport.Compose(data);
        Assert.Contains("Session (this run only", text);
        Assert.Contains("  Pages 3", text);
        Assert.Contains("Trend: Startup is steady", text);

        // Collected-but-empty / not collected both leave the report's old shape.
        var plain = DiagnosticsReport.Compose(new DiagnosticsData
        {
            AppVersion = "0.4.0",
            SettingsPath = "s",
            LogPath = "l",
            ProfileDir = "p",
        });
        Assert.DoesNotContain("Session (this run only", plain);
        Assert.DoesNotContain("Trend:", plain);
    }

    // ─────────────────────── pack text stays honest ───────────────────────

    [Fact]
    public void PackText_HasNoUnfinishedPlaceholders()
    {
        foreach (var text in new[]
                 {
                     PackText.FindOptionsTooltip, PackText.ZoomPresetsHeader, PackText.SessionWindowTitle,
                     PackText.ErrorLookupHeader, PackText.QueueReorderTooltip, PackText.DuplicatesButton,
                     PackText.PinModeMenuHeader, PackText.PinnedModeStatusPrefix, PackText.RestartToApply,
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("TODO", text);
            Assert.DoesNotContain("XXX", text);
        }
    }
}
