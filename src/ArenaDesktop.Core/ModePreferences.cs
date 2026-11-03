namespace ArenaDesktop.Core;

// ───────────────────── startup mode & favorite modes (v0.3.0, modes pack) ─────────────────────

/// <summary>Which shape a <see cref="StartupModes"/> setting string takes (v0.3.0).</summary>
public enum StartupModeKind
{
    /// <summary>Open Battle (the Arena home page) — the long-standing behavior, and the default.</summary>
    Battle,

    /// <summary>Open the mode the user last navigated to (per profile; Battle when none is recorded).</summary>
    Last,

    /// <summary>Open one specific mode every launch.</summary>
    Specific,
}

/// <summary>
/// The startup-mode setting (v0.3.0): ONE string in settings with
/// three shapes — "battle" (default), "last", or "mode:&lt;id&gt;".
/// It decides which Arena MODE a launch opens with when the launch
/// would otherwise land on the Arena home page. It deliberately does
/// not replace the per-profile start page (S5 #47): a restored last
/// page or a valid custom start page still wins, and safe mode still
/// starts at Home — the startup mode only picks the mode page that
/// stands in for Battle. Sanitizing is total: anything unrecognized
/// reads as "battle", so a hand-edited file can never strand a
/// launch on a mode that doesn't exist.
/// </summary>
public static class StartupModes
{
    /// <summary>The "open Battle" setting value (also the default and the repair target).</summary>
    public const string Battle = "battle";

    /// <summary>The "open the last mode I used" setting value.</summary>
    public const string Last = "last";

    /// <summary>The prefix of the specific-mode shape: "mode:" + a registry id.</summary>
    public const string SpecificPrefix = "mode:";

    /// <summary>The setting value that opens <paramref name="modeId"/> every launch, or "battle" when the id names no mode.</summary>
    public static string ForMode(string? modeId) =>
        ArenaModes.TryGet(modeId, out var mode) ? SpecificPrefix + mode.Id : Battle;

    /// <summary>Which shape <paramref name="setting"/> takes; anything unrecognized is Battle.</summary>
    public static StartupModeKind KindOf(string? setting) => Parse(setting).Kind;

    /// <summary>The specific mode's id when <paramref name="setting"/> has the specific shape, else null.</summary>
    public static string? SpecificModeId(string? setting) => Parse(setting).ModeId;

    /// <summary>Canonicalize a stored setting: a known shape with a canonical mode id, else "battle".</summary>
    public static string Sanitize(string? setting)
    {
        var (kind, modeId) = Parse(setting);
        return kind switch
        {
            StartupModeKind.Last => Last,
            StartupModeKind.Specific when modeId is not null => SpecificPrefix + modeId,
            _ => Battle,
        };
    }

    /// <summary>
    /// The mode a launch opens with: the specific mode for
    /// "mode:&lt;id&gt;", the recorded last mode for "last", Battle
    /// otherwise — and Battle again whenever a named mode doesn't
    /// exist, so resolution can never fail.
    /// </summary>
    public static ArenaMode ResolveStartupMode(string? setting, string? lastModeId)
    {
        var (kind, modeId) = Parse(setting);
        switch (kind)
        {
            case StartupModeKind.Specific:
                if (ArenaModes.TryGet(modeId, out var specific))
                    return specific;
                break;
            case StartupModeKind.Last:
                if (ArenaModes.TryGet(lastModeId, out var last))
                    return last;
                break;
        }
        return ArenaModes.All[0];
    }

    private static (StartupModeKind Kind, string? ModeId) Parse(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
            return (StartupModeKind.Battle, null);
        var value = setting.Trim();
        if (value.Equals(Last, StringComparison.OrdinalIgnoreCase))
            return (StartupModeKind.Last, null);
        if (value.StartsWith(SpecificPrefix, StringComparison.OrdinalIgnoreCase)
            && ArenaModes.TryGet(value[SpecificPrefix.Length..], out var mode))
            return (StartupModeKind.Specific, mode.Id);
        return (StartupModeKind.Battle, null);
    }
}

/// <summary>
/// Favorite modes (v0.3.0): up to three mode ids, in the user's
/// order. Favorites lead the tray's Arena modes submenu and the
/// taskbar Jump List's modes category; everywhere else the registry
/// order stands. Sanitizing is the same discipline as the rest of
/// settings: unknown ids drop, duplicates drop, the cap holds.
/// </summary>
public static class FavoriteModes
{
    /// <summary>At most this many favorites — a short list is the point.</summary>
    public const int MaxFavorites = 3;

    /// <summary>Canonical favorite ids: known modes only, deduped, the user's order kept, capped at <see cref="MaxFavorites"/>.</summary>
    public static List<string> Sanitize(IEnumerable<string?>? ids)
    {
        var result = new List<string>();
        if (ids is null)
            return result;
        foreach (var id in ids)
        {
            if (result.Count >= MaxFavorites)
                break;
            if (ArenaModes.TryGet(id, out var mode) && !result.Contains(mode.Id))
                result.Add(mode.Id);
        }
        return result;
    }

    /// <summary>The modes in display order: favorites first (the user's order), then the remaining modes in registry order. Empty favorites = registry order, unchanged.</summary>
    public static IReadOnlyList<ArenaMode> Ordered(IEnumerable<string?>? favoriteIds)
    {
        var favorites = Sanitize(favoriteIds)
            .Select(id => ArenaModes.TryGet(id, out var mode) ? mode : null)
            .Where(m => m is not null)
            .Cast<ArenaMode>()
            .ToList();
        var favoriteSet = new HashSet<string>(favorites.Select(m => m.Id));
        return favorites
            .Concat(ArenaModes.All.Where(m => !favoriteSet.Contains(m.Id)))
            .ToList();
    }
}
