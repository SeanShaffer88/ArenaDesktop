using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.12.0 — "Save to Battle Lab" from the prompt pad: the one thought
/// that used to end in copy → open the Lab → New prompt → paste. The rules
/// are pinned here rather than discovered in a window: what the prompt is
/// called, which category it lands in, when the same prompt is refused as
/// a duplicate, what happens at the library's cap, and exactly what the
/// pad says afterwards.
/// </summary>
public class PadHandoffTests
{
    private static BattleLabLibrary Library(params BattlePrompt[] prompts) =>
        new(new BattleLabData { Prompts = prompts.ToList() });

    private static BattlePrompt Prompt(string name, string body, string category = "General") => new()
    {
        Id = name.ToLowerInvariant().Replace(' ', '-'),
        Name = name,
        Category = category,
        Body = body,
    };

    // ──────────────────────── naming ────────────────────────

    [Fact]
    public void Name_IsTheFirstNonEmptyLine()
    {
        Assert.Equal("Write a haiku",
            PadHandoff.NameFor("\n\n   Write a haiku   \nabout the sea\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t\n  ")]
    public void Name_FallsBackWhenThereIsNoText(string? text) =>
        Assert.Equal("Prompt from the pad", PadHandoff.NameFor(text));

    [Fact]
    public void Name_IsCutShortButStaysWithinTheLimit()
    {
        var longLine = new string('a', 200);
        var name = PadHandoff.NameFor(longLine);
        Assert.True(name.Length <= PadHandoff.MaxNameLength, $"name was {name.Length} characters");
        Assert.EndsWith("…", name);
        Assert.StartsWith(new string('a', 10), name);
    }

    // ──────────────────────── keeping ────────────────────────

    [Fact]
    public void Keep_AddsThePromptToTheLibrary()
    {
        var library = Library();
        var result = PadHandoff.Keep(library, "  Summarise this article in three bullets.  ");

        Assert.Equal(PadKeepOutcome.Kept, result.Outcome);
        Assert.NotNull(result.Prompt);
        Assert.Single(library.Prompts);
        var kept = library.Prompts[0];
        Assert.Same(kept, result.Prompt);
        Assert.Equal("Summarise this article in three bullets.", kept.Body);
        Assert.Equal("Summarise this article in three bullets.", kept.Name);
        // Its own category, so a handoff is findable later.
        Assert.Equal(PadHandoff.Category, kept.Category);
        Assert.Equal("Prompt pad", kept.Category);
    }

    [Fact]
    public void Keep_IsIdempotentByBody()
    {
        var library = Library();
        var first = PadHandoff.Keep(library, "Compare two models on this prompt.");
        var second = PadHandoff.Keep(library, "  Compare two models on this prompt.\n");

        Assert.Equal(PadKeepOutcome.Kept, first.Outcome);
        Assert.Equal(PadKeepOutcome.AlreadyThere, second.Outcome);
        Assert.Single(library.Prompts);
        // The result names the prompt that is already there.
        Assert.Same(first.Prompt, second.Prompt);
        Assert.Contains("Already in Battle Lab", second.Status);
    }

    [Fact]
    public void Keep_SeesADuplicateThatWasTypedIntoTheLabByHand()
    {
        // The comparison is against the library, not against what the pad
        // remembers: a prompt saved in the Lab by any route still counts.
        var library = Library(Prompt("My prompt", "same text"));
        var result = PadHandoff.Keep(library, "same text");

        Assert.Equal(PadKeepOutcome.AlreadyThere, result.Outcome);
        Assert.Single(library.Prompts);
        Assert.Contains("My prompt", result.Status);
    }

    [Fact]
    public void Keep_RefusesAnEmptyBox()
    {
        var library = Library();
        foreach (var text in new[] { null, "", "   \n\t " })
        {
            var result = PadHandoff.Keep(library, text);
            Assert.Equal(PadKeepOutcome.Empty, result.Outcome);
            Assert.Null(result.Prompt);
        }
        Assert.Empty(library.Prompts);
    }

    [Fact]
    public void Keep_RefusesAtTheLibraryCap()
    {
        var full = Enumerable.Range(0, BattleLabLibrary.MaxPrompts)
            .Select(i => Prompt($"p{i}", $"body {i}"))
            .ToArray();
        var library = Library(full);

        var result = PadHandoff.Keep(library, "one more");
        Assert.Equal(PadKeepOutcome.LibraryFull, result.Outcome);
        Assert.Equal(BattleLabLibrary.MaxPrompts, library.Prompts.Count);
        Assert.Contains("full", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BattleLabLibrary.MaxPrompts.ToString(), result.Status);
    }

    [Fact]
    public void Keep_NamesAClashUniquely()
    {
        var library = Library(Prompt("Same opening line", "a different body"), Prompt("Same opening line 2", "another"));
        var result = PadHandoff.Keep(library, "Same opening line\nwith a body nobody has");

        Assert.Equal(PadKeepOutcome.Kept, result.Outcome);
        Assert.Equal("Same opening line 3", result.Prompt!.Name);
        Assert.Equal(3, library.Prompts.Count);
    }

    [Fact]
    public void UniqueName_IsCaseInsensitiveAndSkipsTakenSuffixes()
    {
        var library = Library(Prompt("Thing", "a"), Prompt("thing 2", "b"));
        Assert.Equal("Thing 3", PadHandoff.UniqueName(library, "Thing"));
        Assert.Equal("Other", PadHandoff.UniqueName(library, "Other"));
    }

    // ──────────────────────── what it says ────────────────────────

    [Fact]
    public void Status_SaysWhereItWent_AndFitsTheStatusLine()
    {
        var library = Library();
        var result = PadHandoff.Keep(library, "Write a limerick about deadlines.");
        Assert.Equal("Saved to Battle Lab · Write a limerick about deadlines.", result.Status);
        Assert.True(result.Status.Length <= 140);
    }

    [Fact]
    public void EveryStatusFitsTheStatusLine()
    {
        var full = Enumerable.Range(0, BattleLabLibrary.MaxPrompts)
            .Select(i => Prompt($"p{i}", $"body {i}")).ToArray();
        var statuses = new[]
        {
            PadHandoff.Keep(Library(), "a prompt").Status,
            PadHandoff.Keep(Library(), "a prompt").Status,
            PadHandoff.Keep(Library(), "").Status,
            PadHandoff.Keep(Library(full), "one more").Status,
            PadHandoff.Keep(Library(Prompt("kept", "same")), "same").Status,
        };
        Assert.All(statuses, s => Assert.True(s.Length <= 140, $"'{s}' is {s.Length} characters"));
    }

    [Fact]
    public void Keep_NeverWritesThePromptTextIntoTheStatusOfAFailure()
    {
        // The empty case has no text to leak; the cap case must not echo
        // the prompt either — a status line can end up in a screenshot or
        // a bug report.
        var full = Enumerable.Range(0, BattleLabLibrary.MaxPrompts)
            .Select(i => Prompt($"p{i}", $"body {i}")).ToArray();
        var secret = "a private thing I was writing";
        var result = PadHandoff.Keep(Library(full), secret);
        Assert.DoesNotContain(secret, result.Status);
    }

    // ──────────────────────── it is really saved ────────────────────────

    [Fact]
    public void Keep_PersistsThroughTheLibraryStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arenadesktop-pad-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "battle-lab.json");
            var library = BattleLabLibrary.Open(path);
            var result = PadHandoff.Keep(library, "A prompt worth keeping");
            Assert.Equal(PadKeepOutcome.Kept, result.Outcome);

