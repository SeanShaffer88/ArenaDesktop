namespace ArenaDesktop.Core;

/// <summary>
/// One Arena mode (ArenaDesktop v0.1.0): a named place inside arena.ai
/// the app can take you to from the window, the tray, the Jump List,
/// the command line, and an arenadesktop:// link — one registry, every
/// surface (Storytold: everything is a command).
///
/// URLs verified against the live site on 2026-10-27: the home page IS
/// Battle; Direct Chat and Side by Side have their own pages under
/// /text/ (the model(s) are picked at the top of those pages); Agent,
/// Image, Code, Video and Web Search each have a top-level page; the
/// Leaderboard is its own page. If Arena moves a route, this file is
/// the one place to update.
/// </summary>
public sealed record ArenaMode(
    string Id,
    string Label,
    string Url,
    string ShortcutText,
    string Note)
{
    /// <summary>Only Battle shares the home address (see the class note).</summary>
    public bool SharesHomeUrl => Url == UrlPolicy.HomeUrl;
}

public static class ArenaModes
{
    public const string BattleId = "battle";
    public const string DirectId = "direct";
    public const string SideBySideId = "side-by-side";
    public const string LeaderboardId = "leaderboard";
    public const string AgentId = "agent";
    public const string ImageId = "image";
    public const string CodeId = "code";
    public const string VideoId = "video";
    public const string SearchId = "search";

    public static readonly IReadOnlyList<ArenaMode> All = new[]
    {
        new ArenaMode(BattleId, "Battle",
            "https://arena.ai/",
            "Ctrl+1",
            "Battle: two anonymous models answer your prompt — vote, then see who they were."),
        new ArenaMode(DirectId, "Direct Chat",
            "https://arena.ai/text/direct",
            "Ctrl+2",
            "Direct Chat: chat with one named model — pick it at the top of the page."),
        new ArenaMode(SideBySideId, "Side by Side",
            "https://arena.ai/text/side-by-side",
            "Ctrl+3",
            "Side by Side: compare two named models — pick both at the top of the page."),
        new ArenaMode(LeaderboardId, "Leaderboard",
            "https://arena.ai/leaderboard",
            "Ctrl+4",
            "Leaderboard: the public model rankings, by category."),
        new ArenaMode(AgentId, "Agent",
            "https://arena.ai/agent",
            "Ctrl+5",
            "Agent Mode: Arena's agents take on longer, multi-step tasks."),
        new ArenaMode(ImageId, "Image",
            "https://arena.ai/image",
            "Ctrl+6",
            "Image: generate and compare images from the top image models."),
        new ArenaMode(CodeId, "Code",
            "https://arena.ai/code",
            "Ctrl+7",
            "Code: build websites and apps with the top coding models."),
        new ArenaMode(VideoId, "Video",
            "https://arena.ai/video",
            "Ctrl+8",
            "Video: generate and compare videos from the top video models."),
        new ArenaMode(SearchId, "Web Search",
            "https://arena.ai/search",
            "Ctrl+9",
            "Web Search: search the web with AI search models."),
    };

    /// <summary>Look a mode up by id (case-insensitive; spaces read as dashes, so "side by side" works too).</summary>
    public static bool TryGet(string? id, out ArenaMode mode)
    {
        mode = All[0];
        if (string.IsNullOrWhiteSpace(id))
            return false;
        var normalized = id.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        foreach (var candidate in All)
        {
            if (candidate.Id == normalized)
            {
                mode = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary>The URL a mode navigates to; unknown ids fall back to the Arena home page.</summary>
    public static string UrlFor(string? id) =>
        TryGet(id, out var mode) ? mode.Url : UrlPolicy.HomeUrl;

    /// <summary>
    /// Which mode a URL belongs to, for status text and tests: the
    /// leaderboard path is its own mode; any other arena.ai /
    /// lmarena.ai page is the main (Battle-first) experience. Non-Arena
    /// URLs return null.
    /// </summary>
    public static ArenaMode? ForUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != "https")
            return null;
        if (!uri.Host.Equals("arena.ai", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.Equals("lmarena.ai", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.EndsWith(".arena.ai", StringComparison.OrdinalIgnoreCase))
            return null;
        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var mode in All)
        {
            if (mode.Id == BattleId) continue;
            if (Uri.TryCreate(mode.Url, UriKind.Absolute, out var modeUri)
                && path.Equals(modeUri.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return mode;
        }
        if (path.StartsWith("/leaderboard", StringComparison.OrdinalIgnoreCase))
            return All[3];
        return All[0];
    }
}
