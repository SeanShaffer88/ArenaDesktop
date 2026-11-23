using System.Diagnostics;
using System.Text;

namespace ArenaDesktop.Core;

/// <summary>
/// What to say when the app hits an exception it did not expect (v0.13.0).
/// Until now the dialog told the user a file name; the one thing it did not
/// offer was the part that would let a report be acted on — which exception,
/// thrown where. This turns an <see cref="Exception"/> into a few short
/// facts (type, message, the app frame that threw, the innermost cause) and
/// a copyable package with the tail of the log, bounded so a clipboard entry
/// stays a paragraph rather than a megabyte.
/// <para>
/// Everything here is pure text: no windows, no clipboard, no file writes.
/// The shell decides what to show and what to copy, which is why this is
/// testable at all.
/// </para>
/// </summary>
public static class CrashReport
{
    /// <summary>Long enough for a real message, short enough for a dialog line.</summary>
    public const int MaxMessageLength = 200;

    /// <summary>The log tail that rides along with a crash report.</summary>
    public const int MaxTailLines = 25;

    /// <summary>…and its hard character cap, because a log line can be 500 characters.</summary>
    public const int MaxTailChars = 4000;

    /// <summary>The report's facts, already cleaned for display.</summary>
    public sealed record Facts(string Type, string Message, string Where, string? Inner);

