using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// The side pane (v0.8.0): Battle Lab beside the Arena page instead of
/// in a window of its own. The arithmetic is the part that decides
/// whether the user sees two usable panes or two slivers, so it is
/// pinned here rather than left to literals in the window.
/// </summary>
public class SplitWorkspaceTests
{
    // ─────────────────────────── can we split at all ───────────────────────────

    [Fact]
    public void Split_IsRefusedBelowBothFloors()
    {
        // page 360 + splitter 6 + pane 340
        Assert.Equal(706, SplitWorkspace.MinSplitContentWidth);
        Assert.False(SplitWorkspace.CanSplit(SplitWorkspace.MinSplitContentWidth - 1));
        Assert.True(SplitWorkspace.CanSplit(SplitWorkspace.MinSplitContentWidth));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    [InlineData(705)]
    public void NarrowWindow_FallsBackToAZeroWidthPaneRatherThanSqueezing(double width)
    {
        // 0 is the caller's signal to stay single-pane. A split here
        // would give the Arena page less than it needs to be usable.
        Assert.Equal(0, SplitWorkspace.ResolvePaneWidth(width, SplitWorkspace.DefaultPaneWidth));
    }

    [Fact]
    public void WideWindow_OpensThePaneAtItsDefault()
    {
        // 1280 as shipped, minus the rail in Left Rail layout: plenty.
        var pane = SplitWorkspace.ResolvePaneWidth(1280, rememberedPaneWidth: 0);
        Assert.Equal(SplitWorkspace.DefaultPaneWidth, pane);
    }

    [Fact]
    public void RememberedWidth_IsClampedSoThePageKeepsItsFloor()
    {
        // Someone dragged the splitter far left in a maximised window,
        // then shrank the window: the page must not be squeezed below
        // its floor to honour the remembered width.
        const double contentWidth = 900;
        var pane = SplitWorkspace.ResolvePaneWidth(contentWidth, rememberedPaneWidth: 820);
        Assert.Equal(contentWidth - SplitWorkspace.SplitterWidth - SplitWorkspace.MinPageWidth, pane);
        Assert.True(contentWidth - pane - SplitWorkspace.SplitterWidth >= SplitWorkspace.MinPageWidth);
    }

    [Fact]
    public void RememberedWidth_IsRaisedToThePanesFloor()
    {
        // ...and someone who dragged it to nothing still gets a Lab
        // they can read, not a two-pixel strip.
        var pane = SplitWorkspace.ResolvePaneWidth(1280, rememberedPaneWidth: 12);
        Assert.Equal(SplitWorkspace.MinPaneWidth, pane);
    }

    [Fact]
    public void PaneWidth_AlwaysLeavesThePageItsFloor_AcrossTheRange()
    {
        // The invariant that matters, over every width the shell might
        // hand over, including the awkward ones around the threshold.
        for (var width = 700.0; width <= 2000; width += 7)
        {
            var pane = SplitWorkspace.ResolvePaneWidth(width, rememberedPaneWidth: 900);
            if (pane == 0)
            {
                Assert.False(SplitWorkspace.CanSplit(width));
                continue;
            }
            Assert.True(pane >= SplitWorkspace.MinPaneWidth, $"{width}: pane {pane} below its floor");
            Assert.True(width - pane - SplitWorkspace.SplitterWidth >= SplitWorkspace.MinPageWidth,
                $"{width}: page squeezed to {width - pane - SplitWorkspace.SplitterWidth}");
        }
    }

    // ─────────────────────────── the Lab's own columns ───────────────────────────

    [Fact]
    public void WidePane_KeepsTheLabsOriginalThreeColumns()
    {
        Assert.Equal(SplitWorkspace.PaneArrangement.ThreeColumns, SplitWorkspace.Arrange(940));
        Assert.Equal(SplitWorkspace.PaneArrangement.ThreeColumns, SplitWorkspace.Arrange(1220));
    }

    [Fact]
    public void CrampedPane_FoldsToOneTabbedColumn()
    {
        Assert.Equal(SplitWorkspace.PaneArrangement.Tabbed, SplitWorkspace.Arrange(SplitWorkspace.TwoColumnMinWidth - 1));
        Assert.Equal(SplitWorkspace.PaneArrangement.Tabbed, SplitWorkspace.Arrange(SplitWorkspace.MinPaneWidth));
        // A zero width is what "cannot split" hands back; it must not
        // read as a usable arrangement.
        Assert.Equal(SplitWorkspace.PaneArrangement.Tabbed, SplitWorkspace.Arrange(0));
    }

    [Fact]
    public void ArrangementBoundaries_AreExact()
    {
        Assert.Equal(SplitWorkspace.PaneArrangement.TwoColumns, SplitWorkspace.Arrange(939));
        Assert.Equal(SplitWorkspace.PaneArrangement.ThreeColumns, SplitWorkspace.Arrange(940));
        Assert.Equal(SplitWorkspace.PaneArrangement.Tabbed, SplitWorkspace.Arrange(599));
        Assert.Equal(SplitWorkspace.PaneArrangement.TwoColumns, SplitWorkspace.Arrange(600));
    }

    [Fact]
    public void HalfAWindow_FoldsToTwoColumns()
    {
        // The default pane width: the three-column window shape would
        // leave the editor ~150px here — this is the whole reason the
        // pane reflows instead of reusing the window's layout.
        Assert.Equal(SplitWorkspace.PaneArrangement.TwoColumns,
            SplitWorkspace.Arrange(SplitWorkspace.DefaultPaneWidth));
        Assert.Equal(SplitWorkspace.PaneArrangement.TwoColumns,
            SplitWorkspace.Arrange(SplitWorkspace.ResolvePaneWidth(1280, 0)));
    }

    // ─────────────────────── resize does not thrash the grid ───────────────────────

    [Fact]
    public void Tracker_ReportsAChangeOnlyWhenTheArrangementReallyChanges()
    {
        var tracker = new PaneArrangementTracker();
        Assert.Null(tracker.Current);

        // First check always applies.
        Assert.True(tracker.NeedsChange(500));
        Assert.Equal(SplitWorkspace.PaneArrangement.Tabbed, tracker.Current);

        // A drag-resize inside the same band is not a layout change —
        // this is what keeps re-parenting off every resize tick.
        for (var w = 500.0; w < SplitWorkspace.TwoColumnMinWidth; w += 5)
            Assert.False(tracker.NeedsChange(w));

        Assert.True(tracker.NeedsChange(SplitWorkspace.TwoColumnMinWidth));
        Assert.False(tracker.NeedsChange(SplitWorkspace.TwoColumnMinWidth + 10));
        Assert.True(tracker.NeedsChange(SplitWorkspace.ThreeColumnMinWidth));

        // Reset forgets, so the next width re-applies even at the same band.
        tracker.Reset();
        Assert.Null(tracker.Current);
        Assert.True(tracker.NeedsChange(SplitWorkspace.ThreeColumnMinWidth));
    }
}
