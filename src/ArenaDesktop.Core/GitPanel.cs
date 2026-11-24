namespace ArenaDesktop.Core;

/// <summary>One row of `git status --porcelain=v1 --branch` output.</summary>
public sealed record GitStatusEntry(char Index, char WorkTree, string Path, string? OriginalPath = null)
{
    public bool IsUntracked => Index == '?' && WorkTree == '?';
    public bool IsStaged => Index is not (' ' or '?');
    public bool IsModifiedInWorkTree => WorkTree is not (' ' or '?');

    public string Describe() =>
        IsUntracked ? "Untracked"
        : (Index, WorkTree) switch
        {
            ('A', _) => "Added",
            ('D', _) or (_, 'D') => "Deleted",
            ('R', _) => "Renamed",
            (_, 'M') => "Modified (unstaged)",
            ('M', _) => "Modified (staged)",
            _ => "Changed",
        };
}

/// <summary>A repository's status: branch line + entries.</summary>
public sealed record GitStatus(string? Branch, IReadOnlyList<GitStatusEntry> Entries)
{
    public bool IsClean => Entries.Count == 0;
}

/// <summary>
/// Native Git backing (completeness slice C): argument building and
/// output parsing for the git CLI, run through IProcessRunner. The
/// app never handles credentials — push/pull run non-interactively
/// (GIT_TERMINAL_PROMPT=0) with the user's own git credential setup,
/// so a missing credential fails plainly instead of hanging.
/// </summary>
public sealed class GitService(IProcessRunner runner, string repoFolder)
{
    public string RepoFolder => repoFolder;

    private static readonly IReadOnlyDictionary<string, string> GitEnv =
        new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0" };

    private Task<ProcessResult> Git(IReadOnlyList<string> args, CancellationToken ct = default) =>
        runner.RunAsync(new ProcessSpec("git", args, repoFolder, GitEnv), null, ct);

    public async Task<bool> IsRepositoryAsync(CancellationToken ct = default)
    {
        var result = await Git(["rev-parse", "--is-inside-work-tree"], ct);
        return result.ExitCode == 0 && result.StandardOutput.Trim() == "true";
    }

    public async Task<GitStatus> StatusAsync(CancellationToken ct = default)
    {
        var result = await Git(["status", "--porcelain=v1", "--branch"], ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git status failed: {ProcessOutcome.FirstLine(result.StandardError)}");
        return ParseStatus(result.StandardOutput);
    }

    public async Task<string> DiffAsync(bool staged, string? path = null, CancellationToken ct = default)
    {
        var args = new List<string> { "diff" };
        if (staged) args.Add("--cached");
        if (!string.IsNullOrWhiteSpace(path)) { args.Add("--"); args.Add(path); }
        var result = await Git(args, ct);
        return result.ExitCode == 0 ? result.StandardOutput : $"git diff failed: {ProcessOutcome.FirstLine(result.StandardError)}";
    }

    public async Task<ProcessResult> StageAsync(IEnumerable<string> paths, CancellationToken ct = default) =>
        await Git(["add", "--", .. paths], ct);

    public async Task<ProcessResult> UnstageAsync(IEnumerable<string> paths, CancellationToken ct = default) =>
        await Git(["restore", "--staged", "--", .. paths], ct);

    public async Task<ProcessResult> CommitAsync(string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A commit message is required.", nameof(message));
        return await Git(["commit", "-m", message.Trim()], ct);
    }

    public async Task<ProcessResult> PushAsync(CancellationToken ct = default) =>
        await Git(["push"], ct);

    public async Task<ProcessResult> PullAsync(CancellationToken ct = default) =>
        await Git(["pull", "--ff-only"], ct);

    public async Task<(string? Current, IReadOnlyList<string> All)> BranchesAsync(CancellationToken ct = default)
    {
        var result = await Git(["branch", "--format=%(HEAD) %(refname:short)"], ct);
        if (result.ExitCode != 0) return (null, []);
        string? current = null;
        var all = new List<string>();
        foreach (var raw in result.StandardOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 3) continue;
            // %(HEAD) is "*" for the current branch, " " otherwise,
            // followed by a space — so lines are "* dev" / "  main".
            if (line.StartsWith("* ")) { current = line[2..]; all.Add(current); }
            else if (line.StartsWith("  ")) all.Add(line[2..]);
        }
        return (current, all);
    }

    public async Task<ProcessResult> SwitchBranchAsync(string branch, CancellationToken ct = default) =>
        await Git(["switch", branch], ct);

    // ───────────────────────── parsing / discovery ─────────────────────────

    public static GitStatus ParseStatus(string porcelain)
    {
        string? branch = null;
        var entries = new List<GitStatusEntry>();
        foreach (var raw in porcelain.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.StartsWith("## "))
            {
                var rest = line[3..];
                var dots = rest.IndexOf("...", StringComparison.Ordinal);
                var space = rest.IndexOf(' ');
                var end = dots >= 0 ? dots : space >= 0 ? space : rest.Length;
                branch = rest[..end].Trim();
                if (branch.StartsWith("No commits yet on ")) branch = branch["No commits yet on ".Length..];
                continue;
            }
            if (line.Length < 4) continue;
            var pathPart = line[3..];
            string? original = null;
            var path = pathPart;
            var arrow = pathPart.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                original = Unquote(pathPart[..arrow]);
                path = Unquote(pathPart[(arrow + 4)..]);
            }
            else path = Unquote(pathPart);
            entries.Add(new GitStatusEntry(line[0], line[1], path, original));
        }
        return new GitStatus(branch, entries);
    }

    private static string Unquote(string path) =>
        path.Length >= 2 && path[0] == '"' && path[^1] == '"' ? path[1..^1] : path;

    /// <summary>
    /// Repositories the panel can switch between: the folder itself
    /// plus its immediate subfolders that contain a .git entry.
    /// </summary>
    public static IReadOnlyList<string> DiscoverRepositories(string folder)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return found;
        if (Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git")))
            found.Add(folder);
        try
        {
            foreach (var sub in Directory.GetDirectories(folder))
            {
                if (Directory.Exists(Path.Combine(sub, ".git")) || File.Exists(Path.Combine(sub, ".git")))
                    found.Add(sub);
            }
        }
        catch (UnauthorizedAccessException) { /* unreadable subfolders are skipped, not fatal */ }
        return found;
    }
}
