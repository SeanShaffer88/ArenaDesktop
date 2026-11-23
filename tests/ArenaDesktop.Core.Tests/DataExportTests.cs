using System.Text.Json;
using System.Text.RegularExpressions;
using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.14.0 evidence exports: the field pass becomes a file, and the journal
/// grows the view a tally cannot give — which prompts actually win.
/// </summary>
public class DataExportTests
{
    // ──────────────────────── field pass ────────────────────────

    private static FieldPassSession Recorded()
    {
        var session = new FieldPassSession(new FieldPassData());
        var steps = FieldPassCatalog.Steps;
        Assert.True(session.SetVerdict(steps[0].Id, FieldPassVerdict.Pass, "looked right"));
        Assert.True(session.SetVerdict(steps[1].Id, FieldPassVerdict.Problem, "toolbar overlapped"));
        Assert.True(session.SetVerdict(steps[2].Id, FieldPassVerdict.Skipped, ""));
        return session;
    }

    [Fact]
    public void TheExportIsValidJson_WithTheSameFactsAsTheMarkdown()
    {
        var generated = new DateTimeOffset(2026, 11, 23, 3, 30, 0, TimeSpan.Zero);
        var session = Recorded();
        var json = FieldPassSummary.ToJson(session, "0.14.0", generated);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("ArenaDesktop 0.14.0", root.GetProperty("app").GetString());
        Assert.Equal(generated.ToString("O"), root.GetProperty("generated").GetString());

        var counts = root.GetProperty("counts");
        Assert.Equal(FieldPassCatalog.Steps.Count, counts.GetProperty("total").GetInt32());
        Assert.Equal(1, counts.GetProperty("passed").GetInt32());
        Assert.Equal(1, counts.GetProperty("problems").GetInt32());
        Assert.Equal(1, counts.GetProperty("skipped").GetInt32());

        var steps = root.GetProperty("steps");
        Assert.Equal(FieldPassCatalog.Steps.Count, steps.GetArrayLength());
        Assert.Equal("Pass", steps[0].GetProperty("verdict").GetString());
        Assert.Equal("looked right", steps[0].GetProperty("note").GetString());
        Assert.Equal("Problem", steps[1].GetProperty("verdict").GetString());
        // The step numbers are 1-based, like the pass itself.
        Assert.Equal(1, steps[0].GetProperty("number").GetInt32());
    }

    [Fact]
    public void TheExportCarriesNoPageContent_AndSaysWhereItCameFrom()
    {
        var markdown = FieldPassSummary.Render(Recorded(), "0.14.0", DateTimeOffset.UnixEpoch);
        var json = FieldPassSummary.ToJson(Recorded(), "0.14.0", DateTimeOffset.UnixEpoch);
        foreach (var text in new[] { markdown, json })
        {
            // Nothing that looks like a web address, and no token-ish blobs.
            Assert.DoesNotContain("http://", text);
            Assert.DoesNotContain("https://", text);
        }
        Assert.Contains("nothing in this summary comes from the Arena page", markdown);
    }

    [Fact]
    public void AnEmptyPassStillExports()
    {
        var json = FieldPassSummary.ToJson(new FieldPassSession(new FieldPassData()), "0.14.0", DateTimeOffset.UnixEpoch);
        using var document = JsonDocument.Parse(json);
        var counts = document.RootElement.GetProperty("counts");
        Assert.Equal(0, counts.GetProperty("passed").GetInt32());
        Assert.Equal(FieldPassCatalog.Steps.Count, counts.GetProperty("remaining").GetInt32());
    }

    // ──────────────────────── journal leaderboard ────────────────────────

    private static BattleJournalEntry Entry(string prompt, BattleOutcome outcome) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        PromptName = prompt,
        Outcome = outcome,
        ModeId = ArenaModes.BattleId,
        RecordedUtc = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void TheLeaderboardRanksByDecidedBattles()
    {
        var entries = new[]
        {
            Entry("Alpha", BattleOutcome.AWins),
            Entry("Alpha", BattleOutcome.BWins),
            Entry("Alpha", BattleOutcome.Tie),
            Entry("Beta", BattleOutcome.AWins),
            Entry("Beta", BattleOutcome.AWins),
        };
        var standings = BattleJournalMarkdown.RankByPrompt(entries);
        Assert.Equal(2, standings.Count);
        Assert.Equal("Beta", standings[0].PromptName);       // 2 decided wins beats Alpha's 1 + 1
        Assert.Equal(2, standings[0].AWins);
        Assert.Equal("Alpha", standings[1].PromptName);
        Assert.Equal(3, standings[1].Total);
        Assert.Equal(1, standings[1].Ties);
    }

