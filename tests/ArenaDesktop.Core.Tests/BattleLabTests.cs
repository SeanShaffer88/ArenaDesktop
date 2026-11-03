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

    // ─────────────────────── duplicate (v0.3.0) ───────────────────────

    [Fact]
    public void Duplicate_CopiesBodyAndCategory_WithCopyName()
    {
        var library = EmptyLibrary();
        var original = library.AddPrompt("Gauntlet", "Coding", "Body with [blank]");
        var copy = library.DuplicatePrompt(original.Id);
        Assert.NotNull(copy);
        Assert.NotEqual(original.Id, copy!.Id);
        Assert.Equal("Gauntlet copy", copy.Name);
        Assert.Equal("Coding", copy.Category);
        Assert.Equal("Body with [blank]", copy.Body);
        Assert.Equal(2, library.Prompts.Count);
    }

    [Fact]
    public void Duplicate_NameIncrements_CopyThenCopy2()
    {
        var library = EmptyLibrary();
        var original = AddSample(library, "Gauntlet");
        var first = library.DuplicatePrompt(original.Id);
        var second = library.DuplicatePrompt(original.Id);
        Assert.Equal("Gauntlet copy", first!.Name);
        Assert.Equal("Gauntlet copy 2", second!.Name);
    }

    [Fact]
    public void Duplicate_UnknownIdAndAtCap_ReturnNull()
    {
        var library = EmptyLibrary();
        Assert.Null(library.DuplicatePrompt("no-such-id"));
        var original = AddSample(library);
        for (var i = library.Prompts.Count; i < BattleLabLibrary.MaxPrompts; i++)
            library.AddPrompt($"Filler {i}", "Bulk", "body");
        Assert.Equal(BattleLabLibrary.MaxPrompts, library.Prompts.Count);
        Assert.Null(library.DuplicatePrompt(original.Id));
    }

    // ─────────────────── library transfer (v0.3.0) ───────────────────

    [Fact]
    public void Transfer_ExportImport_RoundTrip_IntoEmptyLibrary()
    {
        var source = EmptyLibrary();
        var a = source.AddPrompt("Alpha", "Coding", "Body A with [blank]");
        var b = source.AddPrompt("Beta", "Writing", "Body B");
        var json = source.ExportPromptsJson();
        Assert.Contains("arenadesktop-battle-lab", json);

        var target = EmptyLibrary();
        var result = target.ImportPrompts(json);
        Assert.True(result.Ok);
        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(new[] { "Alpha", "Beta" }, target.Prompts.Select(p => p.Name));
        Assert.Equal("Coding", target.Prompts[0].Category);
        Assert.Equal("Body A with [blank]", target.Prompts[0].Body);
        // Imported prompts carry new ids — the file's ids never move in.
        Assert.NotEqual(a.Id, target.Prompts[0].Id);
        Assert.NotEqual(b.Id, target.Prompts[1].Id);
    }

    [Fact]
    public void Transfer_Import_SkipsDuplicates_NameCaseInsensitive()
    {
        var library = EmptyLibrary();
        library.AddPrompt("Alpha", "Coding", "Same body");
        var json = """
            { "format": "arenadesktop-battle-lab", "version": 1, "prompts": [
              { "id": "x1", "name": "ALPHA", "category": "Other", "body": "Same body" },
              { "id": "x2", "name": "Alpha", "category": "Coding", "body": "Different body" },
              { "id": "x3", "name": "Fresh", "category": "Writing", "body": "New body" } ] }
            """;
        var result = library.ImportPrompts(json);
        Assert.True(result.Ok);
        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(3, library.Prompts.Count);
    }

    [Fact]
    public void Transfer_ImportTwice_SecondImportAddsNothing()
    {
        var source = EmptyLibrary();
        source.AddPrompt("Alpha", "Coding", "Body A");
        var json = source.ExportPromptsJson();
        var target = EmptyLibrary();
        Assert.Equal(1, target.ImportPrompts(json).Added);
        var second = target.ImportPrompts(json);
        Assert.True(second.Ok);
        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Skipped);
        Assert.Single(target.Prompts);
    }

    [Fact]
    public void Transfer_Import_Malformed_ChangesNothing()
    {
        var library = EmptyLibrary();
        AddSample(library);
        foreach (var bad in new[]
                 {
                     "{ this is not json ]",
                     "",
                     "   ",
                     "[1,2,3]",
                     """{ "format": "something-else", "version": 1, "prompts": [] }""",
                     """{ "format": "arenadesktop-battle-lab", "version": 99, "prompts": [] }""",
                     """{ "format": "arenadesktop-battle-lab", "version": 1 }""",
                 })
        {
            var result = library.ImportPrompts(bad);
            Assert.False(result.Ok, $"Expected refusal for: {bad}");
            Assert.Equal(0, result.Added);
        }
        Assert.False(library.ImportPrompts(null).Ok);
        Assert.Single(library.Prompts);
    }

    // ─────────────────── enqueue category (v0.3.0) ───────────────────

    [Fact]
    public void Queue_EnqueueCategory_AddsMissingInLibraryOrder_CountsAdded()
    {
        var library = EmptyLibrary();
        library.AddPrompt("A", "Coding", "body");
        library.AddPrompt("B", "Writing", "body");
        var c = library.AddPrompt("C", "Coding", "body");
        library.Enqueue(c.Id);
        var added = library.EnqueueCategory("coding");
        Assert.Equal(1, added);
        Assert.Equal(new[] { "C", "A" }, library.QueuePrompts.Select(p => p.Name));
        Assert.Equal(0, library.EnqueueCategory("Coding"));
        Assert.Equal(0, library.EnqueueCategory("No such category"));
    }

    [Fact]
    public void Queue_EnqueueCategory_BlankCategoryReadsGeneral()
    {
        var library = EmptyLibrary();
        library.AddPrompt("Loose", "", "body");
        library.AddPrompt("Filed", "General", "body");
        library.AddPrompt("Other", "Coding", "body");
        Assert.Equal(2, library.EnqueueCategory(null));
        Assert.Equal(new[] { "Loose", "Filed" }, library.QueuePrompts.Select(p => p.Name));
    }

    // ────────────────────── journal tally (v0.3.0) ──────────────────────

    [Fact]
    public void Tally_CountsOutcomes_AndScoreboardLine()
    {
        var t0 = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            new BattleJournalEntry { PromptName = "A", Outcome = BattleOutcome.AWins, RecordedUtc = t0 },
            new BattleJournalEntry { PromptName = "B", Outcome = BattleOutcome.BWins, RecordedUtc = t0 },
            new BattleJournalEntry { PromptName = "C", Outcome = BattleOutcome.Tie, RecordedUtc = t0 },
            new BattleJournalEntry { PromptName = "D", Outcome = BattleOutcome.Tie, RecordedUtc = t0 },
        };
        var tally = BattleJournalTally.Of(entries);
        Assert.Equal(4, tally.Total);
        Assert.Equal(1, tally.AWins);
        Assert.Equal(1, tally.BWins);
        Assert.Equal(2, tally.Ties);
        Assert.Equal("4 battles · A won 1 · B won 1 · Ties 2", tally.ScoreboardLine);

        var empty = BattleJournalTally.Of(Array.Empty<BattleJournalEntry>());
        Assert.Equal(0, empty.Total);
        Assert.Equal("0 battles · A won 0 · B won 0 · Ties 0", empty.ScoreboardLine);
    }

    [Fact]
    public void Tally_FollowsTheJournalFilter()
    {
        var library = EmptyLibrary();
        var t0 = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        library.AddJournalEntry("Alpha", BattleOutcome.AWins, "", t0);
        library.AddJournalEntry("Beta", BattleOutcome.BWins, "", t0.AddHours(1));
        library.AddJournalEntry("Alpha two", BattleOutcome.Tie, "", t0.AddHours(2));
        var all = library.Tally();
        Assert.Equal(3, all.Total);
        var filtered = library.Tally("alpha");
        Assert.Equal(2, filtered.Total);
        Assert.Equal(1, filtered.AWins);
        Assert.Equal(1, filtered.Ties);
    }

    [Fact]
    public void JournalMarkdown_RowsScoreboardAndLabels()
    {
        var t0 = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            new BattleJournalEntry { PromptName = "Alpha", Outcome = BattleOutcome.AWins, Note = "clean win", RecordedUtc = t0 },
            new BattleJournalEntry { PromptName = "Beta", Outcome = BattleOutcome.Tie, Note = "", RecordedUtc = t0.AddHours(1) },
        };
        var markdown = BattleJournalMarkdown.RenderJournalMarkdown(entries, BattleJournalTally.Of(entries));
        Assert.Contains("# Battle Lab journal", markdown);
        Assert.Contains("2 battles · A won 1 · B won 0 · Ties 1", markdown);
        Assert.Contains("Alpha", markdown);
        Assert.Contains("A won", markdown);
        Assert.Contains("clean win", markdown);
        Assert.EndsWith("\n", markdown);
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
