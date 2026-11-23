using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// The shell half of v0.13.0: a crash reports itself instead of pointing at a
/// file, and Settings cannot be taken down by one panel. These read the shell
/// sources (the WPF project cannot be referenced from this suite), the same
/// way the other drift guards do.
/// </summary>
public class CrashWiringTests
{
    [Fact]
    public void TheDispatcherHandler_ShowsTheReport_NotABareMessageBox()
    {
        var app = Source("src/ArenaDesktop/App.xaml.cs");
        Assert.Contains("CrashPresenter.Show(_window, Log, args.Exception);", app);
        // The old wording is gone; a report that cannot name the exception is
        // the thing this slice replaced.
        Assert.DoesNotContain("hit an unexpected error but kept your session safe", app);
        Assert.DoesNotContain("Details were written to the log:", app);
    }

    [Fact]
    public void StartupFailure_AlsoReportsItself()
    {
        var app = Source("src/ArenaDesktop/App.xaml.cs");
        Assert.Contains("CrashPresenter.Show(null, Log, ex, \"ArenaDesktop could not create its main window.\");", app);
    }

    [Fact]
    public void TheCrashWindow_CopiesAndOpensTheLog()
    {
        var xaml = Source("src/ArenaDesktop/CrashWindow.xaml");
        Assert.Contains("x:Name=\"CopyButton\"", xaml);
        Assert.Contains("Content=\"Copy details\"", xaml);
        Assert.Contains("x:Name=\"OpenLogButton\"", xaml);
        Assert.Contains("x:Name=\"DetailsBox\"", xaml);
        Assert.Contains("IsCancel=\"True\"", xaml);          // Esc / Close always works
        // Window-local style: the dialog must not depend on anything it might
        // have to explain being broken.
        Assert.Contains("<Style x:Key=\"Note\" TargetType=\"TextBlock\">", xaml);
        var code = Source("src/ArenaDesktop/CrashWindow.xaml.cs");
        Assert.Contains("Clipboard.SetText(_package)", code);
        Assert.Contains("catch (Exception ex)", code);       // clipboard held by another app, etc.
        Assert.Contains("Could not copy", code);
    }

    [Fact]
    public void ThePresenter_NeverThrows_AndFallsBackToTheOldBox()
    {
        var code = Source("src/ArenaDesktop/CrashWindow.xaml.cs");
        Assert.Contains("public static class CrashPresenter", code);
        // Every step of the way out has a floor under it.
        Assert.Contains("CrashReport.Package(", code);
        Assert.Contains("CrashReport.TailFromFile(log.Path)", code);
        Assert.Contains("MessageBox.Show(", code);
        Assert.Contains("Crash dialog failed: ", code);   // logged before the fallback box
        Assert.Contains("ShowText(", code);
    }

    [Fact]
    public void SettingsFailure_SaysWhichWindow_AndReturns()
    {
        var main = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        var start = main.IndexOf("private void OpenSettings(string? sectionId = null)", StringComparison.Ordinal);
        Assert.True(start >= 0, "OpenSettings not found");
        var body = main[start..Math.Min(main.Length, start + 3000)];
        Assert.Contains("catch (Exception ex)", body);
        Assert.Contains("Settings window creation failed", body);
        Assert.Contains("Settings could not open.", body);
        Assert.Contains("return;", body);
    }

    [Fact]
    public void SettingsSurvivesAPanelThatThrows()
    {
        var xaml = Source("src/ArenaDesktop/SettingsWindow.xaml");
        Assert.Contains("x:Name=\"LoadFailureBanner\"", xaml);
        Assert.Contains("x:Name=\"LoadFailureDetail\"", xaml);
        Assert.Contains("Some settings could not be shown.", xaml);
        // …and it is outside the searchable sections, so searching cannot hide
        // the fact that something did not load.
        Assert.True(xaml.IndexOf("LoadFailureBanner", StringComparison.Ordinal)
                    < xaml.IndexOf("x:Name=\"SectionBehavior\"", StringComparison.Ordinal),
            "the banner must sit above the sections so search cannot hide it");

        var code = Source("src/ArenaDesktop/SettingsWindow.xaml.cs");
        Assert.Contains("private void Guard(string step, Action action)", code);
        Assert.Contains("Settings: {step} failed: {ex}", code);
        Assert.Contains("ShowLoadFailures();", code);
        Assert.Contains("_loadFailures.Clear();", code);
        // The load steps that touch the disk, the registry or the page are
        // guarded, not the assignments.
        var guarded = new[]
        {
            "the update snooze line", "the downloads-folder check", "the pinned-mode row",
            "the global hotkey row", "the profile list", "the clear-data note",
            "the app-lock row", "the privacy report", "the permissions row",
            "the site-data row", "the backup list", "text scaling",
        };
        foreach (var step in guarded)
            Assert.Contains($"Guard(\"{step}\"", code);
    }

    [Fact]
    public void TheHelpPage_ExistsForTheErrorBox()
    {
        var page = HelpCatalog.ById("unexpected-error");
        Assert.NotNull(page);
        Assert.Equal(HelpAction.OpenDiagnostics, page!.Action);
        var body = HelpCatalog.ExtractBody(Source("Help.md"), page);
        Assert.NotNull(body);
        Assert.Contains("Copy details", body);
        Assert.Contains("redaction", body);
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(CrashWiringTests).Assembly.Location)!);
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