            // A fresh load — the next launch — sees it.
            var reloaded = BattleLabLibrary.Open(path);
            var saved = Assert.Single(reloaded.Prompts.Where(p => p.Body == "A prompt worth keeping"));
            Assert.Equal(PadHandoff.Category, saved.Category);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Keep_DoesNotTouchThePadItself()
    {
        // The pad's file is its own business: keeping a prompt is a library
        // write, and the pad's draft/recents are unchanged by it.
        var data = PromptPad.SetDraft(new PromptPadData(), "an unsent prompt");
        var library = Library();
        PadHandoff.Keep(library, data.Draft);
        Assert.Single(library.Prompts);
        Assert.Empty(data.Recents);
        Assert.Equal("an unsent prompt", data.Draft);
    }

    // ──────────────────────── the shell contract ────────────────────────

    [Fact]
    public void Pad_HasTheButtonAndTheKey()
    {
        var xaml = Source("src/ArenaDesktop/PromptPadWindow.xaml");
        Assert.Contains("x:Name=\"KeepButton\"", xaml);
        Assert.Contains("Click=\"OnKeep\"", xaml);
        Assert.Contains("Save to Battle Lab", xaml);
        Assert.Contains("Ctrl+S", xaml);
        var window = Source("src/ArenaDesktop/PromptPadWindow.xaml.cs");
        Assert.Contains("e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control", window);
        Assert.Contains("_onKeep(Editor.Text", window);
        // The box keeps its text: keeping is not copying, so the draft is
        // not cleared by it.
        Assert.Contains("SetHint(result.Status)", window);
    }

    [Fact]
    public void TheLibraryHasOneWriter()
    {
        var shell = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        // The pad is handed the main window's answer, not a library.
        Assert.Contains("new PromptPadWindow(_log, OnPromptPadCopied, PadKeepPrompt)", shell);
        // Live pane first, store second — never two in-memory copies.
        Assert.Contains("PadHandoff.Keep(lab.Library, text)", shell);
        Assert.Contains("PadHandoff.Keep(BattleLabLibrary.Open(AppPaths.BattleLabPath), text)", shell);
        Assert.Contains("lab.RefreshFromLibrary()", shell);
        // And the prompt's text is never logged.
        Assert.Contains("Prompt pad: keep in Battle Lab → {result.Outcome}", shell);
        Assert.DoesNotContain("keep in Battle Lab → {result.Outcome}: {text}", shell);
        var view = Source("src/ArenaDesktop/BattleLabView.xaml.cs");
        Assert.Contains("public BattleLabLibrary Library => _library;", view);
        Assert.Contains("public void RefreshFromLibrary()", view);
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(PadHandoffTests).Assembly.Location)!);
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
