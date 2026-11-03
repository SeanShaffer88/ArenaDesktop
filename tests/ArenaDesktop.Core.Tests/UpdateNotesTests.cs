using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.3.0: the update window shows what's new — the release body
/// rides the releases-list payload the update check already fetches
/// (no second call), and Core formats the markdown body into a
/// capped, window-friendly plain-text summary.
/// </summary>
public class UpdateNotesTests
{
    // ──────────────────── body rides the existing fetch ────────────────────

    [Fact]
    public void ParseReleases_ReadsBodyIntoNotes()
    {
        const string json = """
            [
              { "tag_name": "v0.3.0", "prerelease": false,
                "html_url": "https://github.com/x/releases/v0.3.0",
                "body": "## What changed\n\n- **Big** thing", "assets": [] },
              { "tag_name": "v0.2.0", "prerelease": false,
                "html_url": "https://github.com/x/releases/v0.2.0", "assets": [] },
              { "tag_name": "v0.1.0", "prerelease": false,
                "html_url": "https://github.com/x/releases/v0.1.0",
                "body": null, "assets": [] }
            ]
            """;
        var releases = ReleaseSelection.ParseReleasesJson(json);
        Assert.Equal(3, releases.Count);
        Assert.Equal("## What changed\n\n- **Big** thing", releases[0].Notes);
        Assert.Equal("", releases[1].Notes); // no body field at all
        Assert.Equal("", releases[2].Notes); // body explicitly null
    }

    [Fact]
    public void SelectedRelease_KeepsItsNotes()
    {
        const string json = """
            [
              { "tag_name": "v0.3.0", "prerelease": false,
                "html_url": "https://github.com/x/releases/v0.3.0",
                "body": "Notes for the new one", "assets": [] },
              { "tag_name": "v0.2.0", "prerelease": false,
                "html_url": "https://github.com/x/releases/v0.2.0",
                "body": "Notes for the old one", "assets": [] }
            ]
            """;
        var picked = ReleaseSelection.SelectNewest(
            ReleaseSelection.ParseReleasesJson(json), new Version(0, 2, 0), UpdateChannel.Stable);
        Assert.Equal("Notes for the new one", picked?.Notes);
    }

    // ──────────────────── the formatter ────────────────────

    [Fact]
    public void Format_StripsHeadingsBoldAndLinks()
    {
        const string md = """
            ## What changed

            - **Battle Lab** — see [the guide](https://example.com/guide) for details
            - A *small* fix and some `code`
            """;
        var text = ReleaseNotesFormatter.Format(md);
        Assert.Equal(
            "What changed\n\n• Battle Lab — see the guide for details\n• A small fix and some code",
            text);
    }

    [Fact]
    public void Format_BulletsBecomeSimpleBullets()
    {
        var text = ReleaseNotesFormatter.Format("- one\n* two\n+ three\n1. four");
        Assert.Equal("• one\n• two\n• three\n1. four", text);
    }

    [Fact]
    public void Format_PlainTextPassesThrough()
    {
        Assert.Equal("Just a sentence.", ReleaseNotesFormatter.Format("Just a sentence."));
    }

    [Fact]
    public void Format_DropsRulesAndCollapsesBlankRuns()
    {
        var text = ReleaseNotesFormatter.Format("First\n\n\n\n---\n\nSecond");
        Assert.Equal("First\n\nSecond", text);
    }

    [Fact]
    public void Format_CapsLongNotesWithTheTail()
    {
        var line = "- A reasonably long release-note line that keeps going with more detail";
        var md = string.Join("\n", Enumerable.Repeat(line, 60));
        var text = ReleaseNotesFormatter.Format(md);
        Assert.True(text.Length <= ReleaseNotesFormatter.MaxLength, $"length was {text.Length}");
        Assert.EndsWith(ReleaseNotesFormatter.MoreTail, text);
        Assert.Contains("A reasonably long release-note line", text);
    }

    [Fact]
    public void Format_ShortNotesAreNotCapped()
    {
        var text = ReleaseNotesFormatter.Format("## Small\n\n- One thing");
        Assert.DoesNotContain(ReleaseNotesFormatter.MoreTail, text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void Format_EmptyReadsAsNoNotes(string? md) =>
        Assert.Equal("", ReleaseNotesFormatter.Format(md));

    [Fact]
    public void Format_MalformedMarkdownNeverThrows()
    {
        // Unclosed emphasis, a dangling link, stray brackets: the
        // formatter must hand back readable text, not an exception.
        var text = ReleaseNotesFormatter.Format("**unclosed and [broken]( and ]] stray [[");
        Assert.NotNull(text);
        Assert.Contains("unclosed", text);
    }
}
