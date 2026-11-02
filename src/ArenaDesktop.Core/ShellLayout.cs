namespace ArenaDesktop.Core;

/// <summary>
/// Shell layout (v0.2.0): how the shell's own chrome is arranged
/// around the Arena page. Shell-only by design — the page is never
/// restyled, moved, or hidden by a layout; a layout only changes the
/// toolbar row, the status presentation, and (Left Rail) the
/// navigation rail down the left edge. Ported from MuseDesktop
/// 2.0.0's shell-layout system (the parity target), adapted to
/// ArenaDesktop: Arena has no chrome-density setting, so Standard
/// resolves to this app's long-standing fixed chrome numbers
/// instead of a density table.
/// </summary>
public enum ShellLayout
{
    /// <summary>The long-standing arrangement: full toolbar on top, full status bar along the bottom.</summary>
    Standard,

    /// <summary>Slim 30px toolbar; the status bar is replaced by a small floating status pill over the page's bottom-right corner, so the page gains the bar's height.</summary>
    CompactFocus,

    /// <summary>A 56px vertical rail down the left edge carries navigation and the Arena modes, with Settings and the ⋯ menu at its foot; a thin 30px title strip runs across the top and the full status bar stays along the bottom.</summary>
    LeftRail,
}

/// <summary>Stepping between shell layouts, for anything that cycles the layout choice.</summary>
public static class ShellLayoutCycle
{
    /// <summary>The next layout in the cycle Standard → Compact Focus → Left Rail → Standard. An undefined value steps to Standard, the safe repair.</summary>
    public static ShellLayout Next(ShellLayout layout) => layout switch
    {
        ShellLayout.Standard => ShellLayout.CompactFocus,
        ShellLayout.CompactFocus => ShellLayout.LeftRail,
        _ => ShellLayout.Standard,
    };
}

/// <summary>The chrome measurements a shell layout implies, in WPF device-independent pixels.</summary>
public sealed record ShellLayoutMetrics(
    double ToolbarHeight,
    bool StatusBarVisible,
    bool StatusPillVisible,
    double StatusBarHeight,
    double RailWidth)
{
    /// <summary>The long-standing toolbar height (the Standard layout's row 0).</summary>
    public const double StandardToolbarHeight = 40;

    /// <summary>The long-standing status-bar height (the Standard layout's bottom row).</summary>
    public const double StandardStatusBarHeight = 24;

    /// <summary>The slim strip height Compact Focus and Left Rail pin their top row to.</summary>
    public const double SlimStripHeight = 30;

    /// <summary>The Left Rail's width when that layout is active.</summary>
    public const double LeftRailWidth = 56;

    /// <summary>Whether the layout puts a navigation rail down the left edge.</summary>
    public bool RailVisible => RailWidth > 0;

    /// <summary>
    /// Resolve a layout to its chrome measurements. Standard is
    /// exactly the long-standing numbers (no silent change for
    /// existing users). Compact Focus pins the toolbar to 30px and
    /// hides the status bar in favour of the pill. Left Rail pins the
    /// same 30px strip (it carries only the title and zoom there —
    /// navigation lives on the rail) and keeps the status bar.
    /// </summary>
    public static ShellLayoutMetrics For(ShellLayout layout) => layout switch
    {
        ShellLayout.CompactFocus => new ShellLayoutMetrics(
            ToolbarHeight: SlimStripHeight,
            StatusBarVisible: false,
            StatusPillVisible: true,
            StatusBarHeight: 0,
            RailWidth: 0),
        ShellLayout.LeftRail => new ShellLayoutMetrics(
            ToolbarHeight: SlimStripHeight,
            StatusBarVisible: true,
            StatusPillVisible: false,
            StatusBarHeight: StandardStatusBarHeight,
            RailWidth: LeftRailWidth),
        _ => new ShellLayoutMetrics(
            ToolbarHeight: StandardToolbarHeight,
            StatusBarVisible: true,
            StatusPillVisible: false,
            StatusBarHeight: StandardStatusBarHeight,
            RailWidth: 0),
    };

    /// <summary>
    /// How much taller the page area is than under Standard: the
    /// toolbar saving plus the whole status bar. Pinned by tests so
    /// the claim and the code cannot drift apart.
    /// </summary>
    public static double PageHeightGain(ShellLayout layout)
    {
        var standard = For(ShellLayout.Standard);
        var mine = For(layout);
        return (standard.ToolbarHeight - mine.ToolbarHeight)
             + (standard.StatusBarHeight - mine.StatusBarHeight);
    }
}
