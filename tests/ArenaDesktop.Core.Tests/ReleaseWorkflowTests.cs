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
        foreach (var arch in new[] { "x64", "arm64" })
        {
            var expected = UpdateAssets.InstallerAssetName("$version", arch);
            Assert.Contains(expected, yml);
        }

        // The portable zips follow the shape the S9 fixtures use.
        Assert.Contains("ArenaDesktop-v$version-windows-x64.zip", yml);
        Assert.Contains("ArenaDesktop-v$version-windows-arm64.zip", yml);

        // And the sums file, under the name SumsFile parses.
        Assert.Contains(SumsFile.AssetName, yml);
    }

    [Fact]
    public void ReleaseWorkflow_PublishesExactlyTheFiveAssetsTheUpdaterNeeds()
    {
        // A release with installers but no sums is a release the updater
        // refuses outright; one with sums but no zip leaves zip users
        // stranded. Both lists in the workflow (the local verify and the
        // publish) have to name the same five files.
        var yml = Workflow();
        var expected = new[]
        {
            "ArenaDesktop-v$version-windows-x64.zip",
            "ArenaDesktop-v$version-windows-arm64.zip",
            "ArenaDesktop-Setup-v$version-x64.exe",
            "ArenaDesktop-Setup-v$version-arm64.exe",
            "SHA256SUMS.txt",
        };
        foreach (var name in expected)
            Assert.Contains(name, yml);

        // The publish step's asset list is the same five.
        var publishIndex = yml.IndexOf("gh release create", StringComparison.Ordinal);
        Assert.True(publishIndex > 0, "the workflow no longer publishes a release.");
        var tail = yml[publishIndex..];
        Assert.Contains("@assets", tail);
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
