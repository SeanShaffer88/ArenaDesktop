using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.14.0 shell wiring: the crash ledger is written from three handlers,
/// cleared on a clean exit, shown on the next launch, and the data-file
/// inventory reaches Diagnostics with a repair button beside it.
/// </summary>
public class ReliabilityWiringTests
{
    [Fact]
    public void TheLedgerIsWrittenByAllThreeCrashPaths_AndClearedOnCleanExit()
    {
        var app = Source("src/ArenaDesktop/App.xaml.cs");
        Assert.Contains("AppDomain.CurrentDomain.UnhandledException", app);
        Assert.Contains("TaskScheduler.UnobservedTaskException", app);
        Assert.Contains("args.SetObserved();", app);          // never escalate it to a process kill
        Assert.Contains("RecordCrash(crashLedger, \"Unhandled (UI thread)\", args.Exception)", app);
        Assert.Contains("RecordCrash(crashLedger, \"Unhandled (background thread)\", fatal)", app);
        Assert.Contains("RecordCrash(crashLedger, \"Unobserved task\", args.Exception)", app);
        // A clean exit is the only thing that clears the marker.
        Assert.Contains("CrashHistoryStore.MarkCleanExit(AppPaths.CrashHistoryPath);", app);
        Assert.Single(app.Split("protected override void OnExit").Skip(1));
    }

    [Fact]
    public void ALoopOffersSafeMode()
    {
        var app = Source("src/ArenaDesktop/App.xaml.cs");
        Assert.Contains("var loop = RecordCrash(", app);
        Assert.Contains("offerSafeMode: loop", app);
        var presenter = Source("src/ArenaDesktop/CrashWindow.xaml.cs");
        Assert.Contains("RestartInSafeMode", presenter);
        Assert.Contains("\"--safe-mode\"", presenter);
        var xaml = Source("src/ArenaDesktop/CrashWindow.xaml");
        Assert.Contains("x:Name=\"SafeModeButton\"", xaml);
        Assert.Contains("Restart in safe mode", xaml);
        Assert.Contains("Visibility=\"Collapsed\"", xaml);      // only shown when it is a loop
    }

    [Fact]
    public void TheNextLaunchSaysTheLastRunEndedBadly()
    {
        var app = Source("src/ArenaDesktop/App.xaml.cs");
        Assert.Contains("CrashHistory.LastCrashLine(CrashHistoryStore.Load(crashLedger))", app);
        var main = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        Assert.Contains("NotePreviousRunOnce();", main);
        Assert.Contains("The last run ended in an error", main);
        // …and Diagnostics carries the detail, so the status line has somewhere to point.
        Assert.Contains("LastRunLine = CrashHistory.LastCrashLine(", main);
        Assert.Contains("DataFileLines = DataHealth.Summary(", main);
    }

    [Fact]
    public void DiagnosticsCarriesTheDataInventory_AndAButtonToActOnIt()
    {
        var report = Source("src/ArenaDesktop.Core/Reliability.cs");
        Assert.Contains("public IReadOnlyList<string>? DataFileLines", report);
        Assert.Contains("public string? LastRunLine", report);
        Assert.Contains("lines.Add(\"Data files (this PC)\");", report);
        Assert.Contains("lines.Add(\"Last run\");", report);

        var xaml = Source("src/ArenaDesktop/DiagnosticsWindow.xaml");
        Assert.Contains("x:Name=\"RepairDataButton\"", xaml);
        Assert.Contains("Repair data files…", xaml);
        var window = Source("src/ArenaDesktop/DiagnosticsWindow.xaml.cs");
        Assert.Contains("Func<string>? onRepairData = null", window);
        Assert.Contains("RepairDataButton.Visibility = Visibility.Visible;", window);

        var main = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        Assert.Contains("onRepairData: RepairDataFiles,", main);
        Assert.Contains("DataHealth.RepairAll(reports, DateTimeOffset.Now)", main);
        Assert.Contains("Restart ArenaDesktop to start from defaults.", main);
    }

    [Fact]
    public void TheInventoryUsesTheAppsRealFileNames()
    {
        // The inventory's names are the paths the app actually uses; a
        // rename in AppPaths that missed this list would show a file as
        // "not created yet" forever.
        Assert.Equal("settings.json", Path.GetFileName(AppPaths.SettingsPath));
        Assert.Equal("downloads.json", Path.GetFileName(AppPaths.DownloadsHistoryPath));
        Assert.Equal("battle-lab.json", Path.GetFileName(AppPaths.BattleLabPath));
        Assert.Equal("prompt-pad.json", Path.GetFileName(AppPaths.PromptPadPath));
        Assert.Equal("field-pass.json", Path.GetFileName(AppPaths.FieldPassPath));
        Assert.Equal("privacy-stats.json", Path.GetFileName(AppPaths.PrivacyStatsPath));
        Assert.Equal("crash-history.json", Path.GetFileName(AppPaths.CrashHistoryPath));
        foreach (var name in DataHealth.FileNames)
            Assert.Contains(name, new[]
            {
                "settings.json", "downloads.json", "battle-lab.json", "prompt-pad.json",
                "field-pass.json", "privacy-stats.json", "crash-history.json",
            });
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ReliabilityWiringTests).Assembly.Location)!);
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