    [Fact]
    public void PromptNamesAreGroupedCaseInsensitively_AndBlankOnesDropped()
    {
        var standings = BattleJournalMarkdown.RankByPrompt(new[]
        {
            Entry("Same prompt", BattleOutcome.AWins),
            Entry("same prompt", BattleOutcome.BWins),
            Entry("   ", BattleOutcome.AWins),
        });
        var only = Assert.Single(standings);
        Assert.Equal(2, only.Total);
    }

    [Fact]
    public void TheJournalMarkdownGrowsTheLeaderboard_OnlyWhenThereIsMoreThanOnePrompt()
    {
        var one = new[] { Entry("Only one", BattleOutcome.AWins) };
        var single = BattleJournalMarkdown.RenderJournalMarkdown(one, BattleJournalTally.Of(one));
        Assert.DoesNotContain("Which prompts win", single);

        var two = new[] { Entry("First", BattleOutcome.AWins), Entry("Second", BattleOutcome.BWins) };
        var both = BattleJournalMarkdown.RenderJournalMarkdown(two, BattleJournalTally.Of(two));
        Assert.Contains("## Which prompts win", both);
        Assert.Contains("| First | 1 | 1 | 0 | 0 |", both);
        Assert.Contains("the ones you entered by hand", both);
    }

    [Fact]
    public void AnEmptyJournalHasNoLeaderboard()
    {
        Assert.Empty(BattleJournalMarkdown.RankByPrompt(Array.Empty<BattleJournalEntry>()));
        Assert.Equal("", BattleJournalMarkdown.RenderLeaderboard(Array.Empty<BattleJournalEntry>()));
    }

    // ──────────────────────── the shell side ────────────────────────

    [Fact]
    public void ThePassWindowHasAnExportButton_ThatWritesBothFormats()
    {
        var xaml = Source("src/ArenaDesktop/FieldPassWindow.xaml");
        Assert.Contains("x:Name=\"ExportSummaryButton\"", xaml);
        Assert.Contains("Export report…", xaml);
        var code = Source("src/ArenaDesktop/FieldPassWindow.xaml.cs");
        Assert.Contains("FieldPassSummary.ToJson(_session, _appVersion, DateTimeOffset.Now)", code);
        Assert.Contains("FieldPassSummary.Render(_session, _appVersion, DateTimeOffset.Now)", code);
        Assert.Contains("SaveFileDialog", code);
        Assert.Contains("FilterIndex == 2", code);   // the dialog decides the format
    }

    [Fact]
    public void TheVerifyScriptExists_AndAsksForVerdicts()
    {
        var script = Source("scripts/verify-windows.ps1");
        Assert.Contains("--smoke-test", script);
        Assert.Contains("Ask-Verdict", script);
        Assert.Contains("$reportPath", script);
        Assert.Contains("Not recorded", script);          // non-interactive runs say so rather than guessing
        Assert.DoesNotContain("TODO", script);
    }

    [Fact]
    public void TheArm64BuildIsPartOfCI()
    {
        var ci = Source(".github/workflows/ci.yml");
        Assert.Contains("win-arm64", ci);
        Assert.Contains("-r win-arm64 --self-contained true", ci);
        Assert.Contains("WebView2Loader.dll", ci);
    }

    [Fact]
    public void HighContrastOverridesEveryBrushTheShellPaintsWith()
    {
        var app = Source("src/ArenaDesktop/App.xaml");
        var keys = Regex.Matches(app, "SolidColorBrush x:Key=\"(?<key>[A-Za-z]+)\"")
            .Select(m => m.Groups["key"].Value)
            .Distinct()
            .ToList();
        Assert.Equal(10, keys.Count);

        var theme = Source("src/ArenaDesktop/ThemeManager.cs");
        var start = theme.IndexOf("private static void ApplyHighContrastOverrides()", StringComparison.Ordinal);
        Assert.True(start >= 0, "ApplyHighContrastOverrides not found");
        var body = theme[start..theme.IndexOf("private static void SetBrushColor", start, StringComparison.Ordinal)];
        var overridden = Regex.Matches(body, "SetBrush\\(\"(?<key>[A-Za-z]+)\"")
            .Select(m => m.Groups["key"].Value)
            .ToHashSet();
        foreach (var key in keys)
            Assert.Contains(key, overridden);

        // …and the override is re-applied when Windows' preference changes,
        // not only at startup.
        Assert.Contains("UserPreferenceChanged", Source("src/ArenaDesktop/App.xaml.cs"));
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(DataExportTests).Assembly.Location)!);
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
