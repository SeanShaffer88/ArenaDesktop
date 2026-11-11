using System.Text;

namespace ArenaDesktop.Core;

// ───────────────────────── re-entry guard for modal work (hardening pack) ─────────────────────────

/// <summary>
/// Stops one user action from starting twice (hardening pack).
///
/// Async work driven by a button click — a settings save, an update
/// check, a support-bundle export — can be re-entered: the user
/// double-clicks, or presses the accelerator again while the first run is
/// still awaiting. Today each of those paths carries its own bool
/// (<c>_checkingUpdate</c>, <c>_exporting</c>, …) and each one has to
/// remember to reset it in a <c>finally</c>. Getting that wrong leaves a
/// surface permanently dead; forgetting it entirely leaves two copies of
/// the work racing over the same file.
///
/// This is the one shape, testable, so the answers are pinned:
/// a second call while busy is refused rather than queued, and disposal
/// is what resets it (so a caller cannot forget).
/// </summary>
public sealed class BusyGuard
{
    private int _busy;

    /// <summary>True while a call holds the guard.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>
    /// Try to take the guard. On success the caller MUST dispose the
    /// returned scope (a <c>using</c> statement does it). On failure the
    /// returned scope is a no-op, so the call pattern is always
    /// <c>using var scope = guard.TryEnter(); if (!scope.Entered) return;</c>
    /// — no early-return path can leak the flag.
    /// </summary>
    public Scope TryEnter() =>
        Interlocked.CompareExchange(ref _busy, 1, 0) == 0 ? new Scope(this) : Scope.Refused;

    /// <summary>What <see cref="TryEnter"/> hands back; disposing a granted scope releases the guard exactly once.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly BusyGuard? _owner;

        // internal, not private: a nested type's private members are not
        // visible to the enclosing class, and TryEnter (on BusyGuard)
        // is what grants a scope. External callers still cannot build
        // one themselves — they only ever get what TryEnter hands back.
        internal Scope(BusyGuard owner) => _owner = owner;

        /// <summary>A scope that never held the guard.</summary>
        internal static readonly Scope Refused = default;

        /// <summary>True when this call took the guard and must dispose the scope.</summary>
        public bool Entered => _owner is not null;

        public void Dispose()
        {
            if (_owner is not null)
                Interlocked.Exchange(ref _owner._busy, 0);
        }
    }
}

// ───────────────────────── clipboard lifetime (hardening pack) ─────────────────────────

/// <summary>
/// How long a copied prompt stays on the clipboard before the app puts it
/// back (hardening pack, opt-in).
///
/// Copying a prompt is how Battle Lab hands text to Arena, and the
/// clipboard is a shared, long-lived surface: the prompt (which may name
/// a private project or contain a customer's details) sits there for the
/// rest of the session, available to every app on the PC and to
/// clipboard-history tools. The user pressed Copy to paste it once.
///
/// Two rules make this safe enough to default OFF but useful when turned
/// on, and both are enforced here rather than in a timer callback:
/// - the app only ever CLEARS text it put there itself — if the clipboard
///   now holds something else, the user copied something in the meantime
///   and it must not be touched;
/// - an empty string is a valid outcome (clearing with an empty string
///   rather than a null clipboard, which some apps handle badly).
/// </summary>
public static class ClipboardLifetime
{
    /// <summary>The offered durations, in minutes.</summary>
    public static readonly IReadOnlyList<int> OfferedMinutes = new[] { 1, 2, 5, 15 };

    /// <summary>Below this, the user could not realistically get to Arena and paste.</summary>
    public const int MinMinutes = 1;

    /// <summary>Above this, "clear it later" stops being a privacy feature and becomes a surprise.</summary>
    public const int MaxMinutes = 30;

    /// <summary>The setting's label in Settings.</summary>
    public const string SettingLabel = "Clear copied prompts from the clipboard after…";

    /// <summary>The setting's explanation — what it does, and the one thing it will never do.</summary>
    public const string SettingNote =
        "A copied prompt is cleared from the clipboard after the delay you pick. Only text ArenaDesktop put there is cleared — if you copied something else in the meantime, it is left alone. Never clears while ArenaDesktop is locked.";

    /// <summary>Whether the delay is a usable setting (0 = never).</summary>
    public static bool IsValid(int minutes) => minutes == 0 || minutes is >= MinMinutes and <= MaxMinutes;

    /// <summary>Clamp any stored value into the supported range; unknown values read as "never".</summary>
    public static int Sanitize(int minutes) => IsValid(minutes) ? minutes : 0;

    /// <summary>True when a copy should arm a clear at all.</summary>
    public static bool ShouldArm(int minutes) => minutes >= MinMinutes;

    /// <summary>When a copy at <paramref name="copiedUtc"/> should be cleared.</summary>
    public static DateTimeOffset? ClearAt(DateTimeOffset copiedUtc, int minutes) =>
        ShouldArm(minutes) ? copiedUtc + TimeSpan.FromMinutes(minutes) : null;

