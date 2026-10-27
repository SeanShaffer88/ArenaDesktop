using System.Windows.Shell;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// The taskbar Jump List (S4 #33): right-click the taskbar button for
/// Open Arena, Settings, Battle & Leaderboard (Arena modes), Quick ask (S11 #97), and one task per
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
            list.JumpItems.Add(new JumpTask
            {
                Title = "Battle",
                Description = "Open ArenaDesktop at Battle — two anonymous models, you vote",
                ApplicationPath = exe,
                Arguments = "--battle",
                IconResourcePath = exe,
                CustomCategory = "Arena modes",
            });
            list.JumpItems.Add(new JumpTask
            {
                Title = "Leaderboard",
                Description = "Open ArenaDesktop at the public model Leaderboard",
                ApplicationPath = exe,
                Arguments = "--leaderboard",
                IconResourcePath = exe,
                CustomCategory = "Arena modes",
            });
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
