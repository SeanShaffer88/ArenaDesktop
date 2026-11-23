using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.15.0 — the palette's <c>prompt &lt;text&gt;</c> verb. The ranking is the
/// subject: a search that silently prefers one saved prompt over another is
/// how you copy the wrong thing, so each rank is pinned and a tie is a
/// shortlist rather than a guess.
/// </summary>
public class PromptPickTests
{
    private static BattleLabLibrary Library(params (string Name, string Category, string Body)[] prompts)
    {
        var library = new BattleLabLibrary(new BattleLabData());
        foreach (var (name, category, body) in prompts)
            library.AddPrompt(name, category, body);
        return library;
    }

    private static BattleLabLibrary Sample() => Library(
        ("Cable triage", "Ops", "Sort these cables by failure rate."),
        ("Cable triage, long form", "Ops", "A longer pass over the same cable list."),
        ("Cables in the rack", "Hardware", "Label every cable in rack 4."),
        ("Voice check", "Ops", "Rewrite this in a calmer voice."));

    [Fact]
    public void AnExactNameBeatsEveryOtherRank()
    {
        var pick = PromptPicker.Find(Sample(), "Cable triage");
        Assert.Equal(PromptPickKind.One, pick.Kind);
        Assert.Equal("Cable triage", pick.Prompt!.Name);
        Assert.Contains("Copied “Cable triage”", pick.Status);
    }

    [Fact]
    public void TheNameIsRankedBeforeTheBody()
    {
        // "cables" appears in a body ("these cables") and in a name ("Cables in
        // the rack"). The name wins even though it sorts later.
        var pick = PromptPicker.Find(Sample(), "cables");
        Assert.Equal(PromptPickKind.One, pick.Kind);
        Assert.Equal("Cables in the rack", pick.Prompt!.Name);
    }

    [Fact]
    public void ANamePrefixBeatsAContains_AndATieIsAShortlist()
    {
        var pick = PromptPicker.Find(Sample(), "Cable tri");
        Assert.Equal(PromptPickKind.Many, pick.Kind);      // both start with it
        Assert.Equal(2, pick.Matches.Count);
        Assert.Contains("2 saved prompts match", pick.Status);
        Assert.Contains("Battle Lab is filtered", pick.Status);
        Assert.Null(pick.Prompt);                           // nothing was chosen for you

        var single = PromptPicker.Find(Sample(), "Voice");
        Assert.Equal(PromptPickKind.One, single.Kind);
        Assert.Equal("Voice check", single.Prompt!.Name);
    }

    [Fact]
    public void ACategoryNameFindsItsPrompts()
    {
        var pick = PromptPicker.Find(Sample(), "Hardware");
        Assert.Equal(PromptPickKind.One, pick.Kind);
        Assert.Equal("Cables in the rack", pick.Prompt!.Name);
    }

    [Fact]
    public void TheBodyIsTheLastResort()
    {
        var pick = PromptPicker.Find(Sample(), "calmer");
        Assert.Equal(PromptPickKind.One, pick.Kind);
        Assert.Equal("Voice check", pick.Prompt!.Name);
    }

    [Fact]
    public void MatchingIgnoresCase_AndSurroundingSpace()
    {
        Assert.Equal(PromptPickKind.One, PromptPicker.Find(Sample(), "  cAbLe TrIaGe  ").Kind);
    }

    [Fact]
    public void NothingIsSaidPlainly_WithNowhereToGoButBattleLab()
    {
        var pick = PromptPicker.Find(Sample(), "quantum flux");
        Assert.Equal(PromptPickKind.None, pick.Kind);
        Assert.Null(pick.Prompt);
        Assert.Empty(pick.Matches);
        Assert.Contains("No saved prompt matches", pick.Status);
        Assert.Contains("Battle Lab", pick.Status);

        var empty = PromptPicker.Find(Sample(), "   ");
        Assert.Equal(PromptPickKind.None, empty.Kind);
        Assert.Contains("Type a few words", empty.Status);
    }

    [Fact]
    public void AnEmptyOrMissingLibraryIsNotACrash()
    {
        Assert.Equal(PromptPickKind.None, PromptPicker.Find(Library(), "anything").Kind);
        Assert.Equal(PromptPickKind.None, PromptPicker.Find(null, "anything").Kind);
    }

    [Fact]
    public void TheShortlistIsOrderedByName_SoTwoRunsAgree()
    {
        var pick = PromptPicker.Find(Sample(), "Cable");
        Assert.Equal(PromptPickKind.Many, pick.Kind);
        var names = pick.Matches.Select(p => p.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
    }

    [Fact]
    public void TheStatusFitsTheStatusBar()
    {
        var longName = new string('a', 200);
        var pick = PromptPicker.Find(Library((longName, "Ops", "body")), "aaaa");
        Assert.True(pick.Status.Length <= 220, $"status was {pick.Status.Length} characters");
        Assert.DoesNotContain(longName, pick.Status);        // a 200-char name is not quoted whole
    }

    [Fact]
    public void TheVerbParsesAndRoundTrips()
    {
        var query = PaletteArguments.Parse("saved cable triage");
        Assert.Equal(PaletteArgumentKind.Prompt, query.Kind);
        Assert.Equal("cable triage", query.Argument);        // words kept, spaces and all
        Assert.Contains("cable triage", query.Title);

        var id = PaletteArguments.ToId(query);
        var back = PaletteArguments.FromId(id);
        Assert.NotNull(back);
        Assert.Equal(PaletteArgumentKind.Prompt, back!.Kind);
        Assert.Equal("cable triage", back.Argument);

        // A bare verb is still a search, like the other four.
        Assert.False(PaletteArguments.Parse("saved").IsArgumented);

        // And the verb had to avoid "prompt pad": that pair must keep opening
        // the pad rather than asking the library about a prompt called "pad".
        var pad = PaletteArguments.Parse("prompt pad");
        Assert.False(pad.IsArgumented);
        Assert.Contains(CommandPalette.Search("prompt pad"), c => c.Id == "prompt-pad");
    }

    [Fact]
    public void TheShellCopiesOne_AndFiltersOnMany()
    {
        var main = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        Assert.Contains("case PaletteArgumentKind.Prompt:", main);
        Assert.Contains("RunSavedPrompt(query.Argument);", main);
        Assert.Contains("PromptPicker.Find(library, argument)", main);
        // Many: the pane is filtered rather than one prompt being chosen.
        Assert.Contains("OpenBattleLabPane();", main);
        Assert.Contains("_battleLab?.ShowSearch(argument);", main);
        // One: the same blanks-refusing copy path the queue uses.
        Assert.Contains("BattleQueueRunner.For(pick.Prompt!", main);
        Assert.Contains("private void CopyThenOpen(QueueRun run, string logContext, string copiedLine)", main);
        // The words typed are the user's; the log says only what happened.
        Assert.Contains("_log.Info(\"Saved prompt: no match\");", main);
        Assert.DoesNotContain("_log.Info($\"Saved prompt: {argument}", main);

        var view = Source("src/ArenaDesktop/BattleLabView.xaml.cs");
        Assert.Contains("public void ShowSearch(string? text)", view);
        Assert.Contains("SearchText.Text = text ?? \"\";", view);
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(PromptPickTests).Assembly.Location)!);
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
