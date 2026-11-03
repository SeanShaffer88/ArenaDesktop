namespace ArenaDesktop.Core;

// ───────────────────── mode route health (v0.3.0, modes pack) ─────────────────────

/// <summary>The verdict for one mode route (v0.3.0): plain words the Diagnostics report can print as-is.</summary>
public enum RouteVerdict
{
    /// <summary>The route answered like an existing page: any 2xx/3xx, or 401/403 (sign-in walls still prove the route exists).</summary>
    Reachable,

    /// <summary>The server answered 404 — Arena has moved or renamed this route; the registry in ArenaModes.cs needs an update.</summary>
    Problem,

    /// <summary>No usable answer: a network error, a timeout, or a status that is neither "exists" nor "not found" (5xx and the like).</summary>
    Unreachable,
}

/// <summary>How a route probe failed when it produced no HTTP status (v0.3.0). The shell maps its exceptions onto these; Core never sees an exception type.</summary>
public enum RouteProbeFailure
{
    None,
    Timeout,
    NetworkError,
    Other,
}

/// <summary>
/// One mode's route-health result (v0.3.0): the mode, the verdict
/// (null = not checked yet), and the HTTP status when one came back.
/// Pure data — the probing itself is a shell concern.
/// </summary>
public sealed record RouteHealthEntry(ArenaMode Mode, RouteVerdict? Verdict, int? StatusCode)
{
    /// <summary>An unchecked entry for <paramref name="mode"/> — the checklist before any probe runs.</summary>
    public static RouteHealthEntry Pending(ArenaMode mode) => new(mode, null, null);
}

/// <summary>
/// The mode route-health model (v0.3.0): the checklist over the
/// ArenaModes registry, the status classification, and the report
/// formatting. The check is REACHABILITY ONLY by design: the shell
/// asks each route's server to answer (HEAD, GET if HEAD is refused)
/// and discards any response body unread — nothing about page
/// content is ever looked at, matching the app's standing rule.
/// </summary>
public static class RouteHealth
{
    /// <summary>The checklist: one pending entry per registered mode, in registry order.</summary>
    public static IReadOnlyList<RouteHealthEntry> Checklist() =>
        ArenaModes.All.Select(RouteHealthEntry.Pending).ToList();

    /// <summary>
    /// Classify one probe outcome. A failure (timeout, network error,
    /// anything else with no status) is Unreachable; 2xx/3xx and
    /// 401/403 mean the route exists (Reachable); 404 is a Problem —
    /// the route itself is gone; every other status is Unreachable.
    /// </summary>
    public static RouteVerdict Classify(int? statusCode, RouteProbeFailure failure)
    {
        if (failure != RouteProbeFailure.None || statusCode is null)
            return RouteVerdict.Unreachable;
        var status = statusCode.Value;
        if (status is >= 200 and <= 399 || status is 401 or 403)
            return RouteVerdict.Reachable;
        if (status == 404)
            return RouteVerdict.Problem;
        return RouteVerdict.Unreachable;
    }

    /// <summary>The plain verdict words for one entry, with the status folded in where it explains the verdict.</summary>
    public static string VerdictText(RouteHealthEntry entry) => entry.Verdict switch
    {
        null => "Not checked yet",
        RouteVerdict.Reachable => "Reachable",
        RouteVerdict.Problem => "Problem — the page wasn't found (404)",
        RouteVerdict.Unreachable when entry.StatusCode is { } status
            => $"Unreachable — the server answered HTTP {status}",
        _ => "Unreachable — no answer (check the connection)",
    };

    /// <summary>One report line: two-space indent, the mode's label, its verdict.</summary>
    public static string FormatLine(RouteHealthEntry entry) =>
        $"  {entry.Mode.Label}: {VerdictText(entry)}";

    /// <summary>The summary line over the checked entries (pending entries don't count).</summary>
    public static string FormatSummary(IEnumerable<RouteHealthEntry> entries)
    {
        var checkedEntries = entries.Where(e => e.Verdict is not null).ToList();
        if (checkedEntries.Count == 0)
            return "  No routes checked yet.";
        var reachable = checkedEntries.Count(e => e.Verdict == RouteVerdict.Reachable);
        var problem = checkedEntries.Count(e => e.Verdict == RouteVerdict.Problem);
        var unreachable = checkedEntries.Count(e => e.Verdict == RouteVerdict.Unreachable);
        return $"  Route check: {reachable} reachable, {problem} problem, {unreachable} unreachable (of {checkedEntries.Count} modes).";
    }

    /// <summary>The whole Diagnostics section: the honesty header, one line per mode in registry order, the summary.</summary>
    public static string FormatSection(IEnumerable<RouteHealthEntry> entries)
    {
        var list = entries.ToList();
        var lines = new List<string>
        {
            "Mode routes (reachability only — each route's server was asked to answer; nothing was read from the pages)",
        };
        lines.AddRange(list.Select(FormatLine));
        lines.Add(FormatSummary(list));
        return string.Join(Environment.NewLine, lines);
    }
}
