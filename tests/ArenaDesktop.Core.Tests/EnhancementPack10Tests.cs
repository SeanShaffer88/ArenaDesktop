using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>
/// Enhancement pack 10: the idle lock, the zoom report, and the log level
/// filter.
///
/// Each of these adds behaviour a person can feel — the app locking itself
/// while they are away, a zoom key that finally says what it did, a log
/// filter that only hides what it claims to hide — so the rules are pinned
/// here rather than left inside a timer callback or a window.
/// </summary>
public class EnhancementPack10Tests
{
    // ─────────────────────── 1. idle lock ───────────────────────

    [Fact]
    public void IdleLock_IsOffByDefault_AndOffIsAValidSetting()
    {
        Assert.Equal(IdleLock.NeverMinutes, new AppSettings().AppLockIdleMinutes);
        Assert.False(IdleLock.IsEnabled(0));
        Assert.True(IdleLock.IsValid(0));
        Assert.Equal(TimeSpan.Zero, IdleLock.Threshold(0));
        Assert.Contains(0, IdleLock.OfferedMinutes);
    }

    [Fact]
    public void IdleLock_EveryOfferedDelayIsValid_AndTheRangeIsHonest()
    {
        Assert.All(IdleLock.OfferedMinutes, m => Assert.True(IdleLock.IsValid(m), m.ToString()));
        Assert.True(IdleLock.IsValid(IdleLock.MinMinutes));
        Assert.True(IdleLock.IsValid(IdleLock.MaxMinutes));
        Assert.False(IdleLock.IsValid(IdleLock.MinMinutes - 1));
        Assert.False(IdleLock.IsValid(IdleLock.MaxMinutes + 1));
        Assert.False(IdleLock.IsValid(-1));
    }

    [Fact]
    public void IdleLock_SanitizeNeverClampsUpwards()
    {
        // A hand-edited 600 must read as "never", not become a two-hour
        // lock the user never chose.
        Assert.Equal(0, IdleLock.Sanitize(600));
        Assert.Equal(0, IdleLock.Sanitize(-30));
        Assert.Equal(0, IdleLock.Sanitize(0));
        Assert.Equal(15, IdleLock.Sanitize(15));
    }

    [Fact]
    public void IdleLock_LocksOnlyWhenEveryConditionHolds()
    {
        var threshold = TimeSpan.FromMinutes(15);
        var decision = IdleLock.Decide(15, threshold, lockActive: true, alreadyLocked: false);
        Assert.True(decision.ShouldLock);
        Assert.Equal(IdleLock.Reason.Lock, decision.Why);
        // One second short is not enough.
        Assert.False(IdleLock.Decide(15, threshold - TimeSpan.FromSeconds(1), true, false).ShouldLock);
    }

    [Fact]
    public void IdleLock_DeclinesForEachReasonSeparately()
    {
        var idle = TimeSpan.FromHours(1);
        Assert.Equal(IdleLock.Reason.Disabled,
            IdleLock.Decide(0, idle, lockActive: true, alreadyLocked: false).Why);
        Assert.Equal(IdleLock.Reason.NotConfigured,
            IdleLock.Decide(15, idle, lockActive: false, alreadyLocked: false).Why);
        Assert.Equal(IdleLock.Reason.AlreadyLocked,
            IdleLock.Decide(15, idle, lockActive: true, alreadyLocked: true).Why);
        Assert.Equal(IdleLock.Reason.NotIdleEnough,
            IdleLock.Decide(15, TimeSpan.FromMinutes(2), lockActive: true, alreadyLocked: false).Why);
        Assert.Equal(IdleLock.Reason.UnknownIdle,
            IdleLock.Decide(15, null, lockActive: true, alreadyLocked: false).Why);
    }

    [Fact]
    public void IdleLock_AlreadyLockedOutranksEverythingElse()
    {
        // The handler runs every 30 seconds while the app is locked; if
        // this returned Lock the app would lock itself in a loop.
        var decision = IdleLock.Decide(15, TimeSpan.FromDays(1), lockActive: true, alreadyLocked: true);
        Assert.False(decision.ShouldLock);
        Assert.Equal(IdleLock.Reason.AlreadyLocked, decision.Why);
    }

    [Fact]
    public void IdleLock_UnknownIdleTimeIsNeverTreatedAsIdleForever()
    {
        // A failed OS call must not lock the app by accident — the
        // dangerous default is "don't know, so don't act".
        foreach (var minutes in IdleLock.OfferedMinutes.Where(m => m > 0))
        {
            var decision = IdleLock.Decide(minutes, idleTime: null, lockActive: true, alreadyLocked: false);
            Assert.False(decision.ShouldLock, minutes.ToString());
            Assert.Equal(IdleLock.Reason.UnknownIdle, decision.Why);
        }
    }

