using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// The field pass (v0.2.0): the nine-check catalogue, the verdict
/// flow + counts, the JSON store (round-trip, corrupt-file contract,
/// sanitizing), and the summary renderer's content. The pass records
/// the user's own verdicts — nothing here ever touches the Arena
/// page, so there is nothing else to fake.
/// </summary>
public class FieldPassTests
{
    private static FieldPassSession EmptySession() => new(new FieldPassData());

    // ──────────────────────── catalogue ────────────────────────

    [Fact]
    public void Catalogue_HasNineUniqueStepsInBriefOrder()
    {
        Assert.Equal(9, FieldPassCatalog.Steps.Count);
        Assert.Equal(9, FieldPassCatalog.Steps.Select(s => s.Id).Distinct().Count());
        Assert.Equal(
            new[]
            {
                "layouts", "battle-lab", "arena-modes", "battle-journal", "downloads",
                "tray-hotkey", "app-lock", "profiles", "update-check",
            },
            FieldPassCatalog.Steps.Select(s => s.Id).ToArray());
        Assert.All(FieldPassCatalog.Steps, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Instructions));
            Assert.False(string.IsNullOrWhiteSpace(s.SuccessLine));
        });
    }

    [Fact]
    public void Catalogue_OpenTargets_MatchTheBrief()
    {
        Assert.Equal(FieldPassTarget.Settings, FieldPassCatalog.ById("layouts")!.Target);
        Assert.Equal(FieldPassTarget.BattleLab, FieldPassCatalog.ById("battle-lab")!.Target);
        Assert.Equal(FieldPassTarget.None, FieldPassCatalog.ById("arena-modes")!.Target);
        Assert.Equal(FieldPassTarget.BattleLab, FieldPassCatalog.ById("battle-journal")!.Target);
        Assert.Equal(FieldPassTarget.Downloads, FieldPassCatalog.ById("downloads")!.Target);
        Assert.Equal(FieldPassTarget.None, FieldPassCatalog.ById("tray-hotkey")!.Target);
        Assert.Equal(FieldPassTarget.None, FieldPassCatalog.ById("app-lock")!.Target);
        Assert.Equal(FieldPassTarget.None, FieldPassCatalog.ById("profiles")!.Target);
        Assert.Equal(FieldPassTarget.Settings, FieldPassCatalog.ById("update-check")!.Target);
        Assert.Null(FieldPassCatalog.ById("nope"));
        Assert.Null(FieldPassCatalog.ById(null));
    }

    // ──────────────────────── verdict flow ────────────────────────

    [Fact]
    public void Session_Verdicts_Counts_AndResumeIndex()
    {
        var session = EmptySession();
        var when = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(new FieldPassCounts(0, 0, 0, 9), session.Counts);
        Assert.Equal(0, session.FirstUnfinishedIndex);

        Assert.True(session.SetVerdict("layouts", FieldPassVerdict.Pass, "  all three worked  ", when));
        Assert.True(session.SetVerdict("battle-lab", FieldPassVerdict.Problem, "clipboard empty", when));
        Assert.True(session.SetVerdict("arena-modes", FieldPassVerdict.Skipped, "", when));

        Assert.Equal(new FieldPassCounts(1, 1, 1, 6), session.Counts);
        Assert.Equal(6, session.Counts.Remaining);
        Assert.Equal(3, session.Counts.Done);
        Assert.Equal(3, session.FirstUnfinishedIndex);
        Assert.Equal(FieldPassVerdict.Pass, session.VerdictFor("layouts"));
        Assert.Equal(FieldPassVerdict.None, session.VerdictFor("downloads"));

        var record = session.RecordFor("layouts")!;
        Assert.Equal("all three worked", record.Note); // trimmed on the way in
        Assert.Equal(when, record.RecordedUtc);
    }

    [Fact]
    public void Session_UnknownStep_IsIgnored_AndClearingKeepsTheNote()
    {
        var session = EmptySession();
        Assert.False(session.SetVerdict("nope", FieldPassVerdict.Pass, "x"));
        Assert.Empty(session.Data.Records);

        session.SetVerdict("downloads", FieldPassVerdict.Pass, "fast");
        Assert.True(session.SetVerdict("downloads", FieldPassVerdict.None, "fast"));
        Assert.Equal(FieldPassVerdict.None, session.VerdictFor("downloads"));
        Assert.Equal(new FieldPassCounts(0, 0, 0, 9), session.Counts);
        var record = session.RecordFor("downloads")!;
        Assert.Equal("fast", record.Note);
        Assert.Null(record.RecordedUtc);
    }

    [Fact]
    public void Session_SetNote_DoesNotTouchTheVerdict()
    {
        var session = EmptySession();
        Assert.True(session.SetNote("app-lock", "don't use one"));
        Assert.Equal(FieldPassVerdict.None, session.VerdictFor("app-lock"));
        session.SetVerdict("app-lock", FieldPassVerdict.Skipped, "don't use one");
        Assert.True(session.SetNote("app-lock", "still don't"));
        Assert.Equal(FieldPassVerdict.Skipped, session.VerdictFor("app-lock"));
        Assert.Equal("still don't", session.RecordFor("app-lock")!.Note);
        Assert.False(session.SetNote("nope", "x"));
    }

    [Fact]
    public void Session_Reset_ClearsEverything()
    {
        var session = EmptySession();
        session.SetVerdict("layouts", FieldPassVerdict.Pass, "ok");
        session.SetVerdict("profiles", FieldPassVerdict.Skipped, "");
        session.Reset();
        Assert.Empty(session.Data.Records);
        Assert.Equal(new FieldPassCounts(0, 0, 0, 9), session.Counts);
        Assert.Equal(0, session.FirstUnfinishedIndex);
    }

    // ──────────────────────── store ────────────────────────

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arenadesktop-fieldpass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "field-pass.json");
    }

    [Fact]
    public void Store_RoundTrip_PreservesVerdictsNotesAndTimes()
    {
        var path = TempFile();
        var session = FieldPassSession.Open(path);
        var when = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);
        session.SetVerdict("battle-journal", FieldPassVerdict.Pass, "logged A won", when);
        session.SetVerdict("tray-hotkey", FieldPassVerdict.Problem, "hotkey did nothing", when);
        Assert.True(File.Exists(path)); // autosaved — there is no Save button

        var reopened = FieldPassSession.Open(path);
        Assert.Equal(new FieldPassCounts(1, 1, 0, 7), reopened.Counts);
        var record = reopened.RecordFor("battle-journal")!;
        Assert.Equal("logged A won", record.Note);
        Assert.Equal(when, record.RecordedUtc);
        // Records come back in catalogue order, not marking order.
        Assert.Equal(new[] { "battle-journal", "tray-hotkey" },
            reopened.Data.Records.Select(r => r.StepId).ToArray());
    }

    [Fact]
    public void Store_MissingFile_IsAFreshPass_AndResetSticks()
    {
        var path = TempFile();
        Assert.False(File.Exists(path));
        var session = FieldPassSession.Open(path);
        Assert.Equal(new FieldPassCounts(0, 0, 0, 9), session.Counts);
        session.SetVerdict("layouts", FieldPassVerdict.Pass, "");
        session.Reset();
        Assert.Equal(new FieldPassCounts(0, 0, 0, 9), FieldPassSession.Open(path).Counts);
    }

    [Fact]
    public void Store_CorruptFile_IsPreserved_AndLoadsFresh()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ this is not json ]");
        var data = FieldPassStore.Load(path);
        Assert.Empty(data.Records);
        var preserved = Directory.GetFiles(Path.GetDirectoryName(path)!, "field-pass.json.corrupt-*");
        Assert.Single(preserved);
        Assert.Equal("{ this is not json ]", File.ReadAllText(preserved[0]));
    }

    [Fact]
    public void Store_Load_DropsUnknownStepsDuplicatesAndBadVerdicts()
    {
        var path = TempFile();
        File.WriteAllText(path, """
            { "records": [
                { "stepId": "ghost", "verdict": 1, "note": "boo" },
                { "stepId": "layouts", "verdict": 99, "note": null },
                { "stepId": "layouts", "verdict": 1, "note": "second copy" },
                { "stepId": "downloads", "verdict": 2, "note": "kept" }
            ] }
            """);
        var data = FieldPassStore.Load(path);
        Assert.Equal(new[] { "layouts", "downloads" }, data.Records.Select(r => r.StepId).ToArray());
        Assert.Equal(FieldPassVerdict.None, data.Records[0].Verdict); // 99 isn't a verdict
        Assert.Equal("", data.Records[0].Note);
        Assert.Equal(FieldPassVerdict.Problem, data.Records[1].Verdict);
        Assert.Equal("kept", data.Records[1].Note);
    }

    // ──────────────────────── summary ────────────────────────

    [Fact]
    public void Summary_ProgressText_NamesTheStepAndEveryCount()
    {
        var session = EmptySession();
        session.SetVerdict("layouts", FieldPassVerdict.Pass, "");
        session.SetVerdict("battle-lab", FieldPassVerdict.Pass, "");
        var text = FieldPassSummary.ProgressText(session, 3);
        Assert.Equal("Step 3 of 9 · 2 passed · 0 with a problem · 0 skipped · 7 to go", text);
        // Out-of-range step numbers are clamped, never printed raw.
        Assert.StartsWith("Step 1 of 9", FieldPassSummary.ProgressText(session, 0));
        Assert.StartsWith("Step 9 of 9", FieldPassSummary.ProgressText(session, 42));
    }

    [Fact]
    public void Summary_Render_CarriesVersionCountsVerdictsAndNotes()
    {
        var session = EmptySession();
        var when = new DateTimeOffset(2026, 11, 2, 15, 45, 0, TimeSpan.FromHours(-6));
        session.SetVerdict("layouts", FieldPassVerdict.Pass, "rail is nice", when);
        session.SetVerdict("app-lock", FieldPassVerdict.Skipped, "", when);
        var text = FieldPassSummary.Render(session, "0.2.0",
            new DateTimeOffset(2026, 11, 2, 16, 0, 0, TimeSpan.FromHours(-6)));

        Assert.Contains("# ArenaDesktop field pass", text);
        Assert.Contains("ArenaDesktop version: 0.2.0", text);
        Assert.Contains("Date: 2026-11-02", text);
        Assert.Contains("Result: 1 passed · 0 with a problem · 1 skipped · 7 not done", text);
        Assert.Contains("### 1. Layouts — Passed", text);
        Assert.Contains("Working looks like: Each layout rearranges the window", text);
        Assert.Contains("Note: rail is nice", text);
        Assert.Contains($"Recorded: {when.ToLocalTime():yyyy-MM-dd HH:mm}", text);
        Assert.Contains("### 7. App lock — Skipped", text);
        Assert.Contains("### 9. Update check — Not done", text);
        Assert.Contains("does not verify the checks itself", text);
    }

    // ──────────────────────── launch surface ────────────────────────

    [Fact]
    public void CommandLine_FieldPassFlag_ParsesAndReducesToIntent()
    {
        var options = CommandLine.Parse(new[] { "--field-pass" });
        Assert.True(options.OpenFieldPass);
        var intent = LaunchIntent.FromCommandLine(options);
        Assert.Equal(LaunchIntentKind.OpenFieldPass, intent.Kind);
        // Round-trips through the pending-intent JSON like every kind.
        Assert.Equal(intent, LaunchIntent.TryFromJson(intent.ToJson()));
    }

    [Fact]
    public void Protocol_FieldPassRoute_Parses()
    {
        var parsed = LaunchIntent.ParseProtocolUri("arenadesktop://field-pass", Array.Empty<string>());
        Assert.False(parsed.IsRefusal);
        Assert.Equal(LaunchIntentKind.OpenFieldPass, parsed.Intent!.Kind);
        var refused = LaunchIntent.ParseProtocolUri("arenadesktop://field-pass/extra", Array.Empty<string>());
        Assert.True(refused.IsRefusal);
    }

    [Fact]
    public void Usage_DocumentsFieldPassFlagAndRoute()
    {
        var text = CommandLineUsage.Text("0.2.0");
        Assert.Contains("--field-pass", text);
        Assert.Contains("arenadesktop://field-pass", text);
    }
}
