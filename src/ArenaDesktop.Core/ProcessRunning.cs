namespace ArenaDesktop.Core;

/// <summary>One process to run: a file and an argument list (never a shell string).</summary>
public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>How a process ended. Output is complete (also streamed live when a sink was given).</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool Cancelled = false);

/// <summary>
/// Runs a process with captured output. The shell implements this
/// over System.Diagnostics.Process; Core logic (git, shell jobs)
/// depends only on this seam, so it is fully testable with a fake.
/// Implementations must honour cancellation by killing the process.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        IProgress<string>? outputLines = null,
        CancellationToken ct = default);
}

/// <summary>Shared outcome wording for a finished process.</summary>
public static class ProcessOutcome
{
    public static string Describe(string what, ProcessResult result) =>
        result.Cancelled ? $"{what} was stopped."
        : result.ExitCode == 0 ? $"{what} finished."
        : $"{what} failed (exit {result.ExitCode}){(string.IsNullOrWhiteSpace(result.StandardError) ? "." : $": {FirstLine(result.StandardError)}")}";

    public static string FirstLine(string text) =>
        text.Split('\n', 2)[0].Trim();
}
