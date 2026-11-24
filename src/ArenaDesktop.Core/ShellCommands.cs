namespace ArenaDesktop.Core;

/// <summary>Where a shell job is in its life.</summary>
public enum ShellJobState { Running, Finished, Failed, Stopped }

/// <summary>
/// One terminal/script command run (completeness slices C/D5): the
/// command text, the folder it ran in, its streamed output (capped)
/// and how it ended. The shell hosts the actual process; this model
/// is the record both the terminal window and the notification
/// centre read.
/// </summary>
public sealed class ShellJob
{
    public const int MaxLines = 2000;

    public required string Id { get; init; }
    public required string CommandText { get; init; }
    public required string WorkingFolder { get; init; }
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; private set; }
    public int? ExitCode { get; private set; }
    public ShellJobState State { get; private set; } = ShellJobState.Running;

    private readonly List<string> _lines = [];
    public IReadOnlyList<string> Lines => _lines;
    public bool OutputTruncated { get; private set; }

    public void AppendLine(string line)
    {
        if (_lines.Count >= MaxLines) { OutputTruncated = true; return; }
        _lines.Add(line);
    }

    public void Finish(int exitCode, bool cancelled)
    {
        FinishedUtc = DateTimeOffset.UtcNow;
        ExitCode = cancelled ? null : exitCode;
        State = cancelled ? ShellJobState.Stopped : exitCode == 0 ? ShellJobState.Finished : ShellJobState.Failed;
    }

    public TimeSpan? Duration => FinishedUtc is { } end ? end - StartedUtc : null;

    /// <summary>One-line outcome for notifications and status surfaces.</summary>
    public string Summary =>
        State switch
        {
            ShellJobState.Running => $"Running: {CommandText}",
            ShellJobState.Finished => $"Finished: {CommandText}",
            ShellJobState.Failed => $"Failed (exit {ExitCode}): {CommandText}",
            ShellJobState.Stopped => $"Stopped: {CommandText}",
            _ => CommandText,
        };

    /// <summary>The process spec for a command: the Windows shell runs the text, in the folder.</summary>
    public static ProcessSpec SpecFor(string commandText, string workingFolder) =>
        new("cmd.exe", ["/d", "/c", commandText], workingFolder);
}
