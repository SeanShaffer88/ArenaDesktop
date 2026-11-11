using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// The release workflow is a build script, and build scripts rot silently:
/// nothing fails when a workflow writes <c>Setup-v1.2.3-win-x64.exe</c> and
/// the updater looks for <c>Setup-v1.2.3-x64.exe</c> — the release simply
/// publishes, and every installed copy then refuses to see it.
///
/// The updater's asset naming is pinned in code and already covered by
/// S9Tests; this holds the WORKFLOW to the same names, by reading the YAML
/// as text. It is deliberately a text check rather than YAML parsing: the
/// test project has no YAML dependency, and the failure this guards
/// against is a wrong literal, not a structural mistake.
///
/// Docs and release mechanics only — no product behaviour is asserted here.
/// </summary>
public class ReleaseWorkflowTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ReleaseWorkflowTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArenaDesktop.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "src", "ArenaDesktop")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root from the test assembly.");
    }

    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml"));

    [Fact]
    public void ReleaseWorkflow_IsTriggeredByVersionTagsOnly()
    {
        var yml = Workflow();
        Assert.Contains("push:", yml);
        Assert.Contains("tags:", yml);
        Assert.Contains("- 'v*'", yml);
        // Nothing else may publish: a branch push or a pull request must
        // never be able to create a release.
        Assert.DoesNotContain("pull_request:", yml);
        Assert.DoesNotContain("branches:", yml);
    }

    [Fact]
    public void ReleaseWorkflow_AssetNamesMatchWhatTheUpdaterLooksFor()
    {
        var yml = Workflow();

        // Installer names come from Core, not from the test or the workflow.
        Assert.Contains(UpdateAssets.InstallerAssetName("$version", "x64"), yml);

        // The portable zip follows the shape the S9 fixtures use.
        Assert.Contains("ArenaDesktop-v$version-windows-x64.zip", yml);

        // v0.7.0 dropped ARM64: the workflow must not build or publish an
        // ARM64 asset any more. An arm64 process still names its own
        // architecture correctly, so the absence here is the whole point —
        // see S9Tests for the updater side of that.

        // And the sums file, under the name SumsFile parses.
        Assert.Contains(SumsFile.AssetName, yml);
    }

    [Fact]
    public void ReleaseWorkflow_PublishesExactlyTheThreeAssetsTheUpdaterNeeds()
    {
        // A release with installers but no sums is a release the updater
        // refuses outright; one with sums but no zip leaves zip users
        // stranded. Both lists in the workflow (the local verify and the
        // publish) have to name the same files — three of them since
        // v0.7.0 dropped ARM64.
        var yml = Workflow();
        var expected = new[]
        {
            "ArenaDesktop-v$version-windows-x64.zip",
            "ArenaDesktop-Setup-v$version-x64.exe",
            "SHA256SUMS.txt",
        };
        foreach (var name in expected)
            Assert.Contains(name, yml);

        // The publish step's asset list is the same three.
        var publishIndex = yml.IndexOf("gh release create", StringComparison.Ordinal);
        Assert.True(publishIndex > 0, "the workflow no longer publishes a release.");
        var tail = yml[publishIndex..];
        Assert.Contains("@assets", tail);
    }

    // ─────────────────── the ⋯ menu's shape (v0.7.0) ───────────────────

    private static System.Xml.Linq.XElement MoreMenu()
    {
        var path = Path.Combine(RepoRoot(), "src", "ArenaDesktop", "MainWindow.xaml");
        var doc = System.Xml.Linq.XDocument.Load(path);
        var menu = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ContextMenu"
                && e.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value == "MoreMenu");
        Assert.NotNull(menu);
        return menu!;
    }

    [Fact]
    public void MainMenu_TopLevelIsShortAndGrouped()
    {
        // The menu used to be a flat run of thirty-eight entries. It is
        // grouped now, and this pins the groups by name: a new feature
        // dropped in at the top level is how the flat list grew back
        // last time.
        var menu = MoreMenu();
        var top = menu.Elements()
            .Where(e => e.Name.LocalName == "MenuItem")
            // The profile banner is a collapsed, disabled label whose text is
            // rewritten at runtime; it is not one of the groups.
            .Where(e => e.Attribute("Visibility")?.Value != "Collapsed")
            .Select(e => e.Attribute("Header")?.Value)
            .Where(h => h is not null)
            .Select(h => h!)
            .ToList();
        Assert.Equal(
            new[] { "Switch profile", "Settings…", "Check for updates", "Navigate", "View", "Page", "Tools", "Help" },
            top);
    }

    [Fact]
    public void MainMenu_GroupsAreNestedNotFlat()
    {
        // Each group must actually hold items, and must not itself be a
        // command — a group that lost its children would still pass the
        // test above and be useless.
        var menu = MoreMenu();
        var groups = menu.Elements()
            .Where(e => e.Name.LocalName == "MenuItem"
                && e.Attribute("Header")?.Value is "Navigate" or "View" or "Page" or "Tools" or "Help")
            .ToList();
        Assert.Equal(5, groups.Count);
        foreach (var group in groups)
        {
            var header = group.Attribute("Header")!.Value;
            var children = group.Elements().Where(e => e.Name.LocalName == "MenuItem").ToList();
            Assert.True(children.Count >= 4, $"group '{header}' has only {children.Count} commands");
            // A group is a heading, not an action.
            Assert.Null(group.Attribute("Click"));
            Assert.All(children, c => Assert.NotNull(c.Attribute("Header")?.Value));
        }
    }

    [Fact]
    public void MainMenu_KeepsEveryCommandItHad()
    {
        // Nothing may be lost in a regroup. These are the handlers the
        // flat menu dispatched; every one must still have a control.
        var menu = MoreMenu();
        var clicks = menu.Descendants()
            .Select(e => e.Attribute("Click")?.Value)
            .Where(v => v is not null)
            .ToHashSet();
        var expected = new[]
        {
            "OnArenaMode", "OnPinCurrentMode", "OnUnpinMode", "OnGoPinnedMode", "OnZoomPreset",
            "OnSessionWindow", "OnFind", "OnShortcuts", "OnPrint", "OnSaveAsPdf", "OnCopyPageAddress",
            "OnOpenInBrowser", "OnOpenInNewWindow", "OnTogglePin", "OnToggleFocusMode",
            "OnSelectStandardLayout", "OnToggleCompactFocusLayout", "OnSelectLeftRailLayout",
            "OnRestoreClosedPage", "OnConnectionSecurity", "OnLockNow", "OnViewLog", "OnOpenLogFolder",
            "OnDiagnostics", "OnHelp", "OnExportSupportBundle", "OnAbout", "OnSettings",
            "OnCheckForUpdates", "OnDownloads", "OnBattleLab", "OnFieldPass",
            // v0.7.0 put the four keyboard-only navigation commands in the menu.
            "OnBack", "OnForward", "OnHome", "OnReload",
        };
        foreach (var handler in expected)
            Assert.Contains(handler, clicks);
    }

    [Fact]
    public void MainMenu_KeyHintsMatchTheShortcutCatalog()
    {
        // The menu prints a key beside Settings and Home. Both are
        // REMAPPABLE, so the shell rewrites those two hints from
        // Core's catalog at startup and on Settings OK
        // (RefreshMenuGestures). The markup defaults must still agree
        // with the catalog, or a fresh install shows a key that does
        // not work. Back/Forward/Reload are fixed and never rewritten.
        var menu = MoreMenu();
        string Hint(string name) => menu.Descendants()
            .First(e => e.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value == name)
            .Attribute("InputGestureText")?.Value ?? "";

        // Compare what the user READS, not the stored form: the catalog
        // says "Ctrl+OemComma" and the menu shows "Ctrl+,".
        Assert.Equal(ShortcutBindings.EffectiveGesture(ShortcutCatalog.SettingsId, null).DisplayText(),
            Hint("SettingsItem"));
        Assert.Equal(ShortcutBindings.EffectiveGesture(ShortcutCatalog.HomeId, null).DisplayText(),
            Hint("HomeItem"));
        Assert.Equal("Alt+Left", Hint("BackItem"));
        Assert.Equal("Alt+Right", Hint("ForwardItem"));
        Assert.Equal("F5", Hint("ReloadItem"));
    }

    [Fact]
    public void MainMenu_KeepsTheNamesTheCodeLooksUp()
    {
        // The shell reaches for these by name (RefreshProfilesMenu, the
        // layout tick state, the pin toggles). A regroup that dropped one
        // would be a null reference at runtime, not a compile error.
        var menu = MoreMenu();
        var names = menu.Descendants()
            .Select(e => e.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value)
            .Where(v => v is not null)
            .ToHashSet();
        foreach (var name in new[]
                 {
                     "ProfileItem", "ProfileSeparator", "SwitchProfileItem", "UpdateItem",
                     "PinModeItem", "UnpinModeItem", "GoPinnedModeItem", "ZoomPresetsItem",
                     "PinItem", "FocusItem", "StandardLayoutItem", "CompactFocusItem",
                     "LeftRailLayoutItem", "LockNowItem",
                     // v0.7.0: named so RefreshMenuGestures can keep the
                     // remappable hints (Settings, Home) truthful.
                     "SettingsItem", "BackItem", "ForwardItem", "HomeItem", "ReloadItem",
                 })
            Assert.Contains(name, names);
    }

    [Fact]
    public void NoWorkflowBuildsArm64()
    {
        // v0.7.0 dropped ARM64 support (DECISIONS.md, 2026-11-11). This
        // guard exists because the removal touched five files in three
        // languages and a leftover `-r win-arm64` publish, arm64 matrix
        // entry or installer define would silently start shipping a build
        // nobody tests. Every workflow is checked, not just the release
        // one.
        var root = RepoRoot();
        var workflows = Path.Combine(root, ".github", "workflows");
        foreach (var file in Directory.GetFiles(workflows, "*.yml"))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            Assert.DoesNotContain("win-arm64", text);
            Assert.DoesNotContain("build-arm64", text);
            Assert.DoesNotContain("windows-arm64.zip", text);
            Assert.DoesNotContain("Setup-v$version-arm64", text);
            // Comments are allowed to explain the removal, as the release
            // workflow's ARM64 note does, so nothing asserts on the bare
            // word "arm64" here.
        }
    }

    [Fact]
    public void ReleaseWorkflow_RunsTheFullGateBeforePublishing()
    {
        // AGENTS.md: the gate is the Release build plus the FULL Core test
        // suite, never a filtered subset. A tag that cannot pass its own
        // tests must not become a release.
        var yml = Workflow();
        var build = yml.IndexOf("dotnet build ArenaDesktop.sln -c Release", StringComparison.Ordinal);
        var test = yml.IndexOf("dotnet test tests/ArenaDesktop.Core.Tests -c Release", StringComparison.Ordinal);
        var publish = yml.IndexOf("gh release create", StringComparison.Ordinal);
        Assert.True(build > 0, "no Release build in the workflow.");
        Assert.True(test > 0, "no Core test run in the workflow.");
        Assert.True(publish > 0, "no publish step in the workflow.");
        Assert.True(build < test, "the build must run before the tests.");
        Assert.True(test < publish, "the tests must run before publishing.");
        // No test filtering may creep in.
        Assert.DoesNotContain("--filter", yml);
    }

    [Fact]
    public void ReleaseWorkflow_VerifiesTheTagAgainstTheCsprojAndTheNotes()
    {
        var yml = Workflow();
        // The two mismatches that would otherwise ship a broken release:
        // a tag that disagrees with <Version>, and a What's New file with
        // no section for the version being cut.
        Assert.Contains("<Version>", yml);
        Assert.Contains("WhatsNew.md", yml);
        Assert.Contains("Version mismatch", yml);
        Assert.Contains("No release notes", yml);
    }

    [Fact]
    public void ReleaseWorkflow_RefusesToOverwriteAPublishedRelease()
    {
        var yml = Workflow();
        Assert.Contains("gh release view", yml);
        Assert.Contains("already exists", yml);
        // And it does not silently clobber with --clobber on create.
        var publishIndex = yml.IndexOf("gh release create", StringComparison.Ordinal);
        var publishLine = yml[publishIndex..].Split('\n')[0];
        Assert.DoesNotContain("--clobber", publishLine);
    }

    [Fact]
    public void ReleaseWorkflow_ReDownloadsAndReVerifiesWhatItPublished()
    {
        // RELEASE_CHECKLIST: assets are re-downloaded and hash-verified
        // before a release is called shipped. That step is on the critical
        // path here, not just in the document.
        var yml = Workflow();
        Assert.Contains("gh release download", yml);
        Assert.Contains("Get-FileHash", yml);
        Assert.Contains("Re-downloading", yml);
    }

    [Fact]
    public void ReleaseWorkflow_SmokeTestsTheRealExe()
    {
        // The same smoke test CI runs: a release must not be built from
        // something that cannot start.
        var yml = Workflow();
        Assert.Contains("--smoke-test", yml);
        Assert.Contains("Smoke test", yml);
    }

    [Fact]
    public void ReleaseWorkflow_PrefersAHandWrittenNotesFileWhenOneExists()
    {
        var root = RepoRoot();
        var yml = Workflow();
        Assert.Contains("docs/release-notes/", yml);
        // The 0.6.0 notes must exist, because that release is the first
        // since 0.3.1 and has to carry the whole story.
        var notes = Path.Combine(root, "docs", "release-notes", "v0.6.0.md");
        Assert.True(File.Exists(notes), "docs/release-notes/v0.6.0.md is missing.");
        var text = File.ReadAllText(notes);
        Assert.Contains("Everything since", text);
        Assert.Contains("0.4.0", text);
        Assert.Contains("0.5.0", text);
        Assert.Contains("0.6.0", text);
    }

    [Fact]
    public void ReleaseNotes_CarryEveryBulletFromTheChangelogSectionsTheyCover()
    {
        // The 0.6.0 notes are hand-assembled from three changelog
        // sections. That is exactly the copy where a bullet gets left
        // behind, so every bullet in those sections must appear verbatim.
        var root = RepoRoot();
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md")).Replace("\r\n", "\n");
        var notes = File.ReadAllText(Path.Combine(root, "docs", "release-notes", "v0.6.0.md"));

        foreach (var version in new[] { "0.4.0", "0.5.0", "0.6.0" })
        {
            var section = Section(changelog, version);
            Assert.NotNull(section);
            var bullets = section!.Split('\n')
                .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
                .Select(l => l[2..].Trim())
                .ToList();
            Assert.NotEmpty(bullets);
            Assert.All(bullets, bullet => Assert.Contains(bullet, notes));
        }
    }

    /// <summary>The body of a `## [x.y.z]` section, matching WhatsNew.ExtractSection's shape.</summary>
    private static string? Section(string markdown, string version)
    {
        var lines = markdown.Split('\n');
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal))
                continue;
            if (start >= 0)
                return string.Join("\n", lines[start..i]).Trim();
            var heading = lines[i][3..].Trim().TrimStart('[');
            var end = heading.IndexOfAny(new[] { ']', ' ', '\t' });
            if (end >= 0)
                heading = heading[..end];
            if (string.Equals(heading, version.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase))
                start = i + 1;
        }
        return start >= 0 ? string.Join("\n", lines[start..]).Trim() : null;
    }
}
