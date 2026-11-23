using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// v0.9.0 — the prompt pad: a scratchpad for one prompt at a time, which
/// copies to the clipboard and opens the page the user picked. The rules
/// live in Core so they are pinned here rather than discovered in a
/// window: what counts as the same prompt, how many are kept, what a
/// blank box does, and what the status line claims.
/// </summary>
public class PromptPadTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    // ──────────────────────── copyability and comparison ────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   \n\t ", false)]
    [InlineData("a", true)]
    [InlineData("  a prompt worth keeping  ", true)]
    public void IsCopyable_RejectsOnlyEmptyText(string? text, bool expected) =>
        Assert.Equal(expected, PromptPad.IsCopyable(text));

    [Fact]
    public void Normalize_FoldsCaseAndWhitespace()
    {
        Assert.Equal("write a haiku", PromptPad.Normalize("  Write   a\nhaiku  "));
        Assert.Equal("", PromptPad.Normalize(null));
        Assert.Equal("", PromptPad.Normalize("  \t "));
    }

    // ──────────────────────── remembering ────────────────────────

    [Fact]
    public void Remember_PutsTheNewestFirst_AndTrims()
    {
        var data = new PromptPadData();
        PromptPad.Remember(data, "  first  ", Now);
        PromptPad.Remember(data, "second", Now.AddMinutes(1));
        Assert.Equal(new[] { "second", "first" }, data.Recents.Select(e => e.Text));
        Assert.Equal(Now.AddMinutes(1), data.Recents[0].UsedUtc);
    }

    [Fact]
    public void Remember_MovesAnIdenticalPromptToTheTop_InsteadOfDuplicating()
    {
        var data = new PromptPadData();
        PromptPad.Remember(data, "Explain fetch", Now);
        PromptPad.Remember(data, "other", Now.AddMinutes(1));
        PromptPad.Remember(data, "explain   FETCH", Now.AddMinutes(2));
        Assert.Equal(2, data.Recents.Count);
        Assert.Equal("explain   FETCH", data.Recents[0].Text);
        Assert.Equal(Now.AddMinutes(2), data.Recents[0].UsedUtc);
    }

    [Fact]
    public void Remember_IgnoresBlankPrompts_WithoutTouchingTheList()
    {
        var data = new PromptPadData();
        PromptPad.Remember(data, "kept", Now);
        PromptPad.Remember(data, "   ", Now.AddMinutes(1));
        PromptPad.Remember(data, null, Now.AddMinutes(2));
        Assert.Single(data.Recents);
        Assert.Equal("kept", data.Recents[0].Text);
    }

    [Fact]
    public void Remember_KeepsAtMostTen()
    {
        var data = new PromptPadData();
        for (var i = 0; i < PromptPad.MaxRecents + 5; i++)
            PromptPad.Remember(data, $"prompt {i}", Now.AddMinutes(i));
        Assert.Equal(PromptPad.MaxRecents, data.Recents.Count);
        Assert.Equal($"prompt {PromptPad.MaxRecents + 4}", data.Recents[0].Text);
        Assert.Equal("prompt 5", data.Recents[^1].Text);
    }

    [Fact]
    public void Forget_RemovesTheMatchingPrompt_AndSaysWhetherItDid()
    {
        var data = new PromptPadData();
        PromptPad.Remember(data, "Kept", Now);
        Assert.True(PromptPad.Forget(data, "  kept "));
        Assert.Empty(data.Recents);
        Assert.False(PromptPad.Forget(data, "kept"));
        Assert.False(PromptPad.Forget(data, ""));
    }

    [Fact]
    public void Clear_EmptiesTheList_ButKeepsTheTargetPage()
    {
        var data = new PromptPadData();
        PromptPad.Remember(data, "one", Now);
        PromptPad.WithMode(data, ArenaModes.LeaderboardId);
        PromptPad.Clear(data);
        Assert.Empty(data.Recents);
        Assert.Equal(ArenaModes.LeaderboardId, data.ModeId);
    }

    // ──────────────────────── the target page ────────────────────────

    [Fact]
    public void WithMode_AcceptsOnlyRegisteredModes()
    {
        var data = new PromptPadData();
        PromptPad.WithMode(data, ArenaModes.CodeId);
        Assert.Equal(ArenaModes.CodeId, data.ModeId);
        PromptPad.WithMode(data, "teleport");
        Assert.Equal(ArenaModes.CodeId, data.ModeId);
    }

    [Fact]
    public void TargetMode_FallsBackToBattle()
    {
        Assert.Equal(ArenaModes.BattleId, PromptPad.TargetMode(new PromptPadData()).Id);
        Assert.Equal(ArenaModes.BattleId,
            PromptPad.TargetMode(new PromptPadData { ModeId = "not-a-mode" }).Id);
        Assert.Equal(ArenaModes.SearchId,
            PromptPad.TargetMode(new PromptPadData { ModeId = ArenaModes.SearchId }).Id);
    }

    // ──────────────────────── sanitizing ────────────────────────

    [Fact]
    public void Sanitized_DropsBlanks_KeepsTheNewestOfADuplicate_AndAppliesTheCap()
    {
        var data = new PromptPadData
        {
            ModeId = "nonsense",
            Recents = new List<PromptPadEntry>
            {
                new() { Text = "newer copy", UsedUtc = Now },
                new() { Text = "   ", UsedUtc = Now.AddMinutes(1) },
                new() { Text = null!, UsedUtc = Now.AddMinutes(2) },
                new() { Text = "newer COPY", UsedUtc = Now.AddMinutes(3) },
            },
        };
        var clean = PromptPad.Sanitized(data);
        Assert.Single(clean.Recents);
        Assert.Equal("newer COPY", clean.Recents[0].Text);
        Assert.Equal(ArenaModes.BattleId, clean.ModeId);
    }

    [Fact]
    public void Sanitized_CapsAndSortsNewestFirst()
    {
        var data = new PromptPadData
        {
            Recents = Enumerable.Range(0, PromptPad.MaxRecents + 3)
                .Select(i => new PromptPadEntry { Text = $"p{i}", UsedUtc = Now.AddMinutes(i) })
                .ToList(),
        };
        var clean = PromptPad.Sanitized(data);
        Assert.Equal(PromptPad.MaxRecents, clean.Recents.Count);
        Assert.Equal($"p{PromptPad.MaxRecents + 2}", clean.Recents[0].Text);
    }

    [Fact]
    public void Sanitized_HandlesNullLists()
    {
        var clean = PromptPad.Sanitized(new PromptPadData { Recents = null! });
        Assert.NotNull(clean.Recents);
        Assert.Empty(clean.Recents);
    }

    // ──────────────────────── the store ────────────────────────

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arenadesktop-prompt-pad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Store_MissingFileReadsAsAnEmptyPad()
    {
        var dir = TempDir();
        try
        {
            var data = PromptPadStore.Load(Path.Combine(dir, "prompt-pad.json"));
            Assert.Empty(data.Recents);
            Assert.Equal(ArenaModes.BattleId, data.ModeId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Store_RoundTripsPromptsAndTheTargetPage()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "prompt-pad.json");
            var data = new PromptPadData();
            PromptPad.Remember(data, "Summarise this in one line", Now);
            PromptPad.WithMode(data, ArenaModes.DirectId);
            PromptPadStore.Save(path, data);

            var loaded = PromptPadStore.Load(path);
            Assert.Single(loaded.Recents);
            Assert.Equal("Summarise this in one line", loaded.Recents[0].Text);
            Assert.Equal(Now, loaded.Recents[0].UsedUtc);
            Assert.Equal(ArenaModes.DirectId, loaded.ModeId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Store_KeepsACorruptFileBesideTheFreshPad()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "prompt-pad.json");
            File.WriteAllText(path, "{ this is not json");
            var data = PromptPadStore.Load(path);
            Assert.Empty(data.Recents);
            Assert.Single(Directory.GetFiles(dir, "prompt-pad.json.corrupt-*"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Store_SanitizesWhatItWrites()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "prompt-pad.json");
            var data = new PromptPadData
            {
                Recents = new List<PromptPadEntry> { new() { Text = "   " }, new() { Text = "real", UsedUtc = Now } },
                ModeId = "not-a-mode",
            };
            PromptPadStore.Save(path, data);
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("not-a-mode", text);
            var loaded = PromptPadStore.Load(path);
            Assert.Single(loaded.Recents);
            Assert.Equal("real", loaded.Recents[0].Text);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ──────────────────────── what the window says ────────────────────────

    [Fact]
    public void CharacterCount_IsReadable()
    {
        Assert.Equal("1 character", PromptPad.CharacterCount(1));
        Assert.Equal("412 characters", PromptPad.CharacterCount(412));
        Assert.Equal("1,204 characters", PromptPad.CharacterCount(1204));
        Assert.Equal("0 characters", PromptPad.CharacterCount(0));
    }

    [Fact]
    public void CopyStatus_NamesTheSizeAndThePage_AndNeverClaimsThePaste()
    {
        var battle = ArenaModes.All[0];
        var status = PromptPad.CopyStatus(new string('a', 412), battle);
        Assert.Equal("Copied 412 characters · Battle is ready — paste with Ctrl+V", status);
        Assert.DoesNotContain("pasted", status, StringComparison.OrdinalIgnoreCase);
        // The announcement filter drops anything over 140 characters, so
        // the longest honest sentence has to fit under it.
        Assert.True(status.Length <= 140, $"status was {status.Length} characters");
        Assert.True(PromptPad.EmptyStatus.Length <= 140);
    }

    [Fact]
    public void Preview_IsOneLineAndBounded()
    {
        Assert.Equal("—", PromptPad.Preview(null));
        Assert.Equal("a b", PromptPad.Preview("a\n\n  b"));
        var longText = new string('x', PromptPad.PreviewLength + 20);
        var preview = PromptPad.Preview(longText);
        Assert.EndsWith("…", preview);
        Assert.Equal(PromptPad.PreviewLength + 1, preview.Length);
        Assert.Equal("", PromptPad.Preview("anything", 0));
    }

    [Fact]
    public void RecentsSummary_SaysHowManyAndWhereTheyLive()
    {
        Assert.Contains("No remembered prompts", PromptPad.RecentsSummary(0));
        Assert.Contains("1 remembered prompt", PromptPad.RecentsSummary(1));
        Assert.Contains("4 remembered prompts", PromptPad.RecentsSummary(4));
        Assert.All(new[] { 0, 1, 4 }, n => Assert.Contains("this PC", PromptPad.RecentsSummary(n)));
    }

    // ──────────────────────── every entry point reaches it ────────────────────────

    [Fact]
    public void CommandLine_FlagParsesAndBecomesAnIntent()
    {
        Assert.True(CommandLine.Parse(new[] { "--prompt-pad" }).OpenPromptPad);
        Assert.False(CommandLine.Parse(Array.Empty<string>()).OpenPromptPad);
        var intent = LaunchIntent.FromCommandLine(CommandLine.Parse(new[] { "--prompt-pad" }));
        Assert.Equal(LaunchIntentKind.OpenPromptPad, intent.Kind);
    }

    [Fact]
    public void UsageText_DocumentsTheFlagAndTheLink()
    {
        var text = CommandLineUsage.Text("0.9.0");
        Assert.Contains("--prompt-pad", text);
        Assert.Contains("arenadesktop://prompt-pad", text);
    }

    [Fact]
    public void ProtocolLink_OpensThePad_AndRejectsAPath()
    {
        var opened = LaunchIntent.ParseProtocolUri("arenadesktop://prompt-pad", Array.Empty<string>());
        Assert.Equal(LaunchIntentKind.OpenPromptPad, opened.Intent!.Kind);
        var refused = LaunchIntent.ParseProtocolUri("arenadesktop://prompt-pad/extra", Array.Empty<string>());
        Assert.True(refused.IsRefusal);
    }

    [Fact]
    public void ShortcutCatalog_ListsThePadAsAFixedKey()
    {
        var action = ShortcutCatalog.Find("prompt-pad");
        Assert.NotNull(action);
        Assert.Equal("Ctrl+Shift+P", action!.Gestures[0]);
        Assert.False(action.Remappable);
        // A fixed key, so it can never collide with a remapped action.
        Assert.Contains(ShortcutCatalog.FixedGestures().Values, name => name == action.Name);
    }

    [Fact]
    public void HelpCatalog_HasAPage_AndTheShippedHelpCarriesIt()
    {
        var page = HelpCatalog.ById("prompt-pad");
        Assert.NotNull(page);
        Assert.Equal("How do I use the prompt pad?", page!.Title);
        var help = File.ReadAllText(Path.Combine(RepoRoot(), "Help.md"));
        var body = HelpCatalog.ExtractBody(help, page);
        Assert.NotNull(body);
        Assert.True(body!.Length > 100, $"the prompt pad help page is too thin ({body.Length} chars)");
        Assert.Contains("Ctrl+Shift+P", body);
    }

    [Fact]
    public void MainMenu_HasThePadItemAndPrintsItsKey()
    {
        var doc = System.Xml.Linq.XDocument.Load(
            Path.Combine(RepoRoot(), "src", "ArenaDesktop", "MainWindow.xaml"));
        var item = doc.Descendants().FirstOrDefault(e =>
            e.Attribute("Click")?.Value == "OnPromptPad");
        Assert.NotNull(item);
        Assert.Equal("Prompt pad…", item!.Attribute("Header")?.Value);
        Assert.Equal("Ctrl+Shift+P", item.Attribute("InputGestureText")?.Value);
        // It lives in the Tools group, where the other tool windows are —
        // not at the top level, which is what grew into a flat list before.
        Assert.Equal("Tools", item.Parent?.Attribute("Header")?.Value);
    }

    [Fact]
    public void Shell_KeepsOnePad_AndRoutesEveryEntryPointToIt()
    {
        var shell = File.ReadAllText(Path.Combine(RepoRoot(), "src", "ArenaDesktop", "MainWindow.xaml.cs"));
        Assert.Contains("OpenPromptPadWindow", shell);
        Assert.Contains("openPromptPad: OpenPromptPadWindow", shell);
        Assert.Contains("LaunchIntentKind.OpenPromptPad", shell);
        // The fixed key is dispatched in the shell's own key handler, the
        // one the shortcut catalog is checked against (S6).
        Assert.Contains("Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)", shell);
        var tray = File.ReadAllText(Path.Combine(RepoRoot(), "src", "ArenaDesktop", "TrayController.cs"));
        Assert.Contains("Prompt pad", tray);
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "ArenaDesktop", "App.xaml.cs"));
        Assert.Contains("LaunchIntentKind.OpenPromptPad", app);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(PromptPadTests).Assembly.Location)!);
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
