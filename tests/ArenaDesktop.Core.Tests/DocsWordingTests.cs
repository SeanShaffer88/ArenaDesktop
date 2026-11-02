using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// Retired-wording guard: the feature that focuses Arena ready to type
/// was renamed from "Quick dictate" to "Quick ask" in v0.1.1 (Arena has
/// no voice button), and the old name must not creep back into the
/// user-facing documents. Historical changelog entries are out of
/// scope — they document the rename itself. The `--dictate`
/// command-line flag stays as a working alias and is not wording.
/// </summary>
public class DocsWordingTests
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("Help.md")]
    [InlineData("WhatsNew.md")]
    public void UserFacingDocs_DoNotUseTheRetiredQuickDictateName(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), fileName));
        Assert.DoesNotContain("Quick dictate", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(DocsWordingTests).Assembly.Location)!);
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
