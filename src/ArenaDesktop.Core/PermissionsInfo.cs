namespace ArenaDesktop.Core;

/// <summary>One row of the WebView2 permissions transparency table.</summary>
public sealed record PermissionExposure(string Feature, string Policy, string Revoke);

/// <summary>
/// Exactly which WebView2 capabilities Arena exposes, and how to
/// revoke them (completeness slice E). This model is the single
/// source for the Settings → Privacy table and SECURITY.md's
/// permissions section — it describes the policy UrlPolicy +
/// MainWindow.OnPermissionRequested actually enforce, and the
/// revoke paths that actually exist (the per-profile permissions
/// manager over WebPrivacyGateway, and clearing site data).
/// </summary>
public static class PermissionsTransparency
{
    public static readonly IReadOnlyList<PermissionExposure> Rows =
    [
        new("Microphone",
            "Auto-granted to https://arena.ai only (Arena's voice input). Denied to every other origin.",
            "Settings → Privacy & security → Permissions: set microphone for arena.ai to Block, or clear site data."),
        new("Notifications",
            "Auto-granted to https://arena.ai only. Denied to every other origin.",
            "Same permissions list: set notifications for arena.ai to Block. Windows notification settings also apply."),
        new("Camera",
            "Never auto-granted: the policy denies camera requests from every origin, including arena.ai.",
            "Nothing to revoke — no grant is ever made. A camera request is denied and counted in the privacy report."),
        new("Geolocation",
            "Never auto-granted: denied to every origin.",
            "Nothing to revoke — no grant is ever made."),
        new("Clipboard",
            "Page-driven inside the web view (the page's own clipboard API); the shell adds no clipboard access of its own beyond the copy actions you invoke (copy address, prompt pad, Battle Lab queue).",
            "Clipboard content is yours via Windows; the app keeps no clipboard history. Prompt pad / Battle Lab copies are listed in their own surfaces."),
        new("Downloads",
            "Files land only in the downloads folder you chose, under the collision policy you chose; WebView2's own download flyout is suppressed in favour of the app's Downloads window.",
            "Settings → Downloads chooses the folder and policy; the Downloads window holds the history (file names and paths, never source URLs)."),
        new("Password autosave & autofill",
            "WebView2's own features, on by default, stored inside the profile's WebView2 folder — shared by every window in that profile.",
            "Settings → Privacy & security toggles both off (stops new saves/fills); clear site data to remove what is already stored."),
    ];

    /// <summary>The table as plain text for Settings and docs.</summary>
    public static string RenderText() =>
        string.Join("\n\n", Rows.Select(r => $"{r.Feature}: {r.Policy}\nRevoke: {r.Revoke}"));
}
