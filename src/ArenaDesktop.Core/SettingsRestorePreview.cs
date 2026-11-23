namespace ArenaDesktop.Core;

/// <summary>A safe, high-level preview of settings that differ before a restore.</summary>
public sealed record SettingsRestorePreview(IReadOnlyList<string> ChangedAreas, IReadOnlyList<string> ProfileNames)
{
    public bool HasChanges => ChangedAreas.Count > 0;

    public string Summary => !HasChanges
        ? "This backup has the same settings as the current window."
        : "Changed areas: " + string.Join("; ", ChangedAreas) + ".";

    public static SettingsRestorePreview Compare(AppSettings current, AppSettings restored)
    {
        var changes = new List<string>();
        if (current.Profiles.Count != restored.Profiles.Count
            || current.ActiveProfileId != restored.ActiveProfileId
            || !current.Profiles.Select(p => p.Id).SequenceEqual(restored.Profiles.Select(p => p.Id), StringComparer.Ordinal)
            || !current.Profiles.Select(ProfileSettingsTransfer.Capture)
                .SequenceEqual(restored.Profiles.Select(ProfileSettingsTransfer.Capture))
            || current.LastUrl != restored.LastUrl
            || !current.LastUrlByProfile.OrderBy(p => p.Key).SequenceEqual(restored.LastUrlByProfile.OrderBy(p => p.Key))
            || !current.LastModeIdByProfile.OrderBy(p => p.Key).SequenceEqual(restored.LastModeIdByProfile.OrderBy(p => p.Key))
            || !current.PendingProfileFolderDeletions.SequenceEqual(restored.PendingProfileFolderDeletions))
            changes.Add("profile list, preferences or remembered pages");
        if (current.Theme != restored.Theme || current.AccentColor != restored.AccentColor
            || current.ShellFontScale != restored.ShellFontScale || current.ShellLayout != restored.ShellLayout
            || !current.ToolbarOrder.SequenceEqual(restored.ToolbarOrder)
            || !current.ToolbarHidden.SequenceEqual(restored.ToolbarHidden))
            changes.Add("appearance and toolbar");
        if (current.StartWithWindows != restored.StartWithWindows
            || current.StartMinimizedToTray != restored.StartMinimizedToTray
            || current.CloseToTray != restored.CloseToTray
            || current.SuspendWhenInTray != restored.SuspendWhenInTray
            || current.TraySuspendTimeout != restored.TraySuspendTimeout
            || current.StartupMode != restored.StartupMode
            || current.PinnedModeId != restored.PinnedModeId
            || current.DimToolbarWhenReading != restored.DimToolbarWhenReading
            || current.GlobalHotkeyEnabled != restored.GlobalHotkeyEnabled
            || current.GlobalHotkeyGesture != restored.GlobalHotkeyGesture
            || current.RegisterProtocolLinks != restored.RegisterProtocolLinks
            || current.FirstRunCompleted != restored.FirstRunCompleted
            || !current.FavoriteModeIds.SequenceEqual(restored.FavoriteModeIds))
            changes.Add("startup and behavior");
        if (current.AlwaysOnTop != restored.AlwaysOnTop
            || current.WindowLeft != restored.WindowLeft || current.WindowTop != restored.WindowTop
            || current.WindowWidth != restored.WindowWidth || current.WindowHeight != restored.WindowHeight
            || current.WindowMaximized != restored.WindowMaximized
            || current.SettingsWindowWidth != restored.SettingsWindowWidth
            || current.SettingsWindowHeight != restored.SettingsWindowHeight
            || current.SettingsLastSection != restored.SettingsLastSection
            || !PlacementsEqual(current, restored))
            changes.Add("window placement and Settings memory");
        if (current.TraySingleClickRestore != restored.TraySingleClickRestore
            || current.FlashTaskbarOnAttention != restored.FlashTaskbarOnAttention
            || current.QuietHoursEnabled != restored.QuietHoursEnabled
            || current.QuietHoursStartMinutes != restored.QuietHoursStartMinutes
            || current.QuietHoursEndMinutes != restored.QuietHoursEndMinutes)
            changes.Add("tray and notifications");
        if (current.DownloadsFolder != restored.DownloadsFolder
            || current.AskWhereToSave != restored.AskWhereToSave
            || current.NotifyWhenDownloadCompletes != restored.NotifyWhenDownloadCompletes
            || current.DownloadHistoryCap != restored.DownloadHistoryCap
            || current.CollisionPolicy != restored.CollisionPolicy)
            changes.Add("downloads");
        if (current.UpdateChannel != restored.UpdateChannel
            || current.UpdateCheckFrequency != restored.UpdateCheckFrequency
            || current.CheckForUpdates != restored.CheckForUpdates
            || current.ShowWhatsNew != restored.ShowWhatsNew
            || current.LastUpdateCheckUtc != restored.LastUpdateCheckUtc
            || current.UpdateSnoozedUntilUtc != restored.UpdateSnoozedUntilUtc
            || current.LastSeenVersion != restored.LastSeenVersion
            || current.KeptInstaller != restored.KeptInstaller
            || current.PreviousInstaller != restored.PreviousInstaller)
            changes.Add("updates");
        if (current.PasswordAutosaveEnabled != restored.PasswordAutosaveEnabled
            || current.GeneralAutofillEnabled != restored.GeneralAutofillEnabled
            || current.AppLockEnabled != restored.AppLockEnabled
            || current.AppLockSalt != restored.AppLockSalt || current.AppLockHash != restored.AppLockHash
            || current.AppLockIterations != restored.AppLockIterations
            || current.AppLockOnTrayHide != restored.AppLockOnTrayHide
            || current.AppLockIdleMinutes != restored.AppLockIdleMinutes
            || current.ClipboardClearMinutes != restored.ClipboardClearMinutes
            || current.AppLockWasRepaired != restored.AppLockWasRepaired)
            changes.Add("privacy and security");
        if (current.ReduceMotion != restored.ReduceMotion
            || current.SpeakStatusMessages != restored.SpeakStatusMessages
            || current.ExtendedZoomRange != restored.ExtendedZoomRange
            || current.ShortcutOverrides.Count != restored.ShortcutOverrides.Count
            || !current.ShortcutOverrides.OrderBy(p => p.Key).SequenceEqual(restored.ShortcutOverrides.OrderBy(p => p.Key)))
            changes.Add("keyboard and accessibility");
        if (current.ZoomFactor != restored.ZoomFactor || current.ExtendedZoomRange != restored.ExtendedZoomRange)
            changes.Add("zoom");
        return new SettingsRestorePreview(changes, restored.Profiles.Select(p => p.Name).ToList());
    }

    private static bool PlacementsEqual(AppSettings left, AppSettings right)
    {
        if (left.WindowPlacementByProfile.Count != right.WindowPlacementByProfile.Count)
            return false;
        foreach (var (id, placement) in left.WindowPlacementByProfile)
        {
            if (!right.WindowPlacementByProfile.TryGetValue(id, out var other)
                || placement.Left != other.Left || placement.Top != other.Top
                || placement.Width != other.Width || placement.Height != other.Height
                || placement.Maximized != other.Maximized)
                return false;
        }
        return true;
    }
}
