using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.14.0 — the app looking at its own files and its own crashes. Two
/// questions a user asks when something is wrong ("is one of my files broken?"
/// and "did it crash last time?") that used to have no answer anywhere in the
/// UI.
/// </summary>
public class DataHealthTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arenadesktop-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void EveryFileIsReported_EvenWhenNoneExist()
    {
        var dir = TempDir();
        try
        {
            var reports = DataHealth.Check(dir);
            Assert.Equal(DataHealth.FileNames.Count, reports.Count);
            Assert.All(reports, r => Assert.Equal(DataFileState.Missing, r.State));
            Assert.All(reports, r => Assert.False(r.NeedsRepair));
            var summary = DataHealth.Summary(reports);
            Assert.Contains("not created yet", summary);
            Assert.Contains("nothing needs repair", summary);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AGoodFileParses()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"zoom\":1.0}");
            var report = DataHealth.Check(dir).Single(r => r.Name == "settings.json");
            Assert.Equal(DataFileState.Ok, report.State);
            Assert.Contains("ok (", report.Line);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("{\"zoom\":")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void DamagedFilesAreNamed_NotThrown(string contents)
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "battle-lab.json"), contents);
            var report = DataHealth.Check(dir).Single(r => r.Name == "battle-lab.json");
            Assert.Equal(DataFileState.Unreadable, report.State);
            Assert.True(report.NeedsRepair);
            Assert.StartsWith("battle-lab.json — unreadable: ", report.Line);
            // The detail never echoes the file's content — it says what is
            // wrong, not what was in it.
            Assert.DoesNotContain("zoom", report.Line);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AnEmptyFileIsUnreadable_NotOk()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "prompt-pad.json"), "");
            var report = DataHealth.Check(dir).Single(r => r.Name == "prompt-pad.json");
            Assert.Equal(DataFileState.Unreadable, report.State);
            Assert.Contains("empty", report.Line);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Repair_BacksUpBeforeItDeletes()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "field-pass.json");
            File.WriteAllText(path, "{ this is not json");
            var backup = DataHealth.Repair(path, DateTimeOffset.Now);

            Assert.NotNull(backup);
            Assert.False(File.Exists(path));                       // the app starts fresh
            Assert.True(File.Exists(backup));                      // the bytes still exist
            Assert.Contains(CorruptFile.Marker, backup!);
            Assert.Contains("this is not json", File.ReadAllText(backup!));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Repair_LeavesGoodAndMissingFilesAlone()
    {
        var dir = TempDir();
        try
        {
            var good = Path.Combine(dir, "settings.json");
            File.WriteAllText(good, "{\"a\":1}");
            Assert.Null(DataHealth.Repair(good, DateTimeOffset.Now));
            Assert.True(File.Exists(good));

            Assert.Null(DataHealth.Repair(Path.Combine(dir, "nope.json"), DateTimeOffset.Now));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RepairAll_ReportsOnlyWhatItTouched()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{broken");
            File.WriteAllText(Path.Combine(dir, "battle-lab.json"), "{also broken");
            File.WriteAllText(Path.Combine(dir, "prompt-pad.json"), "{\"draft\":\"fine\"}");
            var reports = DataHealth.Check(dir);

            var backups = DataHealth.RepairAll(reports, DateTimeOffset.Now);
            Assert.Equal(2, backups.Count);
            Assert.True(File.Exists(Path.Combine(dir, "prompt-pad.json")));
            // After repair, the inventory is clean again.
            Assert.DoesNotContain(DataHealth.Check(dir), r => r.NeedsRepair);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Summary_CountsTheDamage()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{broken");
            File.WriteAllText(Path.Combine(dir, "downloads.json"), "{\"records\":[]}");
            var summary = DataHealth.Summary(DataHealth.Check(dir));
            Assert.Contains("1 file(s) cannot be read", summary);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TheLedgerItselfIsInventoried()
    {
        Assert.Contains("crash-history.json", DataHealth.FileNames);
        // And the app's real paths agree with the inventory's names.
        Assert.Equal("crash-history.json", Path.GetFileName(AppPaths.CrashHistoryPath));
    }
}

/// <summary>The crash ledger's policy: what counts as a loop, and what a clean exit clears.</summary>
public class CrashHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 3, 0, 0, TimeSpan.Zero);

    private static CrashRecord At(DateTimeOffset when, string summary = "boom") =>
        new(when, "Unhandled (UI thread)", summary);

    [Fact]
    public void OnCleanExit_NothingToReport()
    {
        var data = CrashHistory.CleanExit(new CrashHistoryData());
        Assert.Null(CrashHistory.LastCrashLine(data));
        Assert.False(CrashHistory.IsLoop(data, Now));
    }

    [Fact]
    public void ACrashIsRememberedAsTheLastRun()
    {
        var data = CrashHistory.Record(new CrashHistoryData(), At(Now.AddMinutes(-5), "Settings would not open"), Now);
        var line = CrashHistory.LastCrashLine(data);
        Assert.NotNull(line);
        Assert.Contains("The last run ended in an error", line);
        Assert.Contains("Settings would not open", line);
    }

    [Fact]
    public void ACleanExitClearsTheMarker_ButKeepsTheHistory()
    {
        var data = CrashHistory.Record(new CrashHistoryData(), At(Now.AddMinutes(-5)), Now);
        CrashHistory.CleanExit(data);
        Assert.Null(CrashHistory.LastCrashLine(data));
        Assert.Single(data.Crashes);   // the pattern is still visible to whoever reads the file
    }

    [Fact]
    public void TwoCrashesAreUnlucky_ThreeAreALoop()
    {
        var data = new CrashHistoryData();
        CrashHistory.Record(data, At(Now.AddMinutes(-6)), Now);
        CrashHistory.Record(data, At(Now.AddMinutes(-4)), Now);
        Assert.False(CrashHistory.IsLoop(data, Now));

        CrashHistory.Record(data, At(Now.AddMinutes(-1)), Now);
        Assert.True(CrashHistory.IsLoop(data, Now));
    }

    [Fact]
    public void OldCrashesDoNotCountTowardsTheWindow()
    {
        var data = new CrashHistoryData();
        CrashHistory.Record(data, At(Now.AddMinutes(-40)), Now);
        CrashHistory.Record(data, At(Now.AddMinutes(-30)), Now);
        CrashHistory.Record(data, At(Now.AddMinutes(-20)), Now);
        Assert.False(CrashHistory.IsLoop(data, Now));
    }

    [Fact]
    public void TheLedgerIsTrimmed_ButKeepsTheNewest()
    {
        var data = new CrashHistoryData();
        for (var i = 0; i < CrashHistory.Keep + 5; i++)
            CrashHistory.Record(data, At(Now.AddSeconds(i), $"crash {i}"), Now);
        Assert.Equal(CrashHistory.Keep, data.Crashes.Count);
        Assert.Equal($"crash {CrashHistory.Keep + 4}", data.Crashes[^1].Summary);  // newest last
        Assert.Equal("crash 5", data.Crashes[0].Summary);                          // oldest dropped
    }

    [Fact]
    public void TheLedgerSurvivesARestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arenadesktop-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "crash-history.json");
            CrashHistoryStore.Record(path, At(Now, "Unhandled (background thread): boom"), Now);
            var reloaded = CrashHistoryStore.Load(path);
            Assert.NotNull(CrashHistory.LastCrashLine(reloaded));
            Assert.Equal("Unhandled (background thread): boom", reloaded.Crashes[^1].Summary);

            CrashHistoryStore.MarkCleanExit(path);
            Assert.Null(CrashHistory.LastCrashLine(CrashHistoryStore.Load(path)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ABrokenLedgerReadsAsEmpty_NeverThrows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arenadesktop-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "crash-history.json");
            File.WriteAllText(path, "{not a ledger");
            var data = CrashHistoryStore.Load(path);
            Assert.Empty(data.Crashes);
            Assert.Null(data.LastCrash);
            // Saving never throws, even into a folder that does not exist yet
            // (AtomicFile creates it) — a ledger write must not be the reason a
            // crash handler fails.
            Assert.True(CrashHistoryStore.Save(Path.Combine(dir, "nested-dir", "x.json"), data));
        }
        finally { Directory.Delete(dir, true); }
    }
}
