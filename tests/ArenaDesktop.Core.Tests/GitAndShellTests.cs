using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>Completeness slice C: git parsing/service over a fake runner, shell job model.</summary>
public sealed class GitAndShellTests
{
    private sealed class FakeRunner(Func<ProcessSpec, ProcessResult> respond) : IProcessRunner
    {
        public List<ProcessSpec> Specs { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessSpec spec, IProgress<string>? outputLines = null, CancellationToken ct = default)
        {
            Specs.Add(spec);
            return Task.FromResult(respond(spec));
        }
    }

    private static ProcessResult Ok(string stdout) => new(0, stdout, "");

    [Fact]
    public void ParseStatus_ReadsBranchAndEntries()
    {
        var status = GitService.ParseStatus(
            "## main...origin/main\n M src/App.cs\nM  staged.cs\n?? new file.txt\nR  old.cs -> renamed.cs\n");
        Assert.Equal("main", status.Branch);
        Assert.Equal(4, status.Entries.Count);
        Assert.True(status.Entries[0].IsModifiedInWorkTree);
        Assert.True(status.Entries[1].IsStaged);
        Assert.True(status.Entries[2].IsUntracked);
        Assert.Equal("renamed.cs", status.Entries[3].Path);
        Assert.Equal("old.cs", status.Entries[3].OriginalPath);
        Assert.False(status.IsClean);
    }

    [Fact]
    public async Task GitService_BuildsExpectedArguments()
    {
        var runner = new FakeRunner(spec => spec.Arguments[0] switch
        {
            "status" => Ok("## dev\n"),
            "branch" => Ok("* dev\n  main\n"),
            _ => Ok(""),
        });
        var git = new GitService(runner, @"C:\repo");
        var status = await git.StatusAsync();
        Assert.Equal("dev", status.Branch);
        await git.StageAsync(["a.cs", "b.cs"]);
        await git.CommitAsync("  message  ");
        await git.PushAsync();
        var (current, all) = await git.BranchesAsync();
        Assert.Equal("dev", current);
        Assert.Equal(["dev", "main"], all);

        var add = runner.Specs.First(s => s.Arguments[0] == "add");
        Assert.Equal(["add", "--", "a.cs", "b.cs"], add.Arguments);
        var commit = runner.Specs.First(s => s.Arguments[0] == "commit");
        Assert.Equal(["commit", "-m", "message"], commit.Arguments);
        var push = runner.Specs.First(s => s.Arguments[0] == "push");
        Assert.Equal("0", push.Environment!["GIT_TERMINAL_PROMPT"]);
        Assert.All(runner.Specs, s => Assert.Equal("git", s.FileName));
        Assert.All(runner.Specs, s => Assert.Equal(@"C:\repo", s.WorkingDirectory));
    }

    [Fact]
    public async Task GitService_StatusFailureThrowsPlainly()
    {
        var runner = new FakeRunner(_ => new ProcessResult(128, "", "fatal: not a git repository"));
        var git = new GitService(runner, @"C:\nope");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => git.StatusAsync());
        Assert.Contains("not a git repository", ex.Message);
    }

    [Fact]
    public void DiscoverRepositories_FindsFolderAndSubfolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "arena-repos-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "child", ".git"));
        Directory.CreateDirectory(Path.Combine(root, "plain"));
        try
        {
            var found = GitService.DiscoverRepositories(root);
            Assert.Equal(2, found.Count);
            Assert.Contains(root, found);
            Assert.Contains(Path.Combine(root, "child"), found);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ShellJob_TracksLifecycleAndCapsOutput()
    {
        var job = new ShellJob { Id = "1", CommandText = "dotnet build", WorkingFolder = @"C:\repo" };
        Assert.Equal(ShellJobState.Running, job.State);
        for (var i = 0; i < ShellJob.MaxLines + 10; i++) job.AppendLine($"line {i}");
        Assert.True(job.OutputTruncated);
        Assert.Equal(ShellJob.MaxLines, job.Lines.Count);
        job.Finish(1, cancelled: false);
        Assert.Equal(ShellJobState.Failed, job.State);
        Assert.Contains("Failed (exit 1)", job.Summary);
        var spec = ShellJob.SpecFor("dotnet build", @"C:\repo");
        Assert.Equal("cmd.exe", spec.FileName);
        Assert.Equal(["/d", "/c", "dotnet build"], spec.Arguments);
    }
}

/// <summary>Slice D2: library search + export over the app's own stores.</summary>
public sealed class LibraryTests
{
    private static (BattleLabData, PromptPadData, List<DownloadRecord>) Sample() =>
    (
        new BattleLabData
        {
            Prompts = [new BattlePrompt { Name = "Chess coach", Category = "Games", Body = "Explain chess tactics simply" }],
            Journal = [new BattleJournalEntry { PromptName = "Chess coach", Outcome = BattleOutcome.AWins, Note = "clearer tactics" }],
        },
        new PromptPadData { Recents = [new PromptPadEntry { Text = "Write a haiku about rain" }] },
        [new DownloadRecord { FileName = "report.pdf", SizeBytes = 2048 }]
    );

    [Fact]
    public void Search_MatchesAcrossStores_WithPrefixTerms()
    {
        var (lab, pad, downloads) = Sample();
        var index = LibrarySearch.BuildIndex(lab, pad, downloads);
        Assert.Equal(4, index.Count);
        Assert.Equal(2, LibrarySearch.Search(index, "chess").Count); // prompt + its journal entry
        Assert.Single(LibrarySearch.Search(index, "haiku"));
        Assert.Single(LibrarySearch.Search(index, "report"));
        Assert.Equal(2, LibrarySearch.Search(index, "ches tact").Count); // prompt + journal both mention them
        Assert.Empty(LibrarySearch.Search(index, "zzz"));
        Assert.Equal(4, LibrarySearch.Search(index, "").Count);
    }