    [Fact]
    public void IdleLock_SurvivesAnOutOfRangeStoredValue()
    {
        // Sanitize runs on load, but Decide must not trust its caller.
        var decision = IdleLock.Decide(999, TimeSpan.FromDays(10), lockActive: true, alreadyLocked: false);
        Assert.False(decision.ShouldLock);
        Assert.Equal(IdleLock.Reason.Disabled, decision.Why);
    }

    [Fact]
    public void IdleLock_SummaryNamesTheDelayInWords()
    {
        Assert.StartsWith("Off", IdleLock.SummaryText(0));
        Assert.Contains("15 minutes", IdleLock.SummaryText(15));
        Assert.Contains("an hour", IdleLock.SummaryText(60));
        Assert.Contains("2 hours", IdleLock.SummaryText(120));
        // Out-of-range reads as off in the prose too.
        Assert.StartsWith("Off", IdleLock.SummaryText(5000));
    }

    [Fact]
    public void IdleLock_SettingRoundTripsThroughTheSettingsFile()
    {
        var settings = new AppSettings { AppLockIdleMinutes = 30 };
        var clone = settings.Clone();
        Assert.Equal(30, clone.AppLockIdleMinutes);

        var json = SettingsStore.Export(settings);
        Assert.True(SettingsStore.TryImport(json, out var imported, out _));
        Assert.Equal(30, imported!.AppLockIdleMinutes);
    }

    [Fact]
    public void IdleLock_IsSanitizedOnLoad_AndResetByDefaults()
    {
        var broken = new AppSettings { AppLockIdleMinutes = 10_000 };
        Assert.Equal(0, broken.Sanitized().AppLockIdleMinutes);

        var reset = new AppSettings { AppLockIdleMinutes = 45 };
        SettingsReset.ApplyDefaults(reset);
        Assert.Equal(IdleLock.NeverMinutes, reset.AppLockIdleMinutes);
    }

    // ─────────────────────── 2. zoom report ───────────────────────

    [Fact]
    public void ZoomReport_NamesThePercent()
    {
        Assert.Equal("Zoom 100%", ZoomMath.Report(1.0, atLimit: false, extendedRange: false));
        Assert.Equal("Zoom 125%", ZoomMath.Report(1.25, atLimit: false, extendedRange: false));
        Assert.Equal("Zoom 50%", ZoomMath.Report(0.5, atLimit: false, extendedRange: false));
    }

    [Fact]
    public void ZoomReport_AtALimitSaysWhyNothingMoved()
    {
        var max = ZoomMath.MaxFor(extendedRange: false);
        var atMax = ZoomMath.Report(max, atLimit: true, extendedRange: false);
        Assert.Contains("largest", atMax);
        var min = ZoomMath.MinFor(extendedRange: false);
        var atMin = ZoomMath.Report(min, atLimit: true, extendedRange: false);
        Assert.Contains("smallest", atMin);
        // And the two are never the same sentence.
        Assert.NotEqual(atMax, atMin);
    }

    [Fact]
    public void ZoomReport_LimitWordingFollowsTheActiveRange()
    {
        // The word has to describe the range actually in use, or it tells
        // the user to give up at a limit they could pass by enabling the
        // extended range.
        var standardMax = ZoomMath.MaxFor(extendedRange: false);
        var message = ZoomMath.Report(standardMax, atLimit: true, extendedRange: false);
        Assert.Contains("range", message);
        Assert.Contains(ZoomMath.FormatPercent(standardMax), message);
    }

    [Fact]
    public void ZoomStepsAtTheLimitDoNotClaimToHaveMoved()
    {
        // The caller decides "at limit" by comparing before and after;
        // this pins that the math really does return the same value.
        var max = ZoomMath.MaxFor(extendedRange: false);
        Assert.Equal(max, ZoomMath.StepUp(max, extendedRange: false));
        var min = ZoomMath.MinFor(extendedRange: false);
        Assert.Equal(min, ZoomMath.StepDown(min, extendedRange: false));
    }

    // ─────────────────────── 3. log level filter ───────────────────────

    private const string SampleLog =
        "2026-11-11 09:00:00 [INFO] App started\n" +
        "2026-11-11 09:00:01 [WARN] Tray icon failed\n" +
        "2026-11-11 09:00:02 [ERROR] Update check failed\n" +
        "2026-11-11 09:00:03 [INFO] Navigated to Battle\n";

    [Fact]
    public void LogLevel_ReadsTheMarkerTheLoggerWrites()
    {
        Assert.Equal(LogLevel.Info, LogLevelFilter.Classify("2026-11-11 09:00:00 [INFO] x"));
        Assert.Equal(LogLevel.Warn, LogLevelFilter.Classify("2026-11-11 09:00:00 [WARN] x"));
        Assert.Equal(LogLevel.Error, LogLevelFilter.Classify("2026-11-11 09:00:00 [ERROR] x"));
    }

