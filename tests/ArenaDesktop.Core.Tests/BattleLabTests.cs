using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>Battle Lab (v0.2.0): prompt blanks, the library, the run queue, the journal, and the store.</summary>
public class BattleLabTests
{
    private static BattleLabLibrary EmptyLibrary() => new(new BattleLabData());

    private static BattlePrompt AddSample(BattleLabLibrary library, string name = "Sample prompt") =>
        library.AddPrompt(name, "Testing", "Body of " + name);

    // ─────────────────────────── blanks ───────────────────────────

    [Fact]
    public void Blanks_Extract_OrderedAndDeduped()
    {
        var blanks = PromptBlanks.Extract("Write about [topic] at [difficulty] level, then revisit [topic] and [Topic].");
        Assert.Equal(new[] { "topic", "difficulty" }, blanks);
    }

    [Fact]
    public void Blanks_Extract_IgnoresEmptyAndUnclosed()
    {
        Assert.Empty(PromptBlanks.Extract("Nothing here."));
        Assert.Empty(PromptBlanks.Extract("Empty [] and [   ] and an unclosed [topic"));
        Assert.Empty(PromptBlanks.Extract(null));
        Assert.Empty(PromptBlanks.Extract(""));
    }

    [Fact]
    public void Blanks_Extract_TrimsLabels()
    {
        var blanks = PromptBlanks.Extract("A [  spaced label ] here");
        Assert.Equal(new[] { "spaced label" }, blanks);
    }

    [Fact]
    public void Blanks_Fill_ReplacesEveryOccurrence_CaseInsensitiveKeys()
    {
        var filled = PromptBlanks.Fill("About [topic], more [topic], plus [other].",
            new Dictionary<string, string> { ["TOPIC"] = "trains", ["other"] = "steam" });
        Assert.Equal("About trains, more trains, plus steam.", filled);
    }

    [Fact]
    public void Blanks_Fill_LeavesUnknownBlanksAsWritten()
    {
        var filled = PromptBlanks.Fill("About [topic] and [missing].",
            new Dictionary<string, string> { ["topic"] = "trains" });
        Assert.Equal("About trains and [missing].", filled);
        Assert.Equal("About [topic].", PromptBlanks.Fill("About [topic].", new Dictionary<string, string>()));
        Assert.Equal("", PromptBlanks.Fill(null, new Dictionary<string, string> { ["a"] = "b" }));
    }

    // ─────────────────────────── library ───────────────────────────

    [Fact]
    public void Library_AddFindUpdateDelete()
    {
        var library = EmptyLibrary();
        var prompt = AddSample(library);
        Assert.False(string.IsNullOrWhiteSpace(prompt.Id));
        Assert.Same(prompt, library.FindPrompt(prompt.Id));

        Assert.True(library.UpdatePrompt(prompt.Id, "Renamed", "Coding", "New body"));
        Assert.Equal("Renamed", library.FindPrompt(prompt.Id)!.Name);
        Assert.Equal("Coding", library.FindPrompt(prompt.Id)!.Category);
        Assert.False(library.UpdatePrompt("no-such-id", "x", "y", "z"));

        Assert.True(library.DeletePrompt(prompt.Id));
        Assert.Null(library.FindPrompt(prompt.Id));
        Assert.False(library.DeletePrompt(prompt.Id));
    }

