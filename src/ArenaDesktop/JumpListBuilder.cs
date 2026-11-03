using System.Windows.Shell;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// The taskbar Jump List (S4 #33): right-click the taskbar button for
/// Open Arena, Settings, the Arena modes category (Battle & Leaderboard,
/// led by the user's favorite modes since v0.3.0), Quick ask (S11 #97), and one task per
/// profile (most recently used first). Tasks launch this exe with the
/// command-line flags, so they ride the one intent surface (S11
/// #100): on a cold start --profile persists as the active profile;
/// on a running instance the flags arrive as a LaunchIntent — a
/// profile task asks before switching (the usual confirm + restart),
/// never switches silently, and while the app is locked every task
/// waits behind the lock. Rebuilt whenever the profile list or
/// active profile changes; every failure is a log line.
/// </summary>
public static class JumpListBuilder
{
    /// <summary>Jump Lists get crowded fast; the most recent handful of profiles is plenty.</summary>
    private const int MaxProfileTasks = 8;

    /// <summary>The modes category's modes (v0.3.0): favorites first (user order), then Battle and Leaderboard when not already listed.</summary>
    private static IReadOnlyList<ArenaMode> JumpListModes(IReadOnlyList<string>? favoriteModeIds)
    {
        var modes = FavoriteModes.Sanitize(favoriteModeIds)
            .Select(id => ArenaModes.TryGet(id, out var mode) ? mode : null)
            .Where(m => m is not null)
            .Cast<ArenaMode>()
            .ToList();
        foreach (var id in new[] { ArenaModes.BattleId, ArenaModes.LeaderboardId })
        {
            if (modes.All(m => m.Id != id) && ArenaModes.TryGet(id, out var standing))
                modes.Add(standing);
        }
        return modes;
    }

    public static void Apply(AppSettings settings, DiagnosticLog log)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;

            var list = new JumpList
            {
                ShowFrequentCategory = false,
                ShowRecentCategory = false,
            };

            list.JumpItems.Add(new JumpTask
            {
                Title = "Open Arena",
                Description = "Open ArenaDesktop at your start page",
                ApplicationPath = exe,
                Arguments = "",
                IconResourcePath = exe,
            });
            list.JumpItems.Add(new JumpTask
            {
                Title = "Settings",
                Description = "Open ArenaDesktop and its Settings window",
                ApplicationPath = exe,
                Arguments = "--settings",
                IconResourcePath = exe,
            });
            // Arena modes category (v0.3.0): the favorite modes lead,
            // in the user's order; Battle and Leaderboard — the pair
            // the list has always carried — follow when they aren't
            // already favorites. No favorites = exactly that pair, in
            // that order, as before.
            foreach (var mode in JumpListModes(settings.FavoriteModeIds))
            {
                list.JumpItems.Add(new JumpTask
                {
                    Title = mode.Label,
                    Description = mode.Id switch
                    {
                        ArenaModes.BattleId => "Open ArenaDesktop at Battle — two anonymous models, you vote",
                        ArenaModes.LeaderboardId => "Open ArenaDesktop at the public model Leaderboard",
                        _ => mode.Note,
                    },
                    ApplicationPath = exe,
                    Arguments = mode.Id switch
                    {
                        ArenaModes.BattleId => "--battle",
                        ArenaModes.LeaderboardId => "--leaderboard",
                        _ => $"--mode {mode.Id}",
                    },
                    IconResourcePath = exe,
                    CustomCategory = "Arena modes",
                });
            }
            list.JumpItems.Add(new JumpTask
            {
                Title = "Quick ask",
                Description = "Open ArenaDesktop with the Arena page focused, ready to type",
                ApplicationPath = exe,
                Arguments = "--dictate",
                IconResourcePath = exe,
            });

            // Profiles: most recently used first, never-launched last
            // (stable within each group by list order).
            var ordered = settings.Profiles
                .OrderByDescending(p => p.LastUsedUtc.HasValue)
                .ThenByDescending(p => p.LastUsedUtc ?? DateTimeOffset.MinValue)
                .Take(MaxProfileTasks);
            foreach (var profile in ordered)
            {
                list.JumpItems.Add(new JumpTask
                {
                    Title = profile.Id == settings.ActiveProfileId
                        ? $"{profile.Name} (current)"
                        : profile.Name,
                    Description = $"Open ArenaDesktop with the {profile.Name} profile",
                    ApplicationPath = exe,
                    Arguments = $"--profile {profile.Id}",
                    IconResourcePath = exe,
                    CustomCategory = "Profiles",
                });
            }

            JumpList.SetJumpList(System.Windows.Application.Current, list);
            list.Apply();
        }
        catch (Exception ex)
        {
            log.Warn($"Jump List update failed: {ex.Message}");
        }
    }
}