    /// <summary>Exception type, message, throwing app frame and innermost cause.</summary>
    public static Facts Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new Facts(
            exception.GetType().FullName ?? exception.GetType().Name,
            Clean(exception.Message),
            Where(exception),
            Innermost(exception));
    }

    /// <summary>One line for the dialog: <c>Type: message</c>.</summary>
    public static string Summary(Exception exception)
    {
        var facts = Describe(exception);
        return $"{facts.Type}: {facts.Message}";
    }

    /// <summary>
    /// The first stack frame that is not the framework's — the app's own code,
    /// which is the only part a user's report can lead to a fix in. Falls back
    /// to the throwing frame itself, then to a plain "no stack" answer: a
    /// report that says so is better than one that pretends.
    /// </summary>
    public static string Where(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var trace = new StackTrace(exception, fNeedFileInfo: false);
        var fallback = trace.FrameCount > 0 ? trace.GetFrame(0)?.GetMethod() : null;
        for (var i = 0; i < trace.FrameCount; i++)
        {
            var method = trace.GetFrame(i)?.GetMethod();
            var type = method?.DeclaringType;
            if (method is null || type is null)
                continue;
            if (IsFramework(type.Namespace))
                continue;
            return $"{type.FullName}.{method.Name}()";
        }
        return fallback?.DeclaringType is { } fallbackType
            ? $"{fallbackType.FullName}.{fallback.Name}()"
            : "unknown (no stack trace)";
    }

    /// <summary>Type and message of the deepest cause, or null when there is only one exception.</summary>
    public static string? Innermost(Exception exception)
    {
        var current = exception;
        var walked = 0;
        while (current.InnerException is { } inner && walked < 16)
        {
            current = inner;
            walked++;
        }
        if (ReferenceEquals(current, exception))
            return null;
        var type = current.GetType().FullName ?? current.GetType().Name;
        return $"{type}: {Clean(current.Message)}";
    }

    /// <summary>
    /// The last <paramref name="lines"/> lines of a log, then cut to
    /// <paramref name="chars"/> characters from the end (the newest lines are
    /// the ones that explain the crash). Empty in, empty out.
    /// </summary>
    public static string Tail(string? logText, int lines = MaxTailLines, int chars = MaxTailChars)
    {
        if (string.IsNullOrWhiteSpace(logText))
            return "";
        var all = logText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var end = all.Length;
        while (end > 0 && all[end - 1].Trim().Length == 0)
            end--;
        if (end == 0)
            return "";
        var start = Math.Max(0, end - Math.Max(1, lines));
        var joined = string.Join("\n", all[start..end]);
        var limit = Math.Max(1, chars);
        return joined.Length <= limit ? joined : "…" + joined[^limit..];
    }

    /// <summary>The log's tail, or "" when it cannot be read — a crash report must never throw.</summary>
    public static string TailFromFile(string path, int lines = MaxTailLines)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "";
            return Tail(File.ReadAllText(path), lines);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// The text the dialog copies to the clipboard. Deterministic on purpose —
    /// the caller supplies version, runtime and clock, so the same crash always
    /// produces the same package (and the test can assert it).
    /// </summary>
    public static string Package(
        string version,
        string runtime,
        DateTimeOffset when,
        string logPath,
        Exception exception,
        string? logText)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var facts = Describe(exception);
        var sb = new StringBuilder();
        sb.Append("ArenaDesktop ").Append(string.IsNullOrWhiteSpace(version) ? "(unknown version)" : version)
          .Append(" crash report\n");
        sb.Append("When: ").Append(when.ToString("yyyy-MM-dd HH:mm:ss zzz")).Append('\n');
        sb.Append("Runtime: ").Append(string.IsNullOrWhiteSpace(runtime) ? "(unknown)" : runtime).Append('\n');
        sb.Append("Log: ").Append(logPath).Append('\n');
        sb.Append('\n');
        sb.Append("Exception: ").Append(facts.Type).Append(": ").Append(facts.Message).Append('\n');
        sb.Append("Where: ").Append(facts.Where).Append('\n');
        if (facts.Inner is { } inner)
            sb.Append("Inner: ").Append(inner).Append('\n');
        var tail = Tail(logText);
        if (tail.Length > 0)
        {
            sb.Append('\n');
            sb.Append("Last ").Append(MaxTailLines).Append(" log lines:\n");
            sb.Append(Safe(tail)).Append('\n');
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>The package without a live exception — used when the window itself will not open.</summary>
    public static string PackageForText(string version, string runtime, DateTimeOffset when, string logPath,
        string summary, string? logText)
    {
        var sb = new StringBuilder();
        sb.Append("ArenaDesktop ").Append(string.IsNullOrWhiteSpace(version) ? "(unknown version)" : version)
          .Append(" crash report\n");
        sb.Append("When: ").Append(when.ToString("yyyy-MM-dd HH:mm:ss zzz")).Append('\n');
        sb.Append("Runtime: ").Append(string.IsNullOrWhiteSpace(runtime) ? "(unknown)" : runtime).Append('\n');
        sb.Append("Log: ").Append(logPath).Append('\n');
        sb.Append('\n');
        sb.Append("Exception: ").Append(Clean(summary)).Append('\n');
        var tail = Tail(logText);
        if (tail.Length > 0)
        {
            sb.Append('\n');
            sb.Append("Last ").Append(MaxTailLines).Append(" log lines:\n");
            sb.Append(Safe(tail)).Append('\n');
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>
    /// A crash report leaves the machine only because the user pastes it, but
    /// "only because" is not a reason to hand it something the support bundle
    /// would have stripped: the same <see cref="RedactionAudit"/> pass runs
    /// here, so a query string, an address or a token in the log cannot ride
    /// along in a copy.
    /// </summary>
    private static string Safe(string tail)
    {
        try
        {
            return RedactionAudit.Redact(tail);
        }
        catch
        {
            // Redaction is best-effort by design; refusing to report at all
            // would be worse than reporting exactly what the log says.
            return tail;
        }
    }

    private static bool IsFramework(string? ns) =>
        string.IsNullOrEmpty(ns)
        || ns.StartsWith("System", StringComparison.Ordinal)
        || ns.StartsWith("Microsoft", StringComparison.Ordinal)
        || ns.StartsWith("MS.", StringComparison.Ordinal)
        || ns.StartsWith("Internal", StringComparison.Ordinal);

    /// <summary>One line, bounded — a message cannot smuggle newlines into the report.</summary>
    private static string Clean(string? message)
    {
        var text = (message ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        return text.Length <= MaxMessageLength ? text : text[..(MaxMessageLength - 1)] + "…";
    }
}
