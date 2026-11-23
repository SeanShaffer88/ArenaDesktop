using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.14.0 — the palette grew arguments. Everything here is the pure half:
/// what counts as a verb, what a refusal looks like (a search, not a silent
/// clamp), and the id round-trip the shell relies on.
/// </summary>
public class PaletteArgumentTests
{
    private static AccountProfile Profile(string id, string name) => new() { Id = id, Name = name };

    private static readonly AccountProfile[] Profiles =
    {
        Profile("default", "Default"),
        Profile("work", "Work"),
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zoom")]
    [InlineData("zoom ")]
    [InlineData("settings")]
    [InlineData("mode")]
    [InlineData("no such verb 12")]
    public void PlainSearchesStaySearches(string? text) =>
        Assert.False(PaletteArguments.Parse(text, Profiles).IsArgumented);

    [Fact]
    public void ZoomTakesAPercentageInsideTheStandardRange()
    {
        var query = PaletteArguments.Parse("zoom 125", Profiles);
        Assert.Equal(PaletteArgumentKind.Zoom, query.Kind);
        Assert.Equal("125", query.Argument);
        Assert.Equal("Zoom to 125%", query.Title);
    }

    [Fact]
    public void ZoomAcceptsAPercentSign_AndRejectsWhatThePageCannotDo()
    {
        Assert.Equal("150", PaletteArguments.Parse("zoom 150%", Profiles).Argument);
        // Out of range or not a number: a search, not a clamp the user did not ask for.
        Assert.False(PaletteArguments.Parse("zoom 900", Profiles).IsArgumented);
        Assert.False(PaletteArguments.Parse("zoom 10", Profiles).IsArgumented);
        Assert.False(PaletteArguments.Parse("zoom lots", Profiles).IsArgumented);
    }

    [Fact]
    public void FindKeepsTheTextExactly()
    {
        var query = PaletteArguments.Parse("find the cables behind the desk", Profiles);
        Assert.Equal(PaletteArgumentKind.Find, query.Kind);
        Assert.Equal("the cables behind the desk", query.Argument);
        Assert.Contains("the cables behind the desk", query.Title);
    }

    [Fact]
    public void FindTitlesAreShortenedForTheRow_ButTheArgumentIsNot()
    {
        var longText = new string('x', 90);
        var query = PaletteArguments.Parse($"find {longText}", Profiles);
        Assert.Equal(longText, query.Argument);            // what goes in the find bar
        Assert.True(query.Title.Length < 70);              // what fits in a row
        Assert.Contains("…", query.Title);                 // and it says it was cut
        Assert.DoesNotContain(longText, query.Title);
    }

    [Fact]
    public void ProfileMatchesByNameThenIdThenPrefix()
    {
        Assert.Equal("work", PaletteArguments.Parse("profile Work", Profiles).Argument);
        Assert.Equal("work", PaletteArguments.Parse("profile work", Profiles).Argument);
        Assert.Equal("work", PaletteArguments.Parse("profile Wo", Profiles).Argument);
        Assert.Equal(PaletteArgumentKind.Profile, PaletteArguments.Parse("profile default", Profiles).Kind);
        Assert.False(PaletteArguments.Parse("profile Nope", Profiles).IsArgumented);
        Assert.False(PaletteArguments.Parse("profile Work", null).IsArgumented);
    }

    [Fact]
    public void ModeResolvesThroughTheRegistry_NotACopyOfIt()
    {
        foreach (var mode in ArenaModes.All)
        {
            Assert.Equal(mode.Id, PaletteArguments.Parse($"mode {mode.Id}", Profiles).Argument);
        }
        Assert.Equal(ArenaModes.BattleId, PaletteArguments.Parse("mode batt", Profiles).Argument);
        Assert.False(PaletteArguments.Parse("mode nonsense", Profiles).IsArgumented);
    }

    [Fact]
    public void IdsRoundTrip()
    {
        foreach (var text in new[] { "zoom 125", "find cables", "profile Work", "mode side" })
        {
            var query = PaletteArguments.Parse(text, Profiles);
            Assert.True(query.IsArgumented, text);
            var id = PaletteArguments.ToId(query);
            Assert.StartsWith(PaletteArguments.IdPrefix, id);
            var back = PaletteArguments.FromId(id);
            Assert.NotNull(back);
            Assert.Equal(query.Kind, back!.Kind);
            Assert.Equal(query.Argument, back.Argument);
            Assert.False(string.IsNullOrWhiteSpace(back.Title));
        }
    }

    [Fact]
    public void AnIdThatIsNotOneIsRejected()
    {
        Assert.Null(PaletteArguments.FromId(null));
        Assert.Null(PaletteArguments.FromId("settings"));
        Assert.Null(PaletteArguments.FromId("arg:"));
        Assert.Null(PaletteArguments.FromId("arg:nope:1"));
        Assert.Null(PaletteArguments.FromId("arg:zoom:"));      // no argument
    }

    [Fact]
    public void TheHelpLineNamesEveryVerb()
    {
        var help = PaletteArguments.HelpText();
        foreach (var verb in PaletteArguments.Verbs)
            Assert.Contains(verb, help);
    }

    [Fact]
    public void ThePaletteShowsTheRow_AndLogsTheArgumentKindOnly()
    {
        var window = Source("src/ArenaDesktop/CommandPaletteWindow.xaml.cs");
        Assert.Contains("PaletteArguments.Parse(query, _settings.Profiles)", window);
        Assert.Contains("rows.Insert(0,", window);

        var main = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        Assert.Contains("if (PaletteArguments.FromId(id) is { } argumented)", main);
        Assert.Contains("RunPaletteArgument(argumented);", main);
        Assert.Contains("PaletteArgumentKind.Zoom:", main);
        Assert.Contains("PaletteArgumentKind.Find:", main);
        Assert.Contains("PaletteArgumentKind.Profile:", main);
        Assert.Contains("PaletteArgumentKind.Mode:", main);
        // The search text is the user's, never the log's.
        Assert.Contains("_log.Info($\"Palette argument: {query.Kind}\");", main);
        Assert.DoesNotContain("Palette argument: {query.Kind} ({query.Argument})", main);
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(PaletteArgumentTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArenaDesktop.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "src", "ArenaDesktop")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root from the test assembly.");
    }
}

/// <summary>The <c>[name=default]</c> syntax (v0.14.0): a blank that already has an answer.</summary>
public class PromptBlankDefaultTests
{
    [Fact]
    public void ADefaultedBlankKeepsItsName()
    {
        Assert.Equal(new[] { "topic", "tone" }, PromptBlanks.Extract("Write about [topic] in a [tone=neutral] voice."));
    }

