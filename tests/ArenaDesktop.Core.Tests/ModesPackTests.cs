using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// The modes pack (v0.3.0): route-health classification + formatting,
/// the startup-mode resolver, and favorite-mode sanitizing. The HTTP
/// probing itself is shell work; everything decided about its results
/// is decided here.
/// </summary>
public class ModesPackTests
{
    // ───────────────────── route health: classification ─────────────────────

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(399)]
    [InlineData(401)]
    [InlineData(403)]
    public void RouteHealth_ExistingRoutes_AreReachable(int statusCode) =>
        Assert.Equal(RouteVerdict.Reachable, RouteHealth.Classify(statusCode, RouteProbeFailure.None));

    [Fact]
    public void RouteHealth_NotFound_IsAProblem() =>
        Assert.Equal(RouteVerdict.Problem, RouteHealth.Classify(404, RouteProbeFailure.None));

    [Theory]
    [InlineData(100)]
    [InlineData(400)]
    [InlineData(405)]
    [InlineData(418)]
    [InlineData(500)]
    [InlineData(503)]
    public void RouteHealth_OtherStatuses_AreUnreachable(int statusCode) =>
        Assert.Equal(RouteVerdict.Unreachable, RouteHealth.Classify(statusCode, RouteProbeFailure.None));

    [Theory]
    [InlineData(RouteProbeFailure.Timeout)]
    [InlineData(RouteProbeFailure.NetworkError)]
    [InlineData(RouteProbeFailure.Other)]
    public void RouteHealth_Failures_AreUnreachable(RouteProbeFailure failure) =>
        Assert.Equal(RouteVerdict.Unreachable, RouteHealth.Classify(null, failure));

    [Fact]
    public void RouteHealth_NoStatusAndNoFailure_IsUnreachable() =>
        Assert.Equal(RouteVerdict.Unreachable, RouteHealth.Classify(null, RouteProbeFailure.None));

    [Fact]
    public void RouteHealth_FailureWinsOverAStatus() =>
        // A probe that timed out mid-flight has no trustworthy status.
        Assert.Equal(RouteVerdict.Unreachable, RouteHealth.Classify(200, RouteProbeFailure.Timeout));

    // ───────────────────── route health: formatting ─────────────────────

    [Fact]
    public void RouteHealth_Checklist_CoversEveryMode_Pending()
    {
        var checklist = RouteHealth.Checklist();
        Assert.Equal(ArenaModes.All.Count, checklist.Count);
        Assert.Equal(ArenaModes.All.Select(m => m.Id), checklist.Select(e => e.Mode.Id));
        Assert.All(checklist, e => Assert.Null(e.Verdict));
        Assert.Equal("  Battle: Not checked yet", RouteHealth.FormatLine(checklist[0]));
    }

    [Fact]
    public void RouteHealth_FormatLine_SaysTheVerdictPlainly()
    {
        var battle = ArenaModes.All[0];
        var leaderboard = ArenaModes.All[3];
        var agent = ArenaModes.All[4];
        Assert.Equal("  Battle: Reachable",
            RouteHealth.FormatLine(new RouteHealthEntry(battle, RouteVerdict.Reachable, 200)));
        Assert.Equal("  Leaderboard: Problem — the page wasn't found (404)",
            RouteHealth.FormatLine(new RouteHealthEntry(leaderboard, RouteVerdict.Problem, 404)));
        Assert.Equal("  Agent: Unreachable — the server answered HTTP 500",
            RouteHealth.FormatLine(new RouteHealthEntry(agent, RouteVerdict.Unreachable, 500)));
        Assert.Equal("  Agent: Unreachable — no answer (check the connection)",
            RouteHealth.FormatLine(new RouteHealthEntry(agent, RouteVerdict.Unreachable, null)));
    }

    [Fact]
    public void RouteHealth_FormatSummary_CountsTheVerdicts()
    {
        var entries = new[]
        {
            new RouteHealthEntry(ArenaModes.All[0], RouteVerdict.Reachable, 200),
            new RouteHealthEntry(ArenaModes.All[1], RouteVerdict.Reachable, 301),
            new RouteHealthEntry(ArenaModes.All[3], RouteVerdict.Problem, 404),
            new RouteHealthEntry(ArenaModes.All[4], RouteVerdict.Unreachable, null),
            RouteHealthEntry.Pending(ArenaModes.All[5]),
        };
        // The pending entry is not part of the count.
        Assert.Equal("  Route check: 2 reachable, 1 problem, 1 unreachable (of 4 modes).",
            RouteHealth.FormatSummary(entries));
        Assert.Equal("  No routes checked yet.",
            RouteHealth.FormatSummary(new[] { RouteHealthEntry.Pending(ArenaModes.All[0]) }));
    }

    [Fact]
    public void RouteHealth_FormatSection_HasHeaderLinesAndSummary()
    {
        var section = RouteHealth.FormatSection(new[]
        {
            new RouteHealthEntry(ArenaModes.All[0], RouteVerdict.Reachable, 200),
        });
        var lines = section.Split(Environment.NewLine);
        Assert.StartsWith("Mode routes (reachability only", lines[0]);
        Assert.Equal("  Battle: Reachable", lines[1]);
        Assert.Equal("  Route check: 1 reachable, 0 problem, 0 unreachable (of 1 modes).", lines[2]);
    }

    // ───────────────────── startup mode ─────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("battle", null)]
    [InlineData("battle", "code")]
    [InlineData("nonsense", "code")]
    public void StartupMode_DefaultAndUnknown_OpenBattle(string? setting, string? lastModeId) =>
        Assert.Equal(ArenaModes.BattleId,
            StartupModes.ResolveStartupMode(setting, lastModeId).Id);

    [Theory]
    [InlineData("code", "code")]
    [InlineData("IMAGE", "image")]
    [InlineData("side by side", "side-by-side")]
    public void StartupMode_Last_OpensTheRecordedMode(string lastModeId, string expectedId) =>
        Assert.Equal(expectedId, StartupModes.ResolveStartupMode("last", lastModeId).Id);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    public void StartupMode_Last_WithoutAUsableRecord_OpensBattle(string? lastModeId) =>
        Assert.Equal(ArenaModes.BattleId,
            StartupModes.ResolveStartupMode("last", lastModeId).Id);

    [Theory]
    [InlineData("mode:image", "image")]
    [InlineData("mode:leaderboard", "leaderboard")]
    [InlineData("MODE:Code", "code")]
    [InlineData("mode:side by side", "side-by-side")]
    public void StartupMode_Specific_OpensThatMode(string setting, string expectedId) =>
        Assert.Equal(expectedId, StartupModes.ResolveStartupMode(setting, "video").Id);

    [Theory]
    [InlineData("mode:nope")]
    [InlineData("mode:")]
    [InlineData("mode:  ")]
    public void StartupMode_SpecificWithUnknownMode_OpensBattle(string setting) =>
        Assert.Equal(ArenaModes.BattleId,
            StartupModes.ResolveStartupMode(setting, null).Id);

    [Theory]
    [InlineData(null, "battle")]
    [InlineData("garbage", "battle")]
    [InlineData(" BATTLE ", "battle")]
    [InlineData("Last", "last")]
    [InlineData("mode:side by side", "mode:side-by-side")]
    [InlineData("mode:nope", "battle")]
    public void StartupMode_Sanitize_Canonicalizes(string? setting, string expected) =>
        Assert.Equal(expected, StartupModes.Sanitize(setting));

    [Fact]
    public void StartupMode_KindAndSpecificId_Parse()
    {
        Assert.Equal(StartupModeKind.Battle, StartupModes.KindOf(null));
        Assert.Equal(StartupModeKind.Battle, StartupModes.KindOf("mode:nope"));
        Assert.Equal(StartupModeKind.Last, StartupModes.KindOf("last"));
        Assert.Equal(StartupModeKind.Specific, StartupModes.KindOf("mode:video"));
        Assert.Equal("video", StartupModes.SpecificModeId("mode:video"));
        Assert.Null(StartupModes.SpecificModeId("last"));
        Assert.Equal("mode:video", StartupModes.ForMode("video"));
        Assert.Equal("battle", StartupModes.ForMode("nope"));
    }

    [Fact]
    public void Settings_Sanitized_RepairsTheModesPackFields()
    {
        var settings = new AppSettings
        {
            StartupMode = "mode:nope",
            FavoriteModeIds = new List<string> { "code", "nope", "code", "image", "video", "search" },
            LastModeIdByProfile = new Dictionary<string, string>
            {
                ["default"] = "IMAGE",
                ["ghost"] = "code",
            },
        };
        settings.Sanitized();
        Assert.Equal("battle", settings.StartupMode);
        // Unknown dropped, duplicate dropped, capped at 3, order kept.
        Assert.Equal(new[] { "code", "image", "video" }, settings.FavoriteModeIds);
        // The ghost profile's entry drops; the real one canonicalizes.
        Assert.Equal("image", settings.ActiveLastModeId);
        Assert.False(settings.LastModeIdByProfile.ContainsKey("ghost"));
    }

    [Fact]
    public void Settings_Sanitized_DropsUnknownLastModes()
    {
        var settings = new AppSettings();
        settings.LastModeIdByProfile["default"] = "not-a-mode";
        settings.Sanitized();
        Assert.Null(settings.ActiveLastModeId);
        Assert.Empty(settings.LastModeIdByProfile);
    }

    // ───────────────────── favorite modes ─────────────────────

    [Fact]
    public void Favorites_Sanitize_DropsUnknownDuplicatesAndExtras()
    {
        var clean = FavoriteModes.Sanitize(
            new string?[] { "video", "nope", "VIDEO", null, "code", "image", "search" });
        Assert.Equal(new[] { "video", "code", "image" }, clean);
        Assert.Empty(FavoriteModes.Sanitize(null));
        Assert.Empty(FavoriteModes.Sanitize(new string?[] { "nope" }));
    }

    [Fact]
    public void Favorites_Ordered_PutsFavoritesFirst_ThenRegistryOrder()
    {
        var ordered = FavoriteModes.Ordered(new[] { "video", "code" });
        Assert.Equal(
            new[] { "video", "code", "battle", "direct", "side-by-side", "leaderboard", "agent", "image", "search" },
            ordered.Select(m => m.Id).ToArray());
        Assert.Equal(ArenaModes.All.Count, ordered.Count);
    }

    [Fact]
    public void Favorites_Ordered_WithoutFavorites_IsRegistryOrder() =>
        Assert.Equal(ArenaModes.All.Select(m => m.Id),
            FavoriteModes.Ordered(null).Select(m => m.Id));
}
