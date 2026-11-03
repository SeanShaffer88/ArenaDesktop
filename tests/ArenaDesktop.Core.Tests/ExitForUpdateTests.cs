using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

// ───────────────── seamless updates: the exit-for-update intent ─────────────────

/// <summary>
/// The exit-for-update handshake's Core half (seamless update,
/// ported from MuseDesktop's R7D tests): the
/// <c>--exit-for-update</c> flag, its intent, the intent's trip
/// through the pending-intent file, and the routing policy that
/// applies it IMMEDIATELY — never queued behind the app lock
/// (exiting reveals nothing; the lock re-engages at next launch),
/// never a show/navigation intent. The installer drives the rest
/// (process detection, fallbacks, relaunch) from the NSIS script.
/// </summary>
public class ExitForUpdateFlagTests
{
    [Fact]
    public void Flag_ParsesAlone()
    {
        var options = CommandLine.Parse(new[] { "--exit-for-update" });
        Assert.True(options.ExitForUpdate);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void Flag_ParsesCaseInsensitively_LikeEveryOtherFlag()
    {
        Assert.True(CommandLine.Parse(new[] { "--EXIT-FOR-UPDATE" }).ExitForUpdate);
    }

    [Fact]
    public void Flag_ParsesMixedWithToleratedJunk()
    {
        // A second launch's arguments are never fatal: unknown
        // arguments are collected, and the flag still lands.
        var options = CommandLine.Parse(new[] { "--nonsense", "--exit-for-update", "stray" });
        Assert.True(options.ExitForUpdate);
        Assert.Equal(new[] { "--nonsense", "stray" }, options.Unknown);
    }

    [Fact]
    public void Flag_AbsentByDefault()
    {
        Assert.False(CommandLine.Parse(Array.Empty<string>()).ExitForUpdate);
        Assert.False(CommandLine.Parse(new[] { "--home" }).ExitForUpdate);
    }

    [Fact]
    public void Flag_IsDocumentedInTheUsageText()
    {
        Assert.Contains("--exit-for-update", CommandLineUsage.Text("0.3.0"));
    }
}

public class ExitForUpdateIntentTests
{
    [Fact]
    public void Intent_FromCommandLine_FlagBecomesTheExitIntent()
    {
        var intent = LaunchIntent.FromCommandLine(
            CommandLine.Parse(new[] { "--exit-for-update" }));
        Assert.Equal(LaunchIntentKind.ExitForUpdate, intent.Kind);
        Assert.Null(intent.ProfileId);
    }

    [Fact]
    public void Intent_ExitOutranksEveryOtherActionFlag()
    {
        // The installer is waiting to replace this build: a profile
        // switch or an opened surface would be work done by a
        // process that is about to exit.
        foreach (var args in new[]
                 {
                     new[] { "--exit-for-update", "--settings" },
                     new[] { "--exit-for-update", "--home" },
                     new[] { "--exit-for-update", "--downloads" },
                     new[] { "--exit-for-update", "--battle-lab" },
                     new[] { "--exit-for-update", "--battle" },
                     new[] { "--exit-for-update", "--profile", "work" },
                 })
        {
            var intent = LaunchIntent.FromCommandLine(CommandLine.Parse(args));
            Assert.Equal(LaunchIntentKind.ExitForUpdate, intent.Kind);
        }
    }

    [Fact]
    public void Intent_RoundTripsThroughJson()
    {
        var intent = new LaunchIntent(LaunchIntentKind.ExitForUpdate);
        Assert.Equal(intent, LaunchIntent.TryFromJson(intent.ToJson()));
    }

    [Fact]
    public void Intent_JsonKindNameIsPinnedForCrossVersionHandoff()
    {
        // The pending-intent file crosses builds: the installer's
        // handshake process (new build) writes it, the running
        // instance (possibly an older build) reads it. Kinds travel
        // by NAME, so the literal string is a wire contract — a
        // rename here must fail loudly, not degrade silently to a
        // plain wake (round-trip tests cannot catch it: both sides
        // rename together).
        var intent = new LaunchIntent(LaunchIntentKind.ExitForUpdate);
        Assert.Contains("\"ExitForUpdate\"", intent.ToJson());
        Assert.Equal(intent,
            LaunchIntent.TryFromJson("{\"kind\":\"ExitForUpdate\"}"));
    }

    [Fact]
    public void Intent_RoundTripsThroughThePendingIntentStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ad-efu-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "pending-intent.json");
        try
        {
            PendingIntentStore.Save(path, new LaunchIntent(LaunchIntentKind.ExitForUpdate));
            var taken = PendingIntentStore.Take(path);
            Assert.Equal(LaunchIntentKind.ExitForUpdate, taken?.Kind);
            Assert.False(File.Exists(path)); // taken = deleted
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }
}

public class ExitForUpdateRoutingTests
{
    [Fact]
    public void Policy_ExitIntentAppliesImmediately_EvenWhileLocked()
    {
        var exit = new LaunchIntent(LaunchIntentKind.ExitForUpdate);
        Assert.True(LaunchIntentRouting.AppliesImmediately(exit));
        Assert.Equal(IntentDisposition.ApplyNow, LaunchIntentRouting.Decide(exit, appLocked: true));
        Assert.Equal(IntentDisposition.ApplyNow, LaunchIntentRouting.Decide(exit, appLocked: false));
    }

    [Theory]
    [InlineData(LaunchIntentKind.ShowWindow)]
    [InlineData(LaunchIntentKind.GoHome)]
    [InlineData(LaunchIntentKind.OpenSettings)]
    [InlineData(LaunchIntentKind.OpenDownloads)]
    [InlineData(LaunchIntentKind.OpenBattleLab)]
    [InlineData(LaunchIntentKind.OpenFieldPass)]
    [InlineData(LaunchIntentKind.ShowHelp)]
    [InlineData(LaunchIntentKind.QuickDictate)]
    [InlineData(LaunchIntentKind.SwitchProfile)]
    [InlineData(LaunchIntentKind.GoArenaMode)]
    public void Policy_OrdinaryIntentsAreNotImmediate_AndStillQueueBehindTheLock(LaunchIntentKind kind)
    {
        var intent = kind == LaunchIntentKind.SwitchProfile
            ? new LaunchIntent(kind, "work")
            : kind == LaunchIntentKind.GoArenaMode
                ? new LaunchIntent(kind, ModeId: ArenaModes.BattleId)
                : new LaunchIntent(kind);
        Assert.False(LaunchIntentRouting.AppliesImmediately(intent));
        Assert.Equal(IntentDisposition.QueueBehindLock, LaunchIntentRouting.Decide(intent, appLocked: true));
        Assert.Equal(IntentDisposition.ApplyNow, LaunchIntentRouting.Decide(intent, appLocked: false));
    }
}