    [Fact]
    public void TheDefaultSpeaksWhenNobodyElseDoes()
    {
        var filled = PromptBlanks.Fill("Write about [topic] in a [tone=neutral] voice.", new Dictionary<string, string>
        {
            ["topic"] = "tides",
        });
        Assert.Equal("Write about tides in a neutral voice.", filled);
    }

    [Fact]
    public void TheUsersValueBeatsTheDefault()
    {
        var filled = PromptBlanks.Fill("[tone=neutral]", new Dictionary<string, string> { ["tone"] = "sarcastic" });
        Assert.Equal("sarcastic", filled);
    }

    [Fact]
    public void AnUnsuppliedDefaultedBlankIsNotReportedAsLeftBehind()
    {
        var values = new Dictionary<string, string> { ["topic"] = "tides" };
        Assert.Empty(PromptBlanks.Unfilled("[topic] [tone=neutral]", values));
        // …but a blank with no default still is.
        var left = PromptBlanks.Unfilled("[topic] [tone=neutral] [audience]", values);
        Assert.Equal(new[] { "audience" }, left);
    }

    [Fact]
    public void DefaultsAreListedOnce_CaseInsensitively()
    {
        var defaults = PromptBlanks.Defaults("[tone=neutral] [Tone=formal] [topic]");
        var only = Assert.Single(defaults);
        Assert.Equal("tone", only.Key);
        Assert.Equal("neutral", only.Value);        // the first spelling wins, like Extract
    }

    [Fact]
    public void PlainBlanksAreUntouched()
    {
        Assert.Empty(PromptBlanks.Defaults("[topic] [tone]"));
        Assert.Equal("[topic]", PromptBlanks.Fill("[topic]", new Dictionary<string, string>()));
        Assert.Equal(new[] { "topic" }, PromptBlanks.Extract("[topic]"));
    }
}