    /// <summary>How much time is left, or zero once the deadline has passed.</summary>
    public static TimeSpan TimeLeft(DateTimeOffset nowUtc, DateTimeOffset? clearAtUtc) =>
        clearAtUtc is { } at && at > nowUtc ? at - nowUtc : TimeSpan.Zero;

    /// <summary>"2 min left" / "clearing now" — the status-bar note while a clear is armed.</summary>
    public static string RemainingText(DateTimeOffset nowUtc, DateTimeOffset? clearAtUtc)
    {
        var left = TimeLeft(nowUtc, clearAtUtc);
        if (clearAtUtc is null)
            return "";
        if (left <= TimeSpan.Zero)
            return "clearing now";
        var minutes = (int)Math.Ceiling(left.TotalMinutes);
        return minutes <= 1 ? "under a minute left" : $"{minutes} min left";
    }

    /// <summary>
    /// Whether the clipboard may be cleared now: the deadline has passed
    /// AND the clipboard still holds exactly the text this app copied.
    /// </summary>
    public static bool ShouldClear(DateTimeOffset nowUtc, DateTimeOffset? clearAtUtc, string? copiedText, string? clipboardText)
    {
        if (clearAtUtc is not { } at || nowUtc < at)
            return false;
        if (string.IsNullOrEmpty(copiedText))
            return false;
        return string.Equals(copiedText, clipboardText, StringComparison.Ordinal);
    }
}

// ───────────────────────── first-run readiness (hardening pack) ─────────────────────────

/// <summary>
/// The one-line answer to "will this PC run me?" (hardening pack).
///
/// The app already checks its runtime in three places — the missing
/// WebView2 overlay, the Diagnostics runtime panel, and the field pass —
/// and each speaks its own dialect. This is the summary a first launch
/// (and Diagnostics) can show before anything has had a chance to fail:
/// the runtime's presence and age, the settings folder's writability, and
/// the download folder's readiness, reduced to one verdict and at most
/// three facts.
///
/// It reports only what it was given: a check that could not run is
/// "unknown", never a pass.
/// </summary>
public sealed record ReadinessReport(
    bool RuntimeOk,
    bool SettingsWritable,
    bool? DownloadsOk,
    string? RuntimeVersion)
{
    /// <summary>True when nothing known is wrong — an unmeasured check is not a failure.</summary>
    public bool CanRun => RuntimeOk && SettingsWritable && DownloadsOk != false;

    /// <summary>The one-line verdict.</summary>
    public string Headline
    {
        get
        {
            if (!RuntimeOk)
                return "This PC is missing something ArenaDesktop needs — see below.";
            if (!SettingsWritable)
                return "ArenaDesktop can run, but it can't save its settings.";
            if (DownloadsOk == false)
                return "ArenaDesktop can run — downloads need a different folder.";
            return "This PC is ready.";
        }
    }

    /// <summary>
    /// The facts behind the verdict, in the order a person would want
    /// them: the runtime first (nothing works without it), then the
    /// folders. A check that wasn't measured says so instead of guessing.
    /// </summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            var lines = new List<string>
            {
                RuntimeOk
                    ? $"WebView2 Runtime: present{(string.IsNullOrWhiteSpace(RuntimeVersion) ? "" : $" (version {RuntimeVersion})")}"
                    : "WebView2 Runtime: missing or too old — install the current Evergreen runtime from Microsoft, then reopen ArenaDesktop",
                SettingsWritable
                    ? "Settings folder: writable"
                    : "Settings folder: read-only — changes won't survive a restart (check the folder's permissions)",
            };
            lines.Add(DownloadsOk switch
            {
                true => "Downloads folder: ready",
                false => "Downloads folder: not usable — pick another one in Settings",
                _ => "Downloads folder: not checked yet",
            });
            return lines;
        }
    }

    /// <summary>
    /// Compose the report from the pieces the shell already has. A null
    /// <paramref name="downloadsOk"/> means "not measured", which is
    /// different from "measured, and it's broken".
    /// </summary>
    public static ReadinessReport Compose(
        RuntimeHealthResult? runtime, bool settingsWritable, bool? downloadsOk)
    {
        // Runtime health has four verdicts and only one of them is good.
        // "Outdated" is NOT ready: the app runs, but on a runtime old
        // enough to miss security years, and the health line already says
        // so. Treating it as a pass here would contradict the Diagnostics
        // panel on the same screen.
        var ok = runtime?.Status == RuntimeHealthStatus.Ok;
        return new ReadinessReport(ok, settingsWritable, downloadsOk, runtime?.Version);
    }

    /// <summary>The report as plain text, for a bug report or the support bundle.</summary>
    public string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Headline);
        foreach (var line in Lines)
            builder.AppendLine("  " + line);
        return builder.ToString().TrimEnd();
    }
}
