using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>Arena modes (ArenaDesktop v0.1.0): the registry every surface dispatches through.</summary>
public class ArenaModesTests
{
    [Fact]
    public void Registry_HasNineModes_InShortcutOrder()
    {
        Assert.Equal(9, ArenaModes.All.Count);
        Assert.Equal("battle", ArenaModes.All[0].Id);
        Assert.Equal("direct", ArenaModes.All[1].Id);
        Assert.Equal("side-by-side", ArenaModes.All[2].Id);
        Assert.Equal("leaderboard", ArenaModes.All[3].Id);
        Assert.Equal("agent", ArenaModes.All[4].Id);
        Assert.Equal("image", ArenaModes.All[5].Id);
        Assert.Equal("code", ArenaModes.All[6].Id);
        Assert.Equal("video", ArenaModes.All[7].Id);
        Assert.Equal("search", ArenaModes.All[8].Id);
        Assert.Equal("Ctrl+1", ArenaModes.All[0].ShortcutText);
        Assert.Equal("Ctrl+9", ArenaModes.All[8].ShortcutText);
    }

    [Theory]
    [InlineData("battle")]
    [InlineData("BATTLE")]
    [InlineData(" direct ")]
    [InlineData("side by side")]
    [InlineData("side_by_side")]
    [InlineData("Leaderboard")]
    public void TryGet_NormalizesIds(string id)
    {
        Assert.True(ArenaModes.TryGet(id, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("settings")]
    [InlineData("chat")]
    public void TryGet_RejectsUnknown(string? id)
    {
        Assert.False(ArenaModes.TryGet(id, out _));
    }

    [Fact]
    public void UrlFor_EveryModeHasItsOwnVerifiedPage()
    {
        Assert.Equal("https://arena.ai/", ArenaModes.UrlFor("battle"));
        Assert.Equal("https://arena.ai/text/direct", ArenaModes.UrlFor("direct"));
        Assert.Equal("https://arena.ai/text/side-by-side", ArenaModes.UrlFor("side-by-side"));
        Assert.Equal("https://arena.ai/leaderboard", ArenaModes.UrlFor("leaderboard"));
        Assert.Equal("https://arena.ai/agent", ArenaModes.UrlFor("agent"));
        Assert.Equal("https://arena.ai/image", ArenaModes.UrlFor("image"));
        Assert.Equal("https://arena.ai/code", ArenaModes.UrlFor("code"));
        Assert.Equal("https://arena.ai/video", ArenaModes.UrlFor("video"));
        Assert.Equal("https://arena.ai/search", ArenaModes.UrlFor("search"));
        Assert.Equal("https://arena.ai/", ArenaModes.UrlFor("nonsense"));
        Assert.Equal("https://arena.ai/", ArenaModes.UrlFor(null));
        // Only Battle shares the home address.
        Assert.True(ArenaModes.All[0].SharesHomeUrl);
        Assert.All(ArenaModes.All.Skip(1), m => Assert.False(m.SharesHomeUrl));
    }

    [Theory]
    [InlineData("https://arena.ai/leaderboard", "leaderboard")]
    [InlineData("https://arena.ai/leaderboard/chat/text", "leaderboard")]
    [InlineData("https://arena.ai/text/direct?model_a=max", "direct")]
    [InlineData("https://arena.ai/text/side-by-side", "side-by-side")]
    [InlineData("https://arena.ai/agent", "agent")]
    [InlineData("https://arena.ai/image", "image")]
    [InlineData("https://arena.ai/code", "code")]
    [InlineData("https://arena.ai/video", "video")]
    [InlineData("https://arena.ai/search", "search")]
    [InlineData("https://arena.ai/text", "battle")]
    [InlineData("https://arena.ai/", "battle")]
    [InlineData("https://lmarena.ai/", "battle")]
    [InlineData("https://chat.arena.ai/x", "battle")]
    public void ForUrl_ClassifiesArenaUrls(string url, string expectedId)
    {
        Assert.Equal(expectedId, ArenaModes.ForUrl(url)?.Id);
    }

    [Theory]
    [InlineData("https://example.com/leaderboard")]
    [InlineData("https://arena.ai.evil.com/leaderboard")]
    [InlineData("http://arena.ai/leaderboard")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void ForUrl_RejectsNonArenaUrls(string? url)
    {
        Assert.Null(ArenaModes.ForUrl(url));
    }

    [Theory]
    [InlineData("--battle", "battle")]
    [InlineData("--direct", "direct")]
    [InlineData("--side-by-side", "side-by-side")]
    [InlineData("--leaderboard", "leaderboard")]
    [InlineData("--agent", "agent")]
    [InlineData("--image", "image")]
    [InlineData("--code", "code")]
    [InlineData("--video", "video")]
    [InlineData("--search", "search")]
    public void CommandLine_ModeFlags(string flag, string expectedId)
    {
        var options = CommandLine.Parse(new[] { flag });
        Assert.Equal(expectedId, options.ArenaModeId);
        var intent = LaunchIntent.FromCommandLine(options);
        Assert.Equal(LaunchIntentKind.GoArenaMode, intent.Kind);
        Assert.Equal(expectedId, intent.ModeId);
    }

    [Fact]
    public void CommandLine_ModeByName_AndUnknownMode()
    {
        Assert.Equal("leaderboard", CommandLine.Parse(new[] { "--mode", "leaderboard" }).ArenaModeId);
        Assert.Equal("side-by-side", CommandLine.Parse(new[] { "--mode=side by side" }).ArenaModeId);
        var unknown = CommandLine.Parse(new[] { "--mode", "nope" });
        Assert.Null(unknown.ArenaModeId);
        Assert.Contains("--mode", unknown.Unknown);
    }

    [Theory]
    [InlineData("arenadesktop://battle", "battle")]
    [InlineData("arenadesktop://direct", "direct")]
    [InlineData("arenadesktop://side-by-side", "side-by-side")]
    [InlineData("arenadesktop://leaderboard", "leaderboard")]
    [InlineData("arenadesktop://agent", "agent")]
    [InlineData("arenadesktop://image", "image")]
    [InlineData("arenadesktop://code", "code")]
    [InlineData("arenadesktop://video", "video")]
    [InlineData("arenadesktop://search", "search")]
    public void Protocol_ModeRoutes(string link, string expectedId)
    {
        var result = LaunchIntent.ParseProtocolUri(link, new[] { "default" });
        Assert.False(result.IsRefusal);
        Assert.Equal(LaunchIntentKind.GoArenaMode, result.Intent!.Kind);
        Assert.Equal(expectedId, result.Intent.ModeId);
    }

    [Fact]
    public void Protocol_ModeRouteWithExtraPath_IsRefused()
    {
        var result = LaunchIntent.ParseProtocolUri("arenadesktop://leaderboard/extra", new[] { "default" });
        Assert.True(result.IsRefusal);
    }

    [Fact]
    public void Intent_RoundTripsThroughJson_WithMode()
    {
        var intent = new LaunchIntent(LaunchIntentKind.GoArenaMode, ModeId: "leaderboard");
        var back = LaunchIntent.TryFromJson(intent.ToJson());
        Assert.Equal(intent, back);
        // A mode intent with no (or an unknown) mode is unreadable, never a crash.
        Assert.Null(LaunchIntent.TryFromJson("{\"kind\":\"GoArenaMode\"}"));
        Assert.Null(LaunchIntent.TryFromJson("{\"kind\":\"GoArenaMode\",\"modeId\":\"nope\"}"));
    }

    [Fact]
    public void Usage_DocumentsModeFlagsAndRoutes()
    {
        var text = CommandLineUsage.Text("0.1.0");
        foreach (var token in new[] { "--battle", "--direct", "--side-by-side", "--leaderboard", "--agent",
                                      "--image", "--code", "--video", "--search", "--mode", "--ask",
                                      "arenadesktop://battle", "arenadesktop://leaderboard",
                                      "arenadesktop://agent", "arenadesktop://search" })
            Assert.Contains(token, text);
    }

    [Fact]
    public void ShortcutCatalog_ListsModeGestures_AsFixed()
    {
        foreach (var id in new[] { "mode-battle", "mode-direct", "mode-side-by-side", "mode-leaderboard",
                                   "mode-agent", "mode-image", "mode-code", "mode-video", "mode-search" })
        {
            var action = ShortcutCatalog.Find(id);
            Assert.NotNull(action);
            Assert.False(action!.Remappable);
        }
        var fixedGestures = ShortcutCatalog.FixedGestures();
        Assert.Contains(fixedGestures.Keys, g => g.Format() == "Ctrl+D1");
        Assert.Contains(fixedGestures.Keys, g => g.Format() == "Ctrl+D4");
        Assert.Contains(fixedGestures.Keys, g => g.Format() == "Ctrl+D9");
    }
}
