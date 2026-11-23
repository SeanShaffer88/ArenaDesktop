using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.13.0 — the crash dialog stopped saying "see the log" and started
/// saying what happened, where, and how to send it. These pin the text:
/// which frame counts as "the app's", how long a message may be, how much
/// of the log travels with a report, and that none of it can throw on the
/// way out of a handler that exists to survive a crash.
/// </summary>
public class CrashReportTests
{
    // Thrown from here, so the "app frame" is this test class rather than
    // the framework's own machinery.
    private static Exception ThrownHere(string message = "boom")
    {
        // Thrown for real: a StackTrace only exists for an exception that was
        // actually thrown, which is the whole point of the Where() assertions.
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Describe_NamesTheTypeAndKeepsTheMessage()
    {
        var facts = CrashReport.Describe(ThrownHere("the thing broke"));
        Assert.Equal("System.InvalidOperationException", facts.Type);
        Assert.Equal("the thing broke", facts.Message);
        Assert.Null(facts.Inner);
    }

    [Fact]
    public void Where_IsTheAppFrame_NotTheFramework()
    {
        var where = CrashReport.Where(ThrownHere());
        // The throwing frame is in this test class; nothing in System.* is
        // allowed to win over it.
        Assert.Contains("CrashReportTests", where);
        Assert.DoesNotContain("System.", where);
        Assert.EndsWith("()", where);
    }

    [Fact]
    public void Where_FallsBackWhenThereIsNoStackTrace()
    {
        // A hand-constructed exception never thrown has no trace at all.
        var where = CrashReport.Where(new InvalidOperationException("never thrown"));
        Assert.Equal("unknown (no stack trace)", where);
    }

    [Fact]
    public void Where_TakesTheDeepestAppFrame_WhenOnlyFrameworkFramesExist()
    {
        // Thrown inside System.* by design: the app frame is the caller, so
        // the answer must still be this test's own method, not System.*.
        var where = CrashReport.Where(Record.Exception(() => int.Parse("not a number", System.Globalization.CultureInfo.InvariantCulture))!);
        Assert.Contains("CrashReportTests", where);
    }

    [Fact]
    public void Inner_IsTheDeepestCause_NotTheWrapper()
    {
        var inner = new ArgumentException("the real reason");
        var outer = new InvalidOperationException("wrapper", inner);
        var facts = CrashReport.Describe(outer);
        Assert.Equal("wrapper", facts.Message);
        Assert.Equal("System.ArgumentException: the real reason", facts.Inner);
    }

    [Fact]
    public void Message_IsOneLine_AndBounded()
    {
        var longText = string.Join("\n", Enumerable.Repeat(new string('x', 100), 10));
        var facts = CrashReport.Describe(new InvalidOperationException(longText));
        Assert.DoesNotContain("\n", facts.Message);
        Assert.True(facts.Message.Length <= CrashReport.MaxMessageLength,
            $"message was {facts.Message.Length} characters");
        Assert.EndsWith("…", facts.Message);
    }

    [Fact]
    public void Message_CollapsesTheRunsOfSpacesAStackTraceLeaves()
    {
        var facts = CrashReport.Describe(new InvalidOperationException("a   b\n\n   c"));
        Assert.Equal("a b c", facts.Message);
    }

    // ──────────────────────── the log tail ────────────────────────

    [Fact]
    public void Tail_IsTheLastLines_NewestKept()
    {
        var log = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"line {i}"));
        var tail = CrashReport.Tail(log, lines: 3);
        Assert.Equal("line 98\nline 99\nline 100", tail);
    }

    [Fact]
    public void Tail_UnderstandsWindowsLineEndings_AndTrailingBlankLines()
    {
        var tail = CrashReport.Tail("one\r\ntwo\r\nthree\r\n\r\n\r\n", lines: 2);
        Assert.Equal("two\nthree", tail);
    }

    [Fact]
    public void Tail_IsCappedByCharacters_KeepingTheEnd()
    {
        var log = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"line {i} " + new string('z', 200)));
        var tail = CrashReport.Tail(log, lines: 50, chars: 300);
        Assert.True(tail.Length <= 301, $"tail was {tail.Length} characters");
        Assert.EndsWith("z", tail);           // the newest line's end survived
        Assert.StartsWith("…", tail);         // and it says it was cut
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n ")]
    public void Tail_OfNothingIsEmpty(string? log) => Assert.Equal("", CrashReport.Tail(log));

    [Fact]
    public void TailFromFile_ReadsAndNeverThrows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arenadesktop-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "arenadesktop.log");
            File.WriteAllText(path, "first\nsecond\nthird\n");
            Assert.Equal("second\nthird", CrashReport.TailFromFile(path, lines: 2));
            Assert.Equal("", CrashReport.TailFromFile(Path.Combine(dir, "missing.log")));
            Assert.Equal("", CrashReport.TailFromFile(dir));   // a directory is not a log
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ──────────────────────── the package ────────────────────────

    [Fact]
    public void Package_SaysWhatWhereAndHowToFollowUp()
    {
        var when = new DateTimeOffset(2026, 11, 23, 2, 5, 9, TimeSpan.FromHours(-6));
        var text = CrashReport.Package(
            "0.13.0", "Microsoft Windows 11 (10.0.22631)", when, @"C:\Users\Sean\AppData\Roaming\ArenaDesktop\logs\arenadesktop.log",
            ThrownHere("Settings would not open"), "INFO a\nINFO b\nINFO c");
        Assert.StartsWith("ArenaDesktop 0.13.0 crash report\n", text);
        Assert.Contains("When: 2026-11-23 02:05:09 -06:00", text);
        Assert.Contains("Runtime: Microsoft Windows 11", text);
        Assert.Contains("Exception: System.InvalidOperationException: Settings would not open", text);
        Assert.Contains("Where: ", text);
        Assert.Contains("CrashReportTests", text);
        Assert.Contains("Last 25 log lines:\nINFO a\nINFO b\nINFO c", text);
        // The log path travels with it — the user does not have to find it.
        Assert.Contains(@"logs\arenadesktop.log", text);
        Assert.EndsWith("\n", text);
        Assert.DoesNotContain("\r", text);   // one flavour of newline, whichever OS built it
    }

    [Fact]
    public void Package_OmitsTheTailSectionWhenThereIsNoLog()
    {
        var text = CrashReport.Package("0.13.0", "Windows", DateTimeOffset.UnixEpoch,
            "log.txt", ThrownHere(), logText: null);
        Assert.DoesNotContain("Last 25 log lines:", text);
    }

    [Fact]
    public void Package_IsBounded_EvenWithAMegabyteOfLog()
    {
        var huge = string.Join("\n", Enumerable.Range(0, 5000).Select(i => $"2026-11-23 00:00:{i % 60:00} [INFO] " + new string('q', 120)));
        var text = CrashReport.Package("0.13.0", "Windows", DateTimeOffset.UnixEpoch, "log.txt", ThrownHere(), huge);
        Assert.True(text.Length < CrashReport.MaxTailChars + 1000,
            $"package was {text.Length} characters — too big to paste");
    }

    [Fact]
    public void PackageForText_WorksWhenThereIsNoHeapExceptionToPointAt()
    {
        var text = CrashReport.PackageForText("0.13.0", "Windows", DateTimeOffset.UnixEpoch,
            "log.txt", "SettingsWindow could not be constructed", "INFO one");
        Assert.Contains("Exception: SettingsWindow could not be constructed", text);
        Assert.Contains("INFO one", text);
        Assert.DoesNotContain("Where:", text);
    }

    [Fact]
    public void Package_RedactsTheLogTail_LikeTheSupportBundleDoes()
    {
        const string log =
            "2026-11-23 02:00:00 [INFO] Navigating https://arena.ai/battle?tab=leaderboard&token=abc123\n" +
            "2026-11-23 02:00:01 [INFO] Signed in as sean@example.com\n" +
            "2026-11-23 02:00:02 [ERROR] Unhandled: boom";
        var text = CrashReport.Package("0.13.0", "Windows", DateTimeOffset.UnixEpoch, "log.txt", ThrownHere(), log);
        Assert.DoesNotContain("token=abc123", text);
        Assert.DoesNotContain("sean@example.com", text);
        Assert.Contains("https://arena.ai/battle", text);   // the harmless part survives
        Assert.Contains("Unhandled: boom", text);
    }

    [Fact]
    public void Summary_IsTheOneLineTheDialogLeadsWith()
    {
        var summary = CrashReport.Summary(ThrownHere("a\ntwo-line message"));
        Assert.Equal("System.InvalidOperationException: a two-line message", summary);
        Assert.DoesNotContain("\n", summary);
    }
}