    [Fact]
    public void Export_JsonAndMarkdownCarryTheStores()
    {
        var (lab, pad, downloads) = Sample();
        var document = LibraryExport.Build(lab, pad, downloads);
        var json = LibraryExport.ToJson(document);
        Assert.Contains("Chess coach", json);
        var markdown = LibraryExport.ToMarkdown(document);
        Assert.Contains("## Battle Lab prompts", markdown);
        Assert.Contains("report.pdf", markdown);
        Assert.Contains("no Arena conversation content", markdown);
    }
}

/// <summary>Slice D3: the notification centre store.</summary>
public sealed class NotificationCentreTests
{
    [Fact]
    public void Store_AddsCapsMarksAndPersists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arena-notif-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "notifications.json");
            var store = NotificationStore.Load(path);
            store.Add(NotificationCategory.Download, "Download finished", "a.pdf");
            store.Add(NotificationCategory.Agent, "Terminal", "Finished: dotnet build");
            Assert.Equal(2, store.UnreadCount);
            Assert.Equal("Terminal", store.Entries[0].Title); // newest first
            store.MarkAllRead();
            Assert.Equal(0, store.UnreadCount);
            var reopened = NotificationStore.Load(path);
            Assert.Equal(2, reopened.Entries.Count);
            Assert.Equal(0, reopened.UnreadCount);
            reopened.Clear();
            Assert.Empty(NotificationStore.Load(path).Entries);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CategoryMuting_IsJudgedInOnePlace()
    {
        Assert.True(NotificationStore.IsCategoryEnabled([], NotificationCategory.Battle));
        Assert.False(NotificationStore.IsCategoryEnabled(["battle"], NotificationCategory.Battle));
        Assert.Equal(["Battle"], NotificationStore.SanitizeMuted(["battle", "BATTLE", "nonsense"]));
    }
}

/// <summary>Slice D5: script commands.</summary>
public sealed class ScriptCommandTests
{
    [Fact]
    public void Sanitize_DropsBlanksAndDuplicateNames()
    {
        var list = ScriptCommandCatalog.Sanitize(
        [
            new ScriptCommand { Name = "Build", Command = "dotnet build" },
            new ScriptCommand { Name = "build", Command = "duplicate name" },
            new ScriptCommand { Name = "Empty", Command = " " },
            new ScriptCommand { Name = "Test", Command = "dotnet test", WorkingFolder = " C:\\x " },
        ]);
        Assert.Equal(2, list.Count);
        Assert.Equal("C:\\x", list[1].WorkingFolder);
    }

    [Fact]
    public void PaletteFamily_GeneratesAndResolves()
    {
        var commands = ScriptCommandCatalog.Sanitize(
            [new ScriptCommand { Name = "Build", Command = "dotnet build" }]);
        var palette = ScriptCommandCatalog.ToPaletteCommands(commands);
        Assert.Single(palette);
        Assert.Equal("Scripts", palette[0].Group);
        Assert.StartsWith("Run: Build — ", palette[0].Title);
        Assert.Equal("Build", ScriptCommandCatalog.ForPaletteId(commands, palette[0].Id)!.Name);
        Assert.Null(ScriptCommandCatalog.ForPaletteId(commands, "mode:battle"));
        // And the palette search finds them through the extras path.
        var found = CommandPalette.Search("build", null, palette);
        Assert.Contains(found, c => c.Id == palette[0].Id);
    }
}

/// <summary>Slice E: profile backup, audit log, permissions transparency.</summary>
public sealed class PrivacyCompletenessTests
{
    [Fact]
    public void ProfileBackup_RoundTrips_AndRejectsWrongPassphrase()
    {
        var settings = new AppSettings();
        settings.Profiles.Add(new AccountProfile { Id = "work", Name = "Work" });
        settings.Sanitized();
        var text = ProfileBackup.Create(settings, "a long passphrase");
        Assert.True(ProfileBackup.IsBackup(text));
        Assert.DoesNotContain("a long passphrase", text);
        Assert.True(ProfileBackup.TryRead(text, "a long passphrase", out var payload, out _));
        Assert.Equal(2, payload!.Profiles.Count);
        Assert.Contains("Work", payload.SettingsJson);
        Assert.False(ProfileBackup.TryRead(text, "wrong passphrase", out _, out var error));
        Assert.Contains("passphrase is wrong", error);
        Assert.False(ProfileBackup.IsBackup("ARENADESKTOP-SETTINGS-ENCRYPTED\n{}"));
    }

    [Fact]
    public void AuditLog_AppendsReadsAndSkipsDamage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arena-audit-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "audit-log.jsonl");
            AuditLog.Record(path, AuditLog.ProfileSwitch, "to profile work");
            AuditLog.Record(path, AuditLog.DataCleared, "Cookies / ActiveProfile");
            File.AppendAllText(path, "{not json\n");
            var recent = AuditLog.ReadRecent(path);
            Assert.Equal(2, recent.Count);
            Assert.Equal(AuditLog.DataCleared, recent[0].Kind); // newest first
            Assert.Equal("to profile work", recent[1].Detail);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void PermissionsTransparency_CoversTheFiveFeatures()
    {
        var features = PermissionsTransparency.Rows.Select(r => r.Feature).ToList();
        Assert.Contains("Camera", features);
        Assert.Contains("Microphone", features);
        Assert.Contains("Notifications", features);
        Assert.Contains("Geolocation", features);
        Assert.Contains("Clipboard", features);
        Assert.All(PermissionsTransparency.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Revoke)));
    }
}
