using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// The hardening pack: durability, write coalescing, bounded reads,
/// clipboard lifetime, readiness, re-entry guards and speakable status.
/// Each of these is a small rule with a consequence a user can feel — a
/// lost settings file, a settings write storm, a prompt left on the
/// clipboard, a status message only sighted users notice — so each rule
/// is pinned here rather than left to the shell's wiring.
/// </summary>
public class HardeningPackTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ad-hardening-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ─────────────────────── 1. atomic writes ───────────────────────

    [Fact]
    public void AtomicWrite_ReplacesTheFileAndLeavesNoLitter()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "settings.json");
            AtomicFile.WriteAllText(path, "first");
            Assert.Equal("first", File.ReadAllText(path));
            AtomicFile.WriteAllText(path, "second");
            Assert.Equal("second", File.ReadAllText(path));
            // The temp file is renamed, never left behind.
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AtomicWrite_CreatesMissingDirectories()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "nested", "deeper", "file.json");
            AtomicFile.WriteAllText(path, "content");
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AtomicWrite_IsUtf8WithoutABom()
    {
        // A BOM in settings.json would make the first key unparsable to
        // some readers and shows up as a stray character to a person
        // opening the file to look for a setting.
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "no-bom.txt");
            AtomicFile.WriteAllText(path, "plain");
            var bytes = File.ReadAllBytes(path);
            Assert.NotEqual(0xEF, bytes[0]);
            Assert.Equal("plain", System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AtomicWrite_TwoWritersNeverShareATempName()
    {
        // Two saves racing (a settings write and an app-lock write) must
        // not scribble over one another's temp file. The names are
        // unique per call, so the last swap wins and the file is valid.
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "race.json");
            Parallel.For(0, 20, i => AtomicFile.WriteAllText(path, $"value-{i}"));
            var text = File.ReadAllText(path);
            Assert.StartsWith("value-", text);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Stores_AllSaveThroughTheAtomicWriter()
    {
        // The behavioural proof: a settings round trip still works after
        // being re-pointed at AtomicFile, and a save leaves exactly one
        // file in the folder (no .tmp left over).
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "settings.json");
            SettingsStore.Save(path, new AppSettings { ZoomFactor = 1.5 });
            var loaded = SettingsStore.Load(path);
            Assert.Equal(1.5, loaded.ZoomFactor);
            Assert.Single(Directory.GetFiles(dir));

            var battlePath = Path.Combine(dir, "battle-lab.json");
            var data = new BattleLabData();
            BattleLabStore.Save(battlePath, data);
            Assert.NotNull(BattleLabStore.Load(battlePath));
            Assert.Single(Directory.GetFiles(dir, "battle-lab*"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ─────────────────────── 2. damaged-file preservation ───────────────────────

    [Fact]
    public void CorruptFile_PreservesACopyWithAReadableStamp()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "broken.json");
            File.WriteAllText(path, "{ not json");
            var stamp = new DateTimeOffset(2026, 11, 11, 14, 32, 5, TimeSpan.Zero);
            var copy = CorruptFile.Preserve(path, stamp);
            Assert.NotNull(copy);
            Assert.True(File.Exists(copy));
            Assert.Contains(CorruptFile.Marker, copy);
            Assert.Contains("20261111-143205", copy);
            Assert.Equal("{ not json", File.ReadAllText(copy!));
            // The original is untouched — the caller decides its fate.
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CorruptFile_DoesNotOverwriteAnEarlierCopy()
    {
        // Two corruptions in the same second must keep BOTH copies: the
        // first one is the file the user actually wants back.
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "broken.json");
            File.WriteAllText(path, "first");
            var stamp = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
            var a = CorruptFile.Preserve(path, stamp);
            File.WriteAllText(path, "second");
            var b = CorruptFile.Preserve(path, stamp);
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.NotEqual(a, b);
            Assert.Equal("first", File.ReadAllText(a!));
            Assert.Equal("second", File.ReadAllText(b!));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CorruptFile_MissingOrUnwritableIsNull_NotAThrow()
    {
        Assert.Null(CorruptFile.Preserve(null, DateTimeOffset.Now));
        Assert.Null(CorruptFile.Preserve("", DateTimeOffset.Now));
        Assert.Null(CorruptFile.Preserve(
            Path.Combine(Path.GetTempPath(), "ad-does-not-exist-" + Guid.NewGuid().ToString("N")),
            DateTimeOffset.Now));
    }

    [Fact]
    public void DownloadsStore_PreservesAFileItCannotParse()
    {
        // History is "only" a convenience record, but a file that exists
        // and won't parse is evidence — the next save would otherwise
        // replace it with an empty list and the evidence would be gone.
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "downloads.json");
            File.WriteAllText(path, "definitely not json");
            var loaded = DownloadHistoryStore.Load(path);
            Assert.Empty(loaded);
            Assert.Single(Directory.GetFiles(dir, "*" + CorruptFile.Marker + "*"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void BattleLabStore_PreservesALibraryItCannotParse()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "battle-lab.json");
            File.WriteAllText(path, "{oops");
            var loaded = BattleLabStore.Load(path);
            Assert.Empty(loaded.Prompts);
            Assert.Single(Directory.GetFiles(dir, "*" + CorruptFile.Marker + "*"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ─────────────────────── 3. write coalescing ───────────────────────

    [Fact]
    public void Coalescer_FirstRequestWritesImmediately()
    {
        var coalescer = new SaveCoalescer();
        Assert.False(coalescer.IsPending);
        Assert.True(coalescer.Request(DateTimeOffset.UtcNow));
        Assert.False(coalescer.IsPending);
    }

    [Fact]
    public void Coalescer_BurstInsideTheWindowDefersOnce_AndIsNeverDropped()
    {
        var start = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        var coalescer = new SaveCoalescer();
        Assert.True(coalescer.Request(start));
        coalescer.MarkWritten(start);

        // Three navigation-driven requests in the same second: the first
        // defers, the rest fold into that same pending write.
        Assert.False(coalescer.Request(start.AddMilliseconds(200)));
        Assert.False(coalescer.Request(start.AddMilliseconds(400)));
        Assert.False(coalescer.Request(start.AddMilliseconds(600)));
        Assert.True(coalescer.IsPending);

        // Still too early, then due exactly at the interval.
        Assert.False(coalescer.IsDue(start.AddSeconds(1)));
        Assert.True(coalescer.IsDue(start + SaveCoalescer.MinInterval));

        // Writing clears it, and an idle app is never due.
        coalescer.MarkWritten(start + SaveCoalescer.MinInterval);
        Assert.False(coalescer.IsPending);
        Assert.False(coalescer.IsDue(start.AddMinutes(5)));
    }

    [Fact]
    public void Coalescer_ForcedFlushTakesThePendingFlag()
    {
        var start = DateTimeOffset.UtcNow;
        var coalescer = new SaveCoalescer();
        Assert.True(coalescer.Request(start));
        coalescer.MarkWritten(start);
        Assert.False(coalescer.Request(start));            // deferred
        Assert.True(coalescer.TakePending());              // flushed
        Assert.False(coalescer.IsPending);
        Assert.False(coalescer.TakePending());             // nothing left
    }

    [Fact]
    public void Coalescer_ResetForgetsTheInterval()
    {
        var start = DateTimeOffset.UtcNow;
        var coalescer = new SaveCoalescer();
        Assert.True(coalescer.Request(start));
        coalescer.MarkWritten(start);
        coalescer.Reset();
        // After a reset the very next request writes at once — an import
        // or a settings reset must not sit in a pending state.
        Assert.True(coalescer.Request(start));
    }

    [Fact]
    public void WriteStats_ReportOnlyWhenSomethingHappened()
    {
        var stats = new SettingsWriteStats();
        Assert.Null(stats.ReportLine);
        stats.NoteWrite();
        stats.NoteDeferred();
        stats.NoteCoalesced();
        var line = stats.ReportLine;
        Assert.NotNull(line);
        Assert.Contains("Settings writes 1", line);
        Assert.Contains("deferred 1", line);
        // Counts only — no paths, no settings content.
        Assert.DoesNotContain("\\", line);
        Assert.DoesNotContain(":", line);
    }

    // ─────────────────────── 4. bounded reads ───────────────────────

    [Fact]
    public void TailReader_MatchesAWholeFileRead()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "log.txt");
            var lines = Enumerable.Range(1, 500).Select(i => $"line {i}").ToList();
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            // The whole-file answer, for comparison.
            var expected = LogReading.TailLines(File.ReadAllText(path), 5);
            Assert.Equal(expected, TextFileSlice.ReadTailLines(path, 5));
            Assert.Contains("line 500", TextFileSlice.ReadTailLines(path, 5));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TailReader_LargeFileReadsOnlyTheTail()
    {
        // A file far bigger than the window: the answer must still be the
        // LAST lines, not the first ones that happened to fit.
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "big.log");
            using (var writer = new StreamWriter(path))
            {
                for (var i = 0; i < 40000; i++)
                    writer.WriteLine($"row {i} padding padding padding");
            }
            var tail = TextFileSlice.ReadTailLines(path, 3, windowBytes: 8192);
            Assert.Contains("row 39999", tail);
            Assert.DoesNotContain("row 0 ", tail);
            Assert.Equal(3, tail.Split('\n').Length);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TailReader_MissingEmptyOrUnreadableIsEmpty()
    {
        var dir = TempDir();
        try
        {
            Assert.Equal("", TextFileSlice.ReadTailLines(null, 5));
            Assert.Equal("", TextFileSlice.ReadTailLines("", 5));
            Assert.Equal("", TextFileSlice.ReadTailLines(Path.Combine(dir, "nope.log"), 5));
            var empty = Path.Combine(dir, "empty.log");
            File.WriteAllText(empty, "");
            Assert.Equal("", TextFileSlice.ReadTailLines(empty, 5));
            Assert.Equal("", TextFileSlice.ReadTailLines(empty, 0));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SizeOrNull_ReportsBytesOrNull()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "sized.bin");
            File.WriteAllBytes(path, new byte[123]);
            Assert.Equal(123, TextFileSlice.SizeOrNull(path));
            Assert.Null(TextFileSlice.SizeOrNull(Path.Combine(dir, "missing.bin")));
            Assert.Null(TextFileSlice.SizeOrNull(null));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ─────────────────────── 5. clipboard lifetime ───────────────────────

    [Fact]
    public void ClipboardLifetime_SanitizesToTheOfferedRangeOrNever()
    {
        Assert.Equal(0, ClipboardLifetime.Sanitize(0));
        Assert.Equal(0, ClipboardLifetime.Sanitize(-5));
        Assert.Equal(0, ClipboardLifetime.Sanitize(999));
        Assert.Equal(1, ClipboardLifetime.Sanitize(1));
        Assert.Equal(15, ClipboardLifetime.Sanitize(15));
        Assert.All(ClipboardLifetime.OfferedMinutes, m => Assert.True(ClipboardLifetime.IsValid(m)));
    }

    [Fact]
    public void ClipboardLifetime_NeverMeansNoDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(ClipboardLifetime.ClearAt(now, 0));
        Assert.False(ClipboardLifetime.ShouldArm(0));
        Assert.Equal(now.AddMinutes(2), ClipboardLifetime.ClearAt(now, 2));
        Assert.True(ClipboardLifetime.ShouldArm(2));
    }

    [Fact]
    public void ClipboardLifetime_ClearsOnlyItsOwnText_AfterTheDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        var deadline = now.AddMinutes(2);
        // Too early.
        Assert.False(ClipboardLifetime.ShouldClear(now, deadline, "prompt text", "prompt text"));
        // Due, and still ours.
        Assert.True(ClipboardLifetime.ShouldClear(now.AddMinutes(3), deadline, "prompt text", "prompt text"));
        // Due, but the user copied something else in the meantime.
        Assert.False(ClipboardLifetime.ShouldClear(now.AddMinutes(3), deadline, "prompt text", "their text"));
        // Nothing was copied, or there is no deadline.
        Assert.False(ClipboardLifetime.ShouldClear(now.AddMinutes(3), deadline, null, null));
        Assert.False(ClipboardLifetime.ShouldClear(now.AddMinutes(3), null, "prompt", "prompt"));
        // An empty clipboard cannot contain our text.
        Assert.False(ClipboardLifetime.ShouldClear(now.AddMinutes(3), deadline, "prompt", ""));
    }

    [Fact]
    public void ClipboardLifetime_RemainingTextCountsDownAndStopsAtZero()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("", ClipboardLifetime.RemainingText(now, null));
        Assert.Equal("2 min left", ClipboardLifetime.RemainingText(now, now.AddMinutes(2)));
        Assert.Equal("under a minute left", ClipboardLifetime.RemainingText(now, now.AddSeconds(20)));
        Assert.Equal("clearing now", ClipboardLifetime.RemainingText(now, now.AddSeconds(-1)));
        Assert.Equal(TimeSpan.Zero, ClipboardLifetime.TimeLeft(now, now.AddSeconds(-5)));
    }

    // ─────────────────────── 6. readiness ───────────────────────

    [Fact]
    public void Readiness_AllGoodSaysSo()
    {
        var report = ReadinessReport.Compose(
            new RuntimeHealthResult(RuntimeHealthStatus.Ok, "131.0.2903.86", "ok"),
            settingsWritable: true, downloadsOk: true);
        Assert.True(report.CanRun);
        Assert.Equal("This PC is ready.", report.Headline);
        Assert.Contains(report.Lines, l => l.Contains("present") && l.Contains("131.0.2903.86"));
        Assert.Contains(report.Lines, l => l.Contains("writable"));
        Assert.Contains(report.Lines, l => l.Contains("ready"));
        Assert.Contains("This PC is ready.", report.Render());
    }

    [Fact]
    public void Readiness_MissingRuntimeOutranksEverythingElse()
    {
        var report = ReadinessReport.Compose(
            new RuntimeHealthResult(RuntimeHealthStatus.Missing, null, "missing"),
            settingsWritable: false, downloadsOk: false);
        Assert.False(report.CanRun);
        Assert.Contains("missing something", report.Headline);
        Assert.Contains(report.Lines, l => l.Contains("missing or too old"));
        // The runtime line is always first — nothing else matters without it.
        Assert.Contains("WebView2", report.Lines[0]);
    }

    [Fact]
    public void Readiness_OutdatedRuntimeIsNotAPass()
    {
        // Four statuses, one good one. Treating "outdated" as ready would
        // contradict the Diagnostics runtime panel on the same screen.
        var report = ReadinessReport.Compose(
            new RuntimeHealthResult(RuntimeHealthStatus.Outdated, "90.0.0.0", "old"),
            settingsWritable: true, downloadsOk: true);
        Assert.False(report.CanRun);
        Assert.Contains("missing something", report.Headline);
    }

    [Fact]
    public void Readiness_ReadOnlySettingsFolderIsNamedAsSuch()
    {
        var report = ReadinessReport.Compose(
            new RuntimeHealthResult(RuntimeHealthStatus.Ok, "131.0", "ok"),
            settingsWritable: false, downloadsOk: true);
        Assert.False(report.CanRun);
        Assert.Contains("can't save its settings", report.Headline);
        Assert.Contains(report.Lines, l => l.Contains("read-only"));
    }

    [Fact]
    public void Readiness_UnmeasuredDownloadsIsNotAFailure()
    {
        var report = ReadinessReport.Compose(
            new RuntimeHealthResult(RuntimeHealthStatus.Ok, "131.0", "ok"),
            settingsWritable: true, downloadsOk: null);
        Assert.True(report.CanRun);
        Assert.Equal("This PC is ready.", report.Headline);
        Assert.Contains(report.Lines, l => l.Contains("not checked yet"));
    }

    // ─────────────────────── 7. re-entry guard ───────────────────────

    [Fact]
    public void BusyGuard_RefusesASecondEntryAndReleasesOnDispose()
    {
        var guard = new BusyGuard();
        Assert.False(guard.IsBusy);
        var first = guard.TryEnter();
        Assert.True(first.Entered);
        Assert.True(guard.IsBusy);
        var second = guard.TryEnter();
        Assert.False(second.Entered);
        // Disposing the REFUSED scope must not release the real holder.
        second.Dispose();
        Assert.True(guard.IsBusy);
        first.Dispose();
        Assert.False(guard.IsBusy);
        // And the guard is reusable afterwards.
        using var third = guard.TryEnter();
        Assert.True(third.Entered);
    }

    [Fact]
    public void BusyGuard_DoubleDisposeIsHarmless()
    {
        var guard = new BusyGuard();
        var scope = guard.TryEnter();
        Assert.True(scope.Entered);
        scope.Dispose();
        scope.Dispose();
        Assert.False(guard.IsBusy);
        // Still usable: a stray double-dispose must not corrupt the state.
        var again = guard.TryEnter();
        Assert.True(again.Entered);
        again.Dispose();
    }

    [Fact]
    public void BusyGuard_UsingStatementPatternIsTheIntendedShape()
    {
        // The documented call pattern, exactly as a handler would use it:
        // no early return can leak the flag.
        var guard = new BusyGuard();
        var ran = 0;
        for (var i = 0; i < 3; i++)
        {
            using var scope = guard.TryEnter();
            if (!scope.Entered)
                continue;
            ran++;
        }
        Assert.Equal(3, ran);
        Assert.False(guard.IsBusy);
    }

    // ─────────────────────── 8. speakable status ───────────────────────

    [Fact]
    public void SpeakableStatus_SaysOutcomesAndSkipsChatter()
    {
        Assert.True(SpeakableStatus.ShouldSpeak("Copying to the clipboard failed — try again."));
        Assert.True(SpeakableStatus.ShouldSpeak("That link can't open inside ArenaDesktop"));
        Assert.True(SpeakableStatus.ShouldSpeak("Settings saved"));
        // Progress narration is the thing this must never read aloud.
        Assert.False(SpeakableStatus.ShouldSpeak("Loading Arena…"));
        Assert.False(SpeakableStatus.ShouldSpeak("Ready"));
        Assert.False(SpeakableStatus.ShouldSpeak("Downloading image.png — 42% (1.2 MB/s)"));
        Assert.False(SpeakableStatus.ShouldSpeak("Download complete: chart.png — click to open"));
        Assert.False(SpeakableStatus.ShouldSpeak("Download interrupted: file.bin"));
        // Progress lines that name what they're doing keep the prefix rule.
        Assert.False(SpeakableStatus.ShouldSpeak("Starting up"));
        Assert.False(SpeakableStatus.ShouldSpeak("Restoring your last page"));
        Assert.False(SpeakableStatus.ShouldSpeak("Paused"));
        Assert.False(SpeakableStatus.ShouldSpeak("Ready…"));
    }

    [Fact]
    public void SpeakableStatus_DoesNotSwallowOutcomesThatUseAProgressWord()
    {
        // "ready" and "paused" are chatter only when they ARE the line.
        // These are real results and must be spoken.
        Assert.True(SpeakableStatus.ShouldSpeak("This PC is ready."));
        Assert.True(SpeakableStatus.ShouldSpeak("Downloads paused by you."));
        Assert.True(SpeakableStatus.ShouldSpeak("ArenaDesktop is ready to paste."));
        Assert.True(SpeakableStatus.ShouldSpeak("already done"));
    }

    [Fact]
    public void SpeakableStatus_RefusesBlanksRepeatsAndMonologues()
    {
        Assert.False(SpeakableStatus.ShouldSpeak(null));
        Assert.False(SpeakableStatus.ShouldSpeak("   "));
        Assert.False(SpeakableStatus.ShouldSpeak("Settings saved", "Settings saved"));
        Assert.True(SpeakableStatus.ShouldSpeak("Settings saved", "Something else"));
        var longLine = new string('a', SpeakableStatus.MaxSpokenLength + 1);
        Assert.False(SpeakableStatus.ShouldSpeak(longLine));
    }

    [Fact]
    public void SpeakableStatus_SpokenFormDropsVisualHints()
    {
        Assert.Equal("Download complete: chart.png",
            SpeakableStatus.Spoken("Download complete: chart.png — click to open"));
        Assert.Equal("Loading Arena", SpeakableStatus.Spoken("Loading Arena…"));
        Assert.Equal("", SpeakableStatus.Spoken(""));
        Assert.Equal("All good", SpeakableStatus.Spoken("  All good  "));
    }

    // ─────────────────────── 9. prompt export formats ───────────────────────

    private static BattlePrompt Prompt(string name, string category, string body) =>
        new() { Id = Guid.NewGuid().ToString("N"), Name = name, Category = category, Body = body };

    [Fact]
    public void PromptMarkdown_GroupsByCategoryAndKeepsEveryBody()
    {
        var md = PromptExportText.ToMarkdown(new[]
        {
            Prompt("Summarise", "Writing", "Summarise this in three bullets."),
            Prompt("Refactor", "Code", "Refactor for readability."),
            Prompt("Outline", "Writing", "Outline the argument."),
        });
        Assert.StartsWith("# Battle Lab prompts", md);
        // One heading per category, not one per prompt. Counted by line:
        // splitting on "## " would also match the "## " inside "### ".
        var categoryHeadings = md.Split('\n').Count(line => line.StartsWith("## ", StringComparison.Ordinal));
        Assert.Equal(2, categoryHeadings);
        var promptHeadings = md.Split('\n').Count(line => line.StartsWith("### ", StringComparison.Ordinal));
        Assert.Equal(3, promptHeadings);
        Assert.Contains("## Code", md);
        Assert.Contains("## Writing", md);
        Assert.Contains("### Refactor", md);
        Assert.Contains("Summarise this in three bullets.", md);
        // Categories group: both Writing prompts land under the single heading.
        Assert.True(md.IndexOf("## Writing", StringComparison.Ordinal)
                  < md.IndexOf("### Outline", StringComparison.Ordinal));
    }

    [Fact]
    public void PromptMarkdown_BlankCategoryFallsBackToGeneral_AndEmptySaysSo()
    {
        var md = PromptExportText.ToMarkdown(new[] { Prompt("Untitled idea", "", "Do the thing.") });
        Assert.Contains("## General", md);
        var empty = PromptExportText.ToMarkdown(Array.Empty<BattlePrompt>());
        Assert.Contains("No prompts in the library.", empty);
    }

    [Fact]
    public void PromptCsv_QuotesCommasQuotesAndNewlines()
    {
        var csv = PromptExportText.ToCsv(new[]
        {
            Prompt("Plain", "Code", "No special characters here."),
            Prompt("Tricky, name", "Writing", "He said \"stop\", then\nleft."),
        });
        var lines = csv.Split('\n');
        Assert.Equal("Category,Name,Body", lines[0]);
        Assert.Contains("\"Tricky, name\"", csv);
        // Inner quotes double, and the newline stays inside one quoted field.
        Assert.Contains("\"He said \"\"stop\"\", then", csv);
        Assert.Contains("No special characters here.", csv);
    }

    [Fact]
    public void PromptCsv_DefusesFormulaPrefixes()
    {
        // A prompt body is arbitrary user text; opening the CSV must not
        // execute it as a formula.
        Assert.Equal("'=SUM(A1:A9)", PromptExportText.CsvField("=SUM(A1:A9)"));
        Assert.Equal("'+1 and more", PromptExportText.CsvField("+1 and more"));
        Assert.Equal("'@mention", PromptExportText.CsvField("@mention"));
        Assert.Equal("Plain text", PromptExportText.CsvField("Plain text"));
        // A dangerous value that also needs quoting gets both treatments.
        Assert.Equal("\"'=A1,B2\"", PromptExportText.CsvField("=A1,B2"));
    }

    [Fact]
    public void PromptCsvField_HandlesNullEmptyAndEdgeWhitespace()
    {
        Assert.Equal("", PromptExportText.CsvField(null));
        Assert.Equal("", PromptExportText.CsvField(""));
        Assert.Equal("\" padded \"", PromptExportText.CsvField(" padded "));
        Assert.Equal("0", PromptExportText.CsvField("0"));
    }

    [Fact]
    public void PromptExportFileName_IsDatedAndExtensionIsNormalised()
    {
        var name = PromptExportText.FileNameFor("csv", new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero));
        Assert.StartsWith("battle-lab-prompts-", name);
        Assert.EndsWith(".csv", name);
        Assert.DoesNotContain("..", name);
        Assert.EndsWith(".md", PromptExportText.FileNameFor(".md", DateTimeOffset.Now));
    }

    // ─────────────────────── 10. tray recent downloads ───────────────────────

    private static DownloadRecord Done(string name, DateTimeOffset when, string? path = null) =>
        new()
        {
            FileName = name,
            FullPath = path ?? "/downloads/" + name,
            CompletedUtc = when,
            State = DownloadRecordState.Completed,
        };

    [Fact]
    public void RecentDownloads_SkipsFilesThatNoLongerExist()
    {
        var newest = Done("gone.png", new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero));
        var older = Done("kept.png", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var picked = RecentDownloads.Pick(new[] { newest, older },
            p => p.EndsWith("kept.png", StringComparison.Ordinal), 5);
        Assert.Single(picked);
        Assert.Equal("kept.png", picked[0].FileName);
    }

    [Fact]
    public void RecentDownloads_NewestFirst_AndHonoursTheCap()
    {
        var records = new[]
        {
            Done("a.png", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Done("c.png", new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero)),
            Done("b.png", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)),
        };
        var picked = RecentDownloads.Pick(records, _ => true, 2);
        Assert.Equal(new[] { "c.png", "b.png" }, picked.Select(p => p.FileName).ToArray());
    }

    [Fact]
    public void RecentDownloads_SkipsFailuresAndBlankPaths()
    {
        var failed = Done("broken.zip", new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
        failed.State = DownloadRecordState.Failed;
        var blank = Done("mystery.bin", new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero), "   ");
        var good = Done("ok.png", new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero));
        var picked = RecentDownloads.Pick(new[] { failed, blank, good }, _ => true, 5);
        Assert.Single(picked);
        Assert.Equal("ok.png", picked[0].FileName);
    }

    [Fact]
    public void RecentDownloads_EmptyOrNonsenseInputIsEmptyNotAThrow()
    {
        Assert.Empty(RecentDownloads.Pick(null!, _ => true, 5));
        Assert.Empty(RecentDownloads.Pick(Array.Empty<DownloadRecord>(), _ => true, 5));
        Assert.Empty(RecentDownloads.Pick(new[] { Done("x.png", DateTimeOffset.UtcNow) }, _ => true, 0));
    }

    // ─────────────────────── 11. new fixed shortcuts ───────────────────────

    [Fact]
    public void FocusPage_IsFixedF6_AndGroupedWithNavigation()
    {
        var action = ShortcutCatalog.Find("focus-page");
        Assert.NotNull(action);
        Assert.Equal("F6", action!.Gestures[0]);
        // Not remappable: F6 is a Windows-standard pane key and the whole
        // point is that it is always available.
        Assert.DoesNotContain(action, ShortcutCatalog.Remappable);
        Assert.Equal("Moving around", ShortcutCheatSheet.GroupFor(action));
    }

    [Fact]
    public void StatusLine_IsFixedCtrlShiftS_AndGroupedWithThePage()
    {
        var action = ShortcutCatalog.Find("status-line");
        Assert.NotNull(action);
        Assert.Equal("Ctrl+Shift+S", action!.Gestures[0]);
        Assert.DoesNotContain(action, ShortcutCatalog.Remappable);
        Assert.Equal("The page", ShortcutCheatSheet.GroupFor(action));
    }

    [Fact]
    public void NewShortcuts_AppearInTheCheatSheetAndAreFindable()
    {
        // Every catalog entry reaches the sheet — a shortcut a user can
        // press but cannot look up is a shortcut nobody learns.
        var grouped = ShortcutCheatSheet.Grouped(null);
        var shown = grouped.SelectMany(g => g.Actions).Select(a => a.Id).ToList();
        Assert.Contains("focus-page", shown);
        Assert.Contains("status-line", shown);
        Assert.Equal(ShortcutCatalog.All.Count, shown.Count);

        // Their search wording reaches them, not just the id.
        Assert.Contains(ShortcutCheatSheet.Search("focus").Select(a => a.Id), id => id == "focus-page");
        Assert.Contains(ShortcutCheatSheet.Search("status").Select(a => a.Id), id => id == "status-line");
        Assert.Contains(ShortcutCheatSheet.Search("F6").Select(a => a.Id), id => id == "focus-page");
    }

    [Fact]
    public void NewShortcuts_DoNotCollideWithAnyOtherGesture()
    {
        var gestures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in ShortcutCatalog.All)
        {
            foreach (var text in action.Gestures)
            {
                // Escape is display-only and never dispatched as a gesture.
                if (text == "Escape")
                    continue;
                Assert.False(gestures.TryGetValue(text, out var owner),
                    $"'{text}' is claimed by both {owner} and {action.Id}");
                gestures[text] = action.Id;
            }
        }
        Assert.Contains("F6", gestures.Keys);
        Assert.Contains("Ctrl+Shift+S", gestures.Keys);
    }

    // ─────────────────────── 12. find in the log ───────────────────────

    [Fact]
    public void LogSearch_IsCaseInsensitiveAndLiteral()
    {
        Assert.Equal(2, LogSearch.FindAll("WARN look\nwarn again", "warn").Count);
        // Brackets and parens are literal text in a log, never a pattern.
        var matches = LogSearch.FindAll("[ERR] (net:1) failed", "[err]");
        Assert.Single(matches);
        Assert.Equal(0, matches[0].Index);
        Assert.Equal(5, matches[0].Length);
        Assert.Empty(LogSearch.FindAll("anything", "[.*]"));
    }

    [Fact]
    public void LogSearch_CountsEveryVisibleOccurrence()
    {
        var matches = LogSearch.FindAll("aaaa", "aa");
        Assert.Equal(3, matches.Count);
        Assert.Equal(new[] { 0, 1, 2 }, matches.Select(m => m.Index).ToArray());
        // End is the end of THIS match (0 + 2), not the end of the text.
        Assert.Equal(2, matches[0].End);
        Assert.Equal(4, matches[^1].End);
    }

    [Fact]
    public void LogSearch_BlankQueryOrEmptyTextHasNoMatches()
    {
        Assert.Empty(LogSearch.FindAll("some log", ""));
        Assert.Empty(LogSearch.FindAll("some log", null));
        Assert.Empty(LogSearch.FindAll("", "log"));
        Assert.Empty(LogSearch.FindAll(null, "log"));
    }

    [Fact]
    public void LogSearch_DescribeReadsNaturally()
    {
        Assert.Equal("No matches", LogSearch.Describe(0, -1));
        Assert.Equal("1 match", LogSearch.Describe(1, 0));
        Assert.Equal("1 of 12 matches", LogSearch.Describe(12, 0));
        Assert.Equal("12 of 12 matches", LogSearch.Describe(12, 11));
    }

    [Fact]
    public void LogSearch_StepWrapsInBothDirections()
    {
        // Nothing selected yet: Next starts at the top, Previous at the end.
        Assert.Equal(0, LogSearch.Step(5, -1, forward: true));
        Assert.Equal(4, LogSearch.Step(5, -1, forward: false));
        // Normal movement.
        Assert.Equal(2, LogSearch.Step(5, 1, forward: true));
        Assert.Equal(1, LogSearch.Step(5, 2, forward: false));
        // Wrapping never lands out of range.
        Assert.Equal(0, LogSearch.Step(5, 4, forward: true));
        Assert.Equal(4, LogSearch.Step(5, 0, forward: false));
        // A single match stays put rather than oscillating.
        Assert.Equal(0, LogSearch.Step(1, 0, forward: true));
        Assert.Equal(0, LogSearch.Step(1, 0, forward: false));
        // No matches is -1, not a throw.
        Assert.Equal(-1, LogSearch.Step(0, -1, forward: true));
    }

    [Fact]
    public void LogSearch_StepSurvivesAStaleIndex()
    {
        // A refresh can shrink the match set under a stored index.
        Assert.Equal(0, LogSearch.Step(3, 99, forward: true));
        Assert.Equal(2, LogSearch.Step(3, 99, forward: false));
    }

    // ─────────────────────── 13. prompt names ───────────────────────

    [Fact]
    public void PromptNaming_BlankBecomesTheFallback_AndRealNamesAreTrimmed()
    {
        Assert.Equal(PromptNaming.Untitled, PromptNaming.Normalize(null));
        Assert.Equal(PromptNaming.Untitled, PromptNaming.Normalize(""));
        Assert.Equal(PromptNaming.Untitled, PromptNaming.Normalize("   \t "));
        Assert.Equal("Summarise", PromptNaming.Normalize("  Summarise  "));
        Assert.Equal("Space inside", PromptNaming.Normalize("Space inside"));
    }

    [Fact]
    public void PromptNaming_FlagsUntitledPrompts()
    {
        Assert.True(PromptNaming.IsUntitled(new BattlePrompt { Name = "" }));
        Assert.True(PromptNaming.IsUntitled(new BattlePrompt { Name = "   " }));
        Assert.True(PromptNaming.IsUntitled(new BattlePrompt { Name = PromptNaming.Untitled }));
        Assert.False(PromptNaming.IsUntitled(new BattlePrompt { Name = "Named" }));
    }

    [Fact]
    public void PromptNaming_NextUntitledNeverDuplicatesALabel()
    {
        // A fresh library gets the plain fallback.
        Assert.Equal(PromptNaming.Untitled, PromptNaming.NextUntitled(Array.Empty<BattlePrompt>()));
        // Once that is taken, a numbered one; numbering continues past gaps.
        var one = new[] { new BattlePrompt { Name = PromptNaming.Untitled } };
        Assert.Equal(PromptNaming.Untitled + " 2", PromptNaming.NextUntitled(one));
        var two = new[]
        {
            new BattlePrompt { Name = PromptNaming.Untitled },
            new BattlePrompt { Name = PromptNaming.Untitled + " 2" },
        };
        Assert.Equal(PromptNaming.Untitled + " 3", PromptNaming.NextUntitled(two));
    }

    [Fact]
    public void PromptNaming_NextUntitledIgnoresCaseAndWhitespace()
    {
        // "untitled prompt" typed by hand still counts as taken.
        var existing = new[] { new BattlePrompt { Name = "untitled PROMPT" } };
        Assert.Equal(PromptNaming.Untitled + " 2", PromptNaming.NextUntitled(existing));
        Assert.Equal(PromptNaming.Untitled, PromptNaming.NextUntitled(null!));
    }

    [Fact]
    public void Sanitized_LiftsABlankPromptNameToTheFallback()
    {
        // A hand-edited file (or an old build) could carry no name at all;
        // the loader must not produce a row with a blank label.
        var data = new BattleLabData
        {
            Prompts =
            {
                new BattlePrompt { Id = "a", Name = "Real name", Body = "x" },
                new BattlePrompt { Id = "b", Name = "   ", Body = "y" },
                new BattlePrompt { Id = "c", Name = "  Padded  ", Body = "z" },
            },
        };
        var clean = BattleLabStore.Sanitized(data);
        Assert.Equal("Real name", clean.Prompts[0].Name);
        Assert.Equal(PromptNaming.Untitled, clean.Prompts[1].Name);
        Assert.Equal("Padded", clean.Prompts[2].Name);
    }

    // ─────────────────────── 14. unfilled blanks ───────────────────────

    private static IReadOnlyDictionary<string, string> Filled(params (string Key, string Value)[] pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
            map[key] = value;
        return map;
    }

    [Fact]
    public void Unfilled_NamesWhatTheCopyWillLeaveAsPlaceholders()
    {
        const string body = "Write about [topic] for [audience] in a [tone] tone.";
        var left = PromptBlanks.Unfilled(body, Filled(("topic", "tides")));
        Assert.Equal(new[] { "audience", "tone" }, left.ToArray());
    }

    [Fact]
    public void Unfilled_IsEmptyWhenEveryBlankIsFilled_OrThereAreNone()
    {
        Assert.Empty(PromptBlanks.Unfilled("Use [topic].", Filled(("topic", "x"))));
        Assert.Empty(PromptBlanks.Unfilled("No blanks here.", Filled()));
        Assert.Empty(PromptBlanks.Unfilled(null, Filled()));
    }

    [Fact]
    public void Unfilled_MatchesBlanksCaseInsensitively()
    {
        // The dialog keys values by the blank's spelling; a user typing
        // "Topic" into a field labelled [topic] still counts as filled.
        Assert.Empty(PromptBlanks.Unfilled("Use [topic].", Filled(("TOPIC", "x"))));
        // Keys with stray spaces are tolerated.
        Assert.Empty(PromptBlanks.Unfilled("Use [topic].", Filled((" topic ", "x"))));
    }

    [Fact]
    public void Unfilled_TreatsAWhitespaceOnlyValueAsNotFilled()
    {
        // A whitespace-only value leaves the placeholder in the text, so
        // Unfilled must agree with Fill rather than counting the key.
        var left = PromptBlanks.Unfilled("Use [topic].", Filled(("topic", "   ")));
        Assert.Equal(new[] { "topic" }, left.ToArray());
        Assert.Equal("Use [topic].", PromptBlanks.Fill("Use [topic].", Filled(("topic", "   "))));
    }

    [Fact]
    public void UnfilledSummary_ReadsAsOneSentence_AndIsEmptyWhenNothingIsLeft()
    {
        Assert.Equal("", PromptBlanks.UnfilledSummary("Use [topic].", Filled(("topic", "x"))));
        Assert.Equal("1 blank left as it was: [topic]",
            PromptBlanks.UnfilledSummary("Use [topic] and go.", Filled()));
        Assert.Equal("2 blanks left as they were: [topic], [tone]",
            PromptBlanks.UnfilledSummary("Use [topic] in a [tone] tone.", Filled()));
    }

    [Fact]
    public void Fill_AndUnfilled_AgreeOnWhatSurvived()
    {
        // The two must never disagree: a blank reported as unfilled is a
        // placeholder still in the text, and a filled one is gone from it.
        const string body = "[a] then [b]";
        var values = Filled(("a", "first"));
        var filled = PromptBlanks.Fill(body, values);
        Assert.Equal("first then [b]", filled);
        var left = PromptBlanks.Unfilled(body, values);
        Assert.Single(left);
        Assert.Contains("[b]", filled);
    }

    // ─────────────────────── 15. write tally in the report ───────────────────────

    /// <summary>
    /// DiagnosticsData has required path members (the report prints them),
    /// so every instance here has to supply them even when the test is
    /// only interested in one optional field.
    /// </summary>
    private static DiagnosticsData WithWriteLine(string? line) => new()
    {
        AppVersion = "1.0.0",
        SettingsPath = @"C:\profiles\main\settings.json",
        LogPath = @"C:\profiles\main\arena.log",
        ProfileDir = @"C:\profiles\main",
        SettingsWriteLine = line,
    };

    [Fact]
    public void DiagnosticsReport_IncludesTheWriteTally_WhenItHasOne()
    {
        var data = new DiagnosticsData
        {
            AppVersion = "1.0.0",
            SettingsPath = @"C:\profiles\main\settings.json",
            LogPath = @"C:\profiles\main\arena.log",
            ProfileDir = @"C:\profiles\main",
            SettingsWriteLine = "  Settings writes 4 · deferred 12 · coalesced 8 · failures 0",
        };
        var text = DiagnosticsReport.Compose(data);
        Assert.Contains("Settings writes this session", text);
        Assert.Contains("deferred 12", text);
        // The tally keeps the two-space indent every fact line under a
        // diagnostics heading uses (readiness below it is indented too),
        // so the report reads as one block rather than a stray column.
        Assert.Contains("\n  Settings writes 4 · deferred 12", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void DiagnosticsReport_OmitsTheWriteTally_WhenNothingWasWritten()
    {
        // ReportLine is null until the first write; the heading must not
        // appear on its own.
        // SettingsWriteLine is init-only, so it is set in the initializer.
        Assert.DoesNotContain("Settings writes this session",
            DiagnosticsReport.Compose(WithWriteLine(null)));
        Assert.DoesNotContain("Settings writes this session",
            DiagnosticsReport.Compose(WithWriteLine("   ")));
    }
}