    [Fact]
    public void LogLevel_DoesNotPromoteAMessageThatMentionsError()
    {
        // This is the bug a substring filter ships with: an INFO line
        // reading "an error happened" is NOT an error line.
        Assert.Equal(LogLevel.Info, LogLevelFilter.Classify("2026-11-11 09:00:00 [INFO] an error happened"));
        Assert.Equal(LogLevel.Warn, LogLevelFilter.Classify("2026-11-11 09:00:00 [WARN] error recovery"));
        Assert.Equal(LogLevel.Info, LogLevelFilter.Classify("2026-11-11 09:00:00 [INFO] mentions [ERROR] inline"));
    }

    [Fact]
    public void LogLevel_UnknownOrMalformedIsUnrecognised_NotDroppedSilently()
    {
        Assert.Equal(LogLevel.Unrecognised, LogLevelFilter.Classify("2026-11-11 09:00:00 [DEBUG] future"));
        Assert.Equal(LogLevel.Unrecognised, LogLevelFilter.Classify("no timestamp [ERROR] here"));
        Assert.Equal(LogLevel.Unrecognised, LogLevelFilter.Classify("garbage"));
        Assert.Equal(LogLevel.Unrecognised, LogLevelFilter.Classify(""));
        Assert.Equal(LogLevel.Unrecognised, LogLevelFilter.Classify(null));
    }

    [Fact]
    public void LogLevel_ApplyKeepsOnlyWhatWasAskedFor()
    {
        var errors = LogLevelFilter.Apply(SampleLog, new[] { LogLevel.Error });
        Assert.Single(errors.Split('\n'));
        Assert.Contains("Update check failed", errors);
        Assert.DoesNotContain("App started", errors);

        var noisy = LogLevelFilter.Apply(SampleLog, new[] { LogLevel.Error, LogLevel.Warn });
        Assert.Equal(2, noisy.Split('\n').Length);
    }

    [Fact]
    public void LogLevel_AnEmptySelectionMeansEverything()
    {
        // The viewer opens in this state; "nothing ticked" must never mean
        // "show nothing".
        Assert.Equal(SampleLog, LogLevelFilter.Apply(SampleLog, Array.Empty<LogLevel>()));
        Assert.Equal(SampleLog, LogLevelFilter.Apply(SampleLog, null));
    }

    [Fact]
    public void LogLevel_CountsAddUpToTheLineCount()
    {
        var counts = LogLevelFilter.Counts(SampleLog);
        Assert.Equal(2, counts[LogLevel.Info]);
        Assert.Equal(1, counts[LogLevel.Warn]);
        Assert.Equal(1, counts[LogLevel.Error]);
        Assert.Equal(0, counts[LogLevel.Unrecognised]);
        Assert.Equal(SampleLog.TrimEnd('\n').Split('\n').Length, counts.Values.Sum());
    }

    [Fact]
    public void LogLevel_CountingIsEmptySafe()
    {
        var counts = LogLevelFilter.Counts("");
        Assert.All(counts.Values, v => Assert.Equal(0, v));
        Assert.Equal("", LogLevelFilter.Apply("", new[] { LogLevel.Error }));
        Assert.Equal("", LogLevelFilter.Apply(null, new[] { LogLevel.Error }));
    }

    [Fact]
    public void LogLevel_EverySelectableLevelHasALabel_AndProblemsAreNamed()
    {
        Assert.All(LogLevelFilter.Selectable, l => Assert.NotEmpty(LogLevelFilter.Label(l)));
        Assert.True(LogLevelFilter.IsProblem(LogLevel.Error));
        Assert.True(LogLevelFilter.IsProblem(LogLevel.Warn));
        Assert.False(LogLevelFilter.IsProblem(LogLevel.Info));
        // The chips cover every level the classifier can return.
        Assert.Contains(LogLevel.Info, LogLevelFilter.Selectable);
        Assert.Contains(LogLevel.Warn, LogLevelFilter.Selectable);
        Assert.Contains(LogLevel.Error, LogLevelFilter.Selectable);
        Assert.Contains(LogLevel.Unrecognised, LogLevelFilter.Selectable);
    }

    [Fact]
    public void LogLevel_FilterAndFindAgreeAboutWhatIsOnScreen()
    {
        // The viewer re-runs find against the filtered text; this pins the
        // composition so a count can never describe hidden lines.
        var filtered = LogLevelFilter.Apply(SampleLog, new[] { LogLevel.Error });
        Assert.Empty(LogSearch.FindAll(filtered, "App started"));
        Assert.Single(LogSearch.FindAll(filtered, "Update check"));
        Assert.Equal(4, LogSearch.FindAll(SampleLog, "2026-11-11").Count);
    }
}
