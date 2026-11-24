using System.Windows;
using System.Windows.Controls;
using ArenaDesktop.Core;
using Forms = System.Windows.Forms;

namespace ArenaDesktop;

/// <summary>
/// Native Git panel (slice C): status/diff/stage/commit/push/pull and
/// repository + branch switching for the user's project folder, all
/// over Core's GitService and the git CLI. Credentials are never the
/// app's business — git uses the user's own setup, non-interactively.
/// </summary>
public partial class GitWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IProcessRunner _runner;
    private readonly DiagnosticLog _log;
    private readonly Action _onSettingsChanged;
    private readonly Action<string>? _onTaskCompleted;
    private GitService? _git;
    private GitStatus? _status;
    private bool _populating;

    public GitWindow(AppSettings settings, IProcessRunner runner, DiagnosticLog log,
        Action onSettingsChanged, Action<string>? onTaskCompleted = null)
    {
        InitializeComponent();
        _settings = settings;
        _runner = runner;
        _log = log;
        _onSettingsChanged = onSettingsChanged;
        _onTaskCompleted = onTaskCompleted;
        Loaded += async (_, _) => { PopulateRepos(); await RefreshAsync(); };
    }

    private void PopulateRepos()
    {
        _populating = true;
        try
        {
            var repos = _settings.ProjectFolder is { } folder
                ? GitService.DiscoverRepositories(folder) : [];
            RepoCombo.ItemsSource = repos;
            RepoCombo.SelectedItem = repos.FirstOrDefault();
            _git = RepoCombo.SelectedItem is string repo ? new GitService(_runner, repo) : null;
            StatusText.Text = _settings.ProjectFolder is null
                ? "Choose a project folder to begin. The Git panel and the terminal work there."
                : repos.Count == 0 ? "No git repository in that folder (or its immediate subfolders)."
                : "";
        }
        finally { _populating = false; }
    }

    private async Task RefreshAsync()
    {
        if (_git is null) { FilesList.ItemsSource = null; DiffBox.Text = ""; return; }
        try
        {
            if (!await _git.IsRepositoryAsync())
            {
                StatusText.Text = "That folder is not a git working tree.";
                FilesList.ItemsSource = null;
                return;
            }
            _status = await _git.StatusAsync();
            FilesList.ItemsSource = _status.Entries
                .Select(e => new FileRow(e)).ToList();
            var (current, all) = await _git.BranchesAsync();
            BranchCombo.ItemsSource = all;
            BranchCombo.SelectedItem = current;
            StatusText.Text = _status.IsClean
                ? $"On {_status.Branch ?? "(detached)"} — nothing to commit, working tree clean."
                : $"On {_status.Branch ?? "(detached)"} — {_status.Entries.Count} changed file(s). Select files, then Stage selected.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private sealed record FileRow(GitStatusEntry Entry)
    {
        public override string ToString() => $"{Entry.Describe()} — {Entry.Path}";
    }

    // The ListBox shows text rows; staging uses the current selection
    // (multi-select) rather than per-row checkboxes to stay simple.
    private GitStatusEntry? SelectedEntry =>
        FilesList.SelectedItem is FileRow row ? row.Entry : null;

    private List<string> SelectedPaths =>
        FilesList.SelectedItems.Cast<FileRow>().Select(r => r.Entry.Path).ToList();

    private async void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_git is null || SelectedEntry is not { } entry) { DiffBox.Text = ""; return; }
        DiffBox.Text = await _git.DiffAsync(entry.IsStaged && !entry.IsModifiedInWorkTree, entry.Path);
        if (string.IsNullOrWhiteSpace(DiffBox.Text))
            DiffBox.Text = await _git.DiffAsync(false, entry.Path);
    }

    private async Task RunAndRefresh(Func<Task<ProcessResult>> op, string what)
    {
        StatusText.Text = $"{what}…";
        try
        {
            var result = await op();
            var text = ProcessOutcome.Describe(what, result);
            StatusText.Text = text;
            _log.Info($"Git: {text}");
            if (what is "Push" or "Pull") _onTaskCompleted?.Invoke(text);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        await RefreshAsync();
    }

    private async void OnStage(object sender, RoutedEventArgs e)
    {
        if (_git is null || SelectedPaths.Count == 0) { StatusText.Text = "Select file(s) first (Ctrl/Shift-click for several)."; return; }
        var paths = SelectedPaths;
        await RunAndRefresh(() => _git.StageAsync(paths), "Stage");
    }

    private async void OnUnstage(object sender, RoutedEventArgs e)
    {
        if (_git is null || SelectedPaths.Count == 0) { StatusText.Text = "Select file(s) first."; return; }
        var paths = SelectedPaths;
        await RunAndRefresh(() => _git.UnstageAsync(paths), "Unstage");
    }

    private async void OnCommit(object sender, RoutedEventArgs e)
    {
        if (_git is null) return;
        if (string.IsNullOrWhiteSpace(CommitBox.Text)) { StatusText.Text = "Write a commit message first."; return; }
        var message = CommitBox.Text;
        await RunAndRefresh(() => _git.CommitAsync(message), "Commit");
        CommitBox.Text = "";
    }

    private async void OnPush(object sender, RoutedEventArgs e)
    {
        if (_git is null) return;
        await RunAndRefresh(() => _git.PushAsync(), "Push");
    }

    private async void OnPull(object sender, RoutedEventArgs e)
    {
        if (_git is null) return;
        await RunAndRefresh(() => _git.PullAsync(), "Pull");
    }

    private async void OnSwitchBranch(object sender, RoutedEventArgs e)
    {
        if (_git is null || BranchCombo.SelectedItem is not string branch) return;
        await RunAndRefresh(() => _git.SwitchBranchAsync(branch), $"Switch to {branch}");
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnRepoChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        _git = RepoCombo.SelectedItem is string repo ? new GitService(_runner, repo) : null;
        await RefreshAsync();
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose the project folder for the Git panel and terminal",
            SelectedPath = _settings.ProjectFolder ?? "",
            ShowNewFolderButton = false,
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        _settings.ProjectFolder = dialog.SelectedPath;
        _onSettingsChanged();
        PopulateRepos();
        _ = RefreshAsync();
    }
}
