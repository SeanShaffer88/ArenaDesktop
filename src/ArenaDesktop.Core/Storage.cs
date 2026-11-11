using System.Text;

namespace ArenaDesktop.Core;

// ───────────────────────── durable writes (hardening pack) ─────────────────────────

/// <summary>
/// One place for "replace this file's contents without ever leaving it
/// half-written" (hardening pack). Every store used to open-code the
/// same three lines — write <c>path + ".tmp"</c>, then move it over the
/// target — which worked, but left each caller to get the details
/// right on its own: the directory might not exist, a crash between the
/// write and the move left a stray <c>.tmp</c> beside the settings, and
/// a target locked by an antivirus scan threw away the whole write.
///
/// The rules here are the ones that matter, once:
/// - the parent directory is created first;
/// - the temp file carries a unique name, so two writers (a settings
///   save racing an app-lock write) can never scribble on each other;
/// - the bytes land before the swap, so the target is either the old
///   file or the new one, never a truncated mix;
/// - the swap retries briefly, because the Windows file system can
///   refuse a rename for a few milliseconds while another process has
///   the target open for a scan;
/// - a failed write cleans up its own temp file (a stray file beside
///   the user's settings is litter, not a diagnostic).
/// The last known-good content is kept by the CALLER's own backup path
/// — this helper deliberately does not invent a second one.
/// </summary>
public static class AtomicFile
{
    /// <summary>How many times the final swap is retried before giving up (each try waits a little longer).</summary>
    public const int SwapAttempts = 4;

    /// <summary>Write <paramref name="text"/> (UTF-8, no BOM) and swap it over <paramref name="path"/>.</summary>
    public static void WriteAllText(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // A unique temp name: same directory (so the swap is a rename on
        // one volume, never a copy) but never a name another writer uses.
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SwapInto(temp, path);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Overwrite <paramref name="path"/> with bytes. The byte-level twin of
    /// <see cref="WriteAllText"/> for callers that already hold encoded data
    /// (the encrypted settings export).
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            SwapInto(temp, path);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Move the finished temp file over the target, retrying the rename a
    /// few times. The retry exists for the everyday Windows case: a virus
    /// scanner, a backup agent or the user's editor holds the target open
    /// for a moment, and the rename is refused with a sharing violation
    /// that clears by itself. Waiting a few milliseconds beats failing a
    /// settings save the user just asked for.
    /// </summary>
    private static void SwapInto(string temp, string target)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < SwapAttempts)
            {
                Thread.Sleep(15 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < SwapAttempts)
            {
                Thread.Sleep(15 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A temp file we couldn't remove is inert: it is never read
            // back, and the next write uses a fresh name.
        }
    }
}

// ───────────────────────── bounded reads (hardening pack) ─────────────────────────

/// <summary>
/// Reading a bounded slice of a file without loading the whole thing
/// (hardening pack). The Diagnostics report wants the tail of the log,
/// and the log is capped at 1 MB — loading and splitting all of it to
/// keep five lines is wasted work on the UI thread, and gets worse if
/// the cap is ever raised.
///
/// The window is read from the END of the file, so "the last 200 lines"
/// costs 200 lines' worth of bytes rather than the whole file. A file
/// smaller than the window is simply read whole.
/// </summary>
public static class TextFileSlice
{
    /// <summary>How many bytes the tail reader reads at most when no window is given.</summary>
    public const int DefaultWindowBytes = 96 * 1024;

    /// <summary>
    /// The last <paramref name="maxLines"/> lines of the file at
    /// <paramref name="path"/> — the same result as reading the file and
    /// calling <see cref="LogReading.TailLines"/>, but reading only the
    /// file's tail. Returns an empty string when the file is missing or
    /// unreadable (a diagnostic helper is never worth an exception).
    /// </summary>
    public static string ReadTailLines(string? path, int maxLines, int windowBytes = DefaultWindowBytes)
    {
        if (string.IsNullOrWhiteSpace(path) || maxLines <= 0)
            return "";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            if (length == 0)
                return "";
            var window = Math.Max(4096, Math.Min(windowBytes, length));
            var start = length - window;
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[window];
            var read = 0;
            while (read < buffer.Length)
            {
                var got = stream.Read(buffer, read, buffer.Length - read);
                if (got <= 0)
                    break;
                read += got;
            }
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            // A non-zero start very likely lands mid-line (and possibly
            // mid-character). Drop the partial first line — the caller
            // asked for whole lines, and half a log line reads as damage.
            if (start > 0)
            {
                var firstBreak = text.IndexOf('\n');
                text = firstBreak >= 0 ? text[(firstBreak + 1)..] : "";
            }
            return LogReading.TailLines(text, maxLines);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>How many bytes a file holds, or null when it can't be measured.</summary>
    public static long? SizeOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch
        {
            return null;
        }
    }
}

// ───────────────────────── damaged-file preservation (hardening pack) ─────────────────────────

/// <summary>
/// What to do with a file that exists but cannot be read as what it
/// claims to be (hardening pack). Settings already did this — copy the
/// unreadable file aside as <c>name.corrupt-&lt;stamp&gt;</c> before
/// falling back to defaults — but the other stores silently started
/// from empty, which quietly destroys a Battle Lab library or a field
/// pass the user may want to hand to someone for repair.
///
/// Keeping a copy is cheap (the files are small), and it changes the
/// failure from "my prompts are gone" to "my prompts are in a file
/// beside the app, and the app told me so".
/// </summary>
public static class CorruptFile
{
    /// <summary>The marker a preserved copy carries, between the original name and the stamp.</summary>
    public const string Marker = ".corrupt-";

    /// <summary>
    /// Copy <paramref name="path"/> to a sibling <c>name.corrupt-&lt;stamp&gt;</c>
    /// file and return the copy's path, or null when there is nothing to
    /// preserve or the copy failed. Never throws: preserving a file is a
    /// courtesy, and the caller's contract is always "start from defaults".
    /// </summary>
    public static string? Preserve(string? path, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            if (!File.Exists(path))
                return null;
            // Local time in the stamp: this name is for a person to read
            // while looking for their lost data, not for sorting machines.
            var backup = $"{path}{Marker}{now.ToLocalTime():yyyyMMdd-HHmmss}";
            // Two corruptions inside one second must not overwrite the
            // first copy — that copy is the one with the user's data.
            var attempt = 1;
            while (File.Exists(backup))
            {
                if (++attempt > 9)
                    return null;
                backup = $"{path}{Marker}{now.ToLocalTime():yyyyMMdd-HHmmss}-{attempt}";
            }
            File.Copy(path, backup, overwrite: false);
            return backup;
        }
        catch
        {
            return null;
        }
    }
}
