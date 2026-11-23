using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.14.0 — "copy the next queued prompt" from the ⋯ menu and the palette,
/// without opening Battle Lab. The rule is Core's so the blanks refusal and the
/// position line are pinned instead of discovered in a handler.
/// </summary>
public class QueueRunTests
{
    private static BattleLabLibrary Library(params (string Name, string Body)[] prompts)
    {
        var library = new BattleLabLibrary(new BattleLabData());
        foreach (var (name, body) in prompts)
            library.AddPrompt(name, "General", body);
        return library;
    }

    [Fact]
    public void AnEmptyQueueHasNothingToRun()
    {
        Assert.Null(BattleQueueRunner.Next(Library()));
        Assert.Null(BattleQueueRunner.Next(null!));
    }

    [Fact]
    public void TheHeadOfTheQueueIsThePrompt()
    {
        var library = Library(("First", "Do the first thing."), ("Second", "Do the second thing."));
        library.Enqueue(library.Prompts[0].Id);
        library.Enqueue(library.Prompts[1].Id);

        var run = BattleQueueRunner.Next(library);
        Assert.NotNull(run);
        Assert.Equal("First", run!.Prompt.Name);
        Assert.True(run.Ready);
        Assert.Equal("Do the first thing.", run.Text);
        Assert.Contains("(1 of 2)", run.Status);
        Assert.Contains("First", run.Status);
    }

    [Fact]
    public void DefaultedBlanksAreFilledAndThePromptStillRuns()
    {
        var library = Library(("Toned", "Write about [topic=tides] in a [tone=neutral] voice."));
        library.Enqueue(library.Prompts[0].Id);

        var run = BattleQueueRunner.Next(library);
        Assert.NotNull(run);
        Assert.True(run!.Ready);                                   // no blanks left to ask about
        Assert.Equal("Write about tides in a neutral voice.", run.Text);
    }

    [Fact]
    public void PlainBlanksRefuseTheRun_AndNameThem()
    {
        var library = Library(("Blanky", "Summarise [article] for [audience]."));
        library.Enqueue(library.Prompts[0].Id);

        var run = BattleQueueRunner.Next(library);
        Assert.NotNull(run);
        Assert.False(run!.Ready);
        Assert.Contains("[article]", run.Status);
        Assert.Contains("[audience]", run.Status);
        Assert.Contains("Battle Lab", run.Status);                 // where to go and fill them
        Assert.Contains("Blanky", run.Status);
    }

    [Fact]
    public void RunningDoesNotChangeTheQueue()
    {
        // Peeking is not consuming: the pane's own "Use next" log flow removes
        // the head when a result is recorded, and a second copy from the menu
        // must not race that decision.
        var library = Library(("Only", "One prompt."));
        library.Enqueue(library.Prompts[0].Id);
        BattleQueueRunner.Next(library);
        BattleQueueRunner.Next(library);
        Assert.Single(library.QueuePrompts);
    }

    [Fact]
    public void TheStatusFitsTheStatusBar_AndNeverCarriesMoreThanTheName()
    {
        var library = Library(("Long prompt name here", new string('x', 400)));
        library.Enqueue(library.Prompts[0].Id);
        var run = BattleQueueRunner.Next(library)!;
        Assert.True(run.Status.Length <= 200, $"status was {run.Status.Length} characters");
        Assert.DoesNotContain(new string('x', 40), run.Status);     // never the body
    }

    [Fact]
    public void TheMenuAndThePaletteBothOfferIt()
    {
        var menu = Source("src/ArenaDesktop/MainWindow.xaml");
        Assert.Contains("Click=\"OnBattleNextQueued\"", menu);
        Assert.Contains("Copy the next queued prompt", menu);

        var command = CommandPalette.ById("battle-next-queue");
        Assert.NotNull(command);
        Assert.Equal("Open", command!.Group);        // a real group, so Grouped() shows it
        Assert.Contains(command, CommandPalette.All);

        var shell = Source("src/ArenaDesktop/MainWindow.xaml.cs");
        Assert.Contains("case \"battle-next-queue\": RunNextQueuedPrompt(); break;", shell);
        Assert.Contains("private void OnBattleNextQueued(", shell);
        Assert.Contains("BattleQueueRunner.Next(library)", shell);
        // The log says what happened, never which prompt it was.
        Assert.Contains("Queue run: copied the next prompt, Battle opened", shell);
        Assert.DoesNotContain("Queue run: copied '{run.Prompt.Name}'", shell);
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(QueueRunTests).Assembly.Location)!);
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
