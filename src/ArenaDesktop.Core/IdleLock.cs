namespace ArenaDesktop.Core;

// ───────────────────────── idle lock (enhancement pack 10) ─────────────────────────

/// <summary>
/// When an app-locked session should lock itself because nobody is at the
/// keyboard.
///
/// The lock already engages at startup and when the window hides to the
/// tray (see <see cref="AppSettings.AppLockOnTrayHide"/>). The case that
/// was missing is the one a person leaves behind all day: window open on
/// screen, app lock switched on, and the desk empty. Nothing locked it,
/// so "the app is locked" was true only while the user remembered to hide
/// it.
///
/// This type holds the rule, not the clock or the OS call — the shell
/// feeds it an idle time from <c>GetLastInputInfo</c> and a timestamp, so
/// the decisions below are pinned by tests rather than living in a timer
/// callback.
///
/// Deliberate limits, each of which a test holds down:
/// - <b>off by default</b> (0 minutes), because a lock that surprises
///   someone mid-sentence is worse than no lock at all;
/// - <b>never while the app is already locked</b> — locking a lock is how
///   you get a loop in an idle handler that fires every tick;
/// - <b>never without a usable credential</b> — <see cref="AppLock.IsLockActive"/>
///   is the same gate every other lock path uses, so a half-configured
///   lock can never strand the user outside their own app;
/// - <b>it reports rather than acts</b>: <see cref="Decide"/> returns a
///   verdict and the caller does the work, so an idle tick that decides
///   "no" costs nothing and cannot have a side effect.
/// </summary>
public static class IdleLock
{
    /// <summary>"Never" — the default. Any value at or below this means the feature is off.</summary>
    public const int NeverMinutes = 0;

    /// <summary>
    /// The shortest delay worth offering. Under a minute would lock while
    /// someone is reading a long answer without touching anything.
    /// </summary>
    public const int MinMinutes = 1;

    /// <summary>
    /// The longest delay worth offering. Past this the feature stops being
    /// security and becomes "the app locked overnight again".
    /// </summary>
    public const int MaxMinutes = 120;

    /// <summary>The durations Settings offers, in minutes; 0 is "Never".</summary>
    /// <remarks>
    /// 120 is offered ahead of 60 because 60 is short for someone reading
    /// long answers; <see cref="MaxMinutes"/> is the same 120, so the
    /// widest choice Settings offers is the widest the rule accepts.
    /// </remarks>
    public static readonly IReadOnlyList<int> OfferedMinutes = new[] { 0, 5, 15, 30, 60, 120 };

    /// <summary>The setting's label in Settings.</summary>
    public const string SettingLabel = "Lock after this much time without input";

    /// <summary>The setting's explanation — what it does, and the two things it will never do.</summary>
    public const string SettingNote =
        "When the app lock is on, ArenaDesktop locks itself after this long with no keyboard or mouse input anywhere on the PC. It never locks while the app is already locked or while no password is set. Watching a long answer still counts as idle — pick a delay longer than the pauses you take.";

    /// <summary>True when the delay is a usable setting; 0 is "never" and is valid.</summary>
    public static bool IsValid(int minutes) => minutes == NeverMinutes || minutes is >= MinMinutes and <= MaxMinutes;

    /// <summary>Clamp any stored value into the supported range; anything unrecognisable reads as "never".</summary>
    public static int Sanitize(int minutes) => IsValid(minutes) ? minutes : NeverMinutes;

    /// <summary>True when the feature is on at all.</summary>
    public static bool IsEnabled(int minutes) => Sanitize(minutes) > NeverMinutes;

    /// <summary>
    /// How long a session may sit idle before locking. Zero when the
    /// feature is off, so a caller can compare directly against an idle
    /// time without checking "enabled" first.
    /// </summary>
    public static TimeSpan Threshold(int minutes) =>
        IsEnabled(minutes) ? TimeSpan.FromMinutes(Sanitize(minutes)) : TimeSpan.Zero;

    /// <summary>
    /// Why a decision came out the way it did. Only <see cref="Lock"/> is
    /// actionable; the rest exist so the shell can log a plain reason
    /// instead of a mystery.
    /// </summary>
    public enum Reason
    {
        /// <summary>Nobody has touched the PC for long enough — lock now.</summary>
        Lock,
        /// <summary>The delay is 0 (the default).</summary>
        Disabled,
        /// <summary>The app lock has no usable credential; <see cref="AppLock.IsLockActive"/> said no.</summary>
        NotConfigured,
        /// <summary>Already locked — engage nothing.</summary>
        AlreadyLocked,
        /// <summary>Input happened inside the threshold.</summary>
        NotIdleEnough,
        /// <summary>Can't tell how long the PC has been idle (the OS call failed).</summary>
        UnknownIdle,
    }

    /// <summary>The verdict plus the reason, so a caller can act and log without re-deriving either.</summary>
    public readonly record struct Decision(bool ShouldLock, Reason Why)
    {
        public static Decision No(Reason why) => new(false, why);
    }

    /// <summary>
    /// Should this idle observation lock the app?
    /// </summary>
    /// <param name="minutes">The configured delay (see <see cref="AppSettings.AppLockIdleMinutes"/>).</param>
    /// <param name="idleTime">
    /// How long the PC has been without input, from the OS. Null when the
    /// question couldn't be answered — treated as "don't know", never as
    /// "idle forever".
    /// </param>
    /// <param name="lockActive">Whether a usable credential exists (<see cref="AppLock.IsLockActive"/>).</param>
    /// <param name="alreadyLocked">Whether the app is locked right now.</param>
    public static Decision Decide(int minutes, TimeSpan? idleTime, bool lockActive, bool alreadyLocked)
    {
        if (alreadyLocked)
            return Decision.No(Reason.AlreadyLocked);
        if (!IsEnabled(minutes))
            return Decision.No(Reason.Disabled);
        if (!lockActive)
            return Decision.No(Reason.NotConfigured);
        if (idleTime is not { } idle)
            return Decision.No(Reason.UnknownIdle);
        return idle >= Threshold(minutes)
            ? new Decision(true, Reason.Lock)
            : Decision.No(Reason.NotIdleEnough);
    }

    /// <summary>
    /// The sentence Settings shows under the picker, naming the delay in
    /// words rather than repeating the number the combo already shows.
    /// </summary>
    public static string SummaryText(int minutes) => Sanitize(minutes) switch
    {
        0 => "Off — ArenaDesktop never locks itself. It still locks at startup and when it hides to the tray, if those are on.",
        var m when m < 60 => $"On — locks after {m} minutes with no input anywhere on the PC.",
        60 => "On — locks after an hour with no input anywhere on the PC.",
        var m => $"On — locks after {m / 60} hours with no input anywhere on the PC.",
    };
}
