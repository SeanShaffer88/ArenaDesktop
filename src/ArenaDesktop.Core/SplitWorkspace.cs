namespace ArenaDesktop.Core;

/// <summary>
/// The side pane (v0.8.0): Battle Lab lives beside the Arena page
/// instead of in a window of its own, so a prompt can be read while
/// writing the battle it belongs to. This is the part of that which
/// is pure arithmetic — the shell asks it where the splitter goes,
/// whether the window is wide enough for two panes at all, and which
/// of the Lab's three columns still fit.
///
/// Split, not overlay: the page keeps its own width and the pane takes
/// space from it, because the whole point is reading both at once.
/// Below a floor the page would be too narrow to use, so the pane
/// refuses and the caller stays a single pane rather than showing two
/// slivers. The numbers are here, with tests, rather than scattered as
/// literals in the window.
/// </summary>
public static class SplitWorkspace
{
    /// <summary>The narrowest the Arena page is allowed to get before the split is refused outright.</summary>
    public const double MinPageWidth = 360;

    /// <summary>The narrowest the side pane is allowed to get; below this it folds to one column and cannot usefully shrink further.</summary>
    public const double MinPaneWidth = 340;

    /// <summary>How wide the pane opens the first time, before the user has dragged the splitter.</summary>
    public const double DefaultPaneWidth = 620;

    /// <summary>The pane's own splitter thickness, in device-independent pixels.</summary>
    public const double SplitterWidth = 6;

    /// <summary>
    /// The narrowest window that can show both panes at their floors.
    /// The caller compares against the CONTENT width (the window minus
    /// any left rail), not the window's outer width.
    /// </summary>
    public const double MinSplitContentWidth = MinPageWidth + SplitterWidth + MinPaneWidth;

    /// <summary>Whether two panes fit in this much content width.</summary>
    public static bool CanSplit(double contentWidth) => contentWidth >= MinSplitContentWidth;

    /// <summary>
    /// The pane width to use: the remembered width clamped so the page
    /// keeps at least <see cref="MinPageWidth"/>, and never below the
    /// pane's own floor. Returns 0 when the window is too narrow to
    /// split at all, which is the caller's cue to stay single-pane.
    /// </summary>
    public static double ResolvePaneWidth(double contentWidth, double rememberedPaneWidth)
    {
        if (!CanSplit(contentWidth))
            return 0;
        var widest = contentWidth - SplitterWidth - MinPageWidth;
        // A remembered width from a wider window would squeeze the page;
        // one from a narrower window would make the Lab unusable.
        var wanted = rememberedPaneWidth > 0 ? rememberedPaneWidth : DefaultPaneWidth;
        var clamped = Math.Min(wanted, widest);
        return Math.Max(clamped, MinPaneWidth);
    }

    /// <summary>
    /// How the Lab's own three columns are arranged in the pane. The
    /// Lab was drawn for a 1220px window (272 + flexible + 356); a side
    /// pane is routinely half that, where three columns would leave the
    /// editor about a hundred pixels wide.
    /// </summary>
    public enum PaneArrangement
    {
        /// <summary>Library, editor and journal side by side — the original window's shape.</summary>
        ThreeColumns,

        /// <summary>Library and editor side by side on top; the journal stacked underneath.</summary>
        TwoColumns,

        /// <summary>One column chosen by tab: Prompts, Editor or Journal.</summary>
        Tabbed,
    }

    /// <summary>The width below which three columns stop being usable (the Lab window's own 940px minimum).</summary>
    public const double ThreeColumnMinWidth = 940;

    /// <summary>The width below which even two columns are too cramped, so the pane goes tabbed.</summary>
    public const double TwoColumnMinWidth = 600;

    /// <summary>The arrangement for a pane this wide.</summary>
    public static PaneArrangement Arrange(double paneWidth) => paneWidth switch
    {
        >= ThreeColumnMinWidth => PaneArrangement.ThreeColumns,
        >= TwoColumnMinWidth => PaneArrangement.TwoColumns,
        _ => PaneArrangement.Tabbed,
    };
}

/// <summary>
/// Remembers the last arrangement the shell applied, so a window resize
/// that does not change the answer does not rebuild the pane's grid.
/// Re-arranging is cheap but not free — it re-parents the three Lab
/// columns — and a drag-resize fires continuously, so the guard is
/// what keeps that off the resize path.
/// </summary>
public sealed class PaneArrangementTracker
{
    private SplitWorkspace.PaneArrangement? _last;

    /// <summary>The arrangement last reported, or null before the first one.</summary>
    public SplitWorkspace.PaneArrangement? Current => _last;

    /// <summary>
    /// True when this width implies a different arrangement than the one
    /// currently applied (always true the first time), and records it.
    /// </summary>
    public bool NeedsChange(double paneWidth)
    {
        var next = SplitWorkspace.Arrange(paneWidth);
        if (_last == next)
            return false;
        _last = next;
        return true;
    }

    /// <summary>Forget the applied arrangement — the next check always re-applies.</summary>
    public void Reset() => _last = null;
}