    [Fact]
    public void Library_Add_GeneratesUniqueIds()
    {
        var library = EmptyLibrary();
        var first = library.AddPrompt("Same Name", "A", "one");
        var second = library.AddPrompt("Same Name", "A", "two");
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, library.Prompts.Count);
    }

    [Fact]
    public void Library_Search_MatchesNameCategoryAndBody()
    {
        var library = EmptyLibrary();
        library.AddPrompt("Alpha", "Coding", "plain body");
        library.AddPrompt("Beta", "Writing", "has a needle inside");
        library.AddPrompt("Gamma", "Needlework", "plain body");

        Assert.Equal(2, library.SearchPrompts("needle").Count);
        Assert.Equal(3, library.SearchPrompts("  ").Count);
        Assert.Single(library.SearchPrompts("alpha"));
        Assert.Empty(library.SearchPrompts("zzz"));
    }

    [Fact]
    public void Library_Categories_DistinctSorted_BlankReadsGeneral()
    {
        var library = EmptyLibrary();
        library.AddPrompt("One", "writing", "b");
        library.AddPrompt("Two", "Coding", "b");
        library.AddPrompt("Three", "", "b");
        library.AddPrompt("Four", "Writing", "b");
        Assert.Equal(new[] { "Coding", "General", "writing" }, library.Categories());
    }

    [Fact]
    public void Library_DeletePrompt_RemovesItsQueueEntries_KeepsJournalSnapshot()
    {
        var library = EmptyLibrary();
        var prompt = AddSample(library);
        library.Enqueue(prompt.Id);
        library.AddJournalEntry(prompt.Name, BattleOutcome.AWins, "note");
        Assert.True(library.DeletePrompt(prompt.Id));
        Assert.Empty(library.QueuePrompts);
        var entry = Assert.Single(library.JournalEntries());
        Assert.Equal(prompt.Name, entry.PromptName);
    }

    // ─────────────────────────── run queue ───────────────────────────

    [Fact]
    public void Queue_Ordering_AddMoveRemoveClear()
    {
        var library = EmptyLibrary();
        var a = AddSample(library, "A");
        var b = AddSample(library, "B");
        var c = AddSample(library, "C");
        Assert.True(library.Enqueue(a.Id));
        Assert.True(library.Enqueue(b.Id));
        Assert.True(library.Enqueue(c.Id));
        Assert.False(library.Enqueue("no-such-id"));
        Assert.Equal(new[] { "A", "B", "C" }, library.QueuePrompts.Select(p => p.Name));
        Assert.Equal("A", library.NextUp!.Name);

        Assert.True(library.MoveInQueue(2, -1));
        Assert.Equal(new[] { "A", "C", "B" }, library.QueuePrompts.Select(p => p.Name));
        Assert.False(library.MoveInQueue(0, -1));
        Assert.False(library.MoveInQueue(2, +1));
        Assert.True(library.MoveInQueue(0, +1));
        Assert.Equal(new[] { "C", "A", "B" }, library.QueuePrompts.Select(p => p.Name));

        Assert.True(library.RemoveFromQueueAt(1));
        Assert.Equal(new[] { "C", "B" }, library.QueuePrompts.Select(p => p.Name));
        Assert.False(library.RemoveFromQueueAt(5));

        library.ClearQueue();
        Assert.Empty(library.QueuePrompts);
        Assert.Null(library.NextUp);
    }

    [Fact]
    public void Queue_AllowsTheSamePromptTwice()
    {
        var library = EmptyLibrary();
        var a = AddSample(library);
        library.Enqueue(a.Id);
        library.Enqueue(a.Id);
        Assert.Equal(2, library.QueuePrompts.Count);
    }

    // ─────────────────────────── journal ───────────────────────────

    [Fact]
    public void Journal_AddNewestFirst_Filter_Delete()
    {
        var library = EmptyLibrary();
        var t0 = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        library.AddJournalEntry("Alpha", BattleOutcome.AWins, "first", t0);
        var second = library.AddJournalEntry("Beta", BattleOutcome.BWins, "second", t0.AddHours(1));
        library.AddJournalEntry("Alpha two", BattleOutcome.Tie, "third", t0.AddHours(2));

        var all = library.JournalEntries();
        Assert.Equal(new[] { "Alpha two", "Beta", "Alpha" }, all.Select(e => e.PromptName));
        Assert.Equal(new[] { "Alpha two", "Alpha" }, library.JournalEntries("alpha").Select(e => e.PromptName));
        Assert.Equal("B won", BattleOutcomeText.Label(second.Outcome));

        Assert.True(library.DeleteJournalEntry(second.Id));
        Assert.Equal(2, library.JournalEntries().Count);
        Assert.False(library.DeleteJournalEntry(second.Id));
    }

    // ─────────────────────────── store ───────────────────────────

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arenadesktop-battlelab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "battle-lab.json");
    }

    [Fact]
    public void Store_RoundTrip_PreservesEverything()
    {
        var path = TempFile();
        var library = BattleLabLibrary.Open(path);
        // Start the round-trip from an empty library: the first-run
        // seeds are deleted first (which also proves they stay gone).
        foreach (var seeded in library.Prompts.ToList())
            library.DeletePrompt(seeded.Id);
        var prompt = library.AddPrompt("Round trip", "Coding", "Body with [blank]");
        library.Enqueue(prompt.Id);
        library.AddJournalEntry("Round trip", BattleOutcome.Tie, "a note",
            new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero));

        var reopened = BattleLabLibrary.Open(path);
        var back = Assert.Single(reopened.Prompts);
        Assert.Equal("Round trip", back.Name);
        Assert.Equal("Body with [blank]", back.Body);
        Assert.Equal(back.Id, reopened.NextUp!.Id);
        var entry = Assert.Single(reopened.JournalEntries());
        Assert.Equal("a note", entry.Note);
        Assert.Equal(BattleOutcome.Tie, entry.Outcome);
    }

    [Fact]
    public void Store_SeedsSamples_OnFirstRunOnly()
    {
        var path = TempFile();
        Assert.False(File.Exists(path));
        var first = BattleLabLibrary.Open(path);
        Assert.Equal(4, first.Prompts.Count);
        Assert.Contains(first.Prompts, p => p.Id == "code-review-gauntlet");
        var math = first.Prompts.Single(p => p.Id == "math-word-problem-set");
        Assert.Equal(new[] { "topic", "difficulty" }, PromptBlanks.Extract(math.Body));
        Assert.True(File.Exists(path));

        // Deleting every sample sticks: the seed is a first-run event,
        // not a refill.
        foreach (var prompt in first.Prompts.ToList())
            first.DeletePrompt(prompt.Id);
        var second = BattleLabLibrary.Open(path);
        Assert.Empty(second.Prompts);
    }

    [Fact]
    public void Store_CorruptFile_IsPreserved_AndLoadsEmpty()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ this is not json ]");
        var data = BattleLabStore.Load(path);
        Assert.Empty(data.Prompts);
        Assert.Empty(data.Queue);
        Assert.Empty(data.Journal);
        var preserved = Directory.GetFiles(Path.GetDirectoryName(path)!, "battle-lab.json.corrupt-*");
        Assert.Single(preserved);
        Assert.Equal("{ this is not json ]", File.ReadAllText(preserved[0]));
    }

    [Fact]
    public void Store_Load_DropsQueueIdsNamingDeletedPrompts()
    {
        var path = TempFile();
        File.WriteAllText(path, """
            { "prompts": [ { "id": "keep", "name": "Keep", "category": "C", "body": "B" } ],
              "queue": [ "keep", "ghost" ], "journal": [] }
            """);
        var data = BattleLabStore.Load(path);
        Assert.Equal(new[] { "keep" }, data.Queue);
    }

    // ──────────────────────── launch surface ────────────────────────

    [Fact]
    public void CommandLine_BattleLabFlag_ParsesAndReducesToIntent()
    {
        var options = CommandLine.Parse(new[] { "--battle-lab" });
        Assert.True(options.OpenBattleLab);
        var intent = LaunchIntent.FromCommandLine(options);
        Assert.Equal(LaunchIntentKind.OpenBattleLab, intent.Kind);
        // Round-trips through the pending-intent JSON like every kind.
        Assert.Equal(intent, LaunchIntent.TryFromJson(intent.ToJson()));
    }

    [Fact]
    public void Protocol_BattleLabRoute_Parses()
    {
        var parsed = LaunchIntent.ParseProtocolUri("arenadesktop://battle-lab", Array.Empty<string>());
        Assert.False(parsed.IsRefusal);
        Assert.Equal(LaunchIntentKind.OpenBattleLab, parsed.Intent!.Kind);
        var refused = LaunchIntent.ParseProtocolUri("arenadesktop://battle-lab/extra", Array.Empty<string>());
        Assert.True(refused.IsRefusal);
    }

    [Fact]
    public void Usage_DocumentsBattleLabFlagAndRoute()
    {
        var text = CommandLineUsage.Text("0.2.0");
        Assert.Contains("--battle-lab", text);
        Assert.Contains("arenadesktop://battle-lab", text);
    }
}
