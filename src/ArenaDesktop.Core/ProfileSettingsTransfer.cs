using System.Text.Json;

namespace ArenaDesktop.Core;

/// <summary>
/// A portable snapshot of one profile's ArenaDesktop preferences. It is
/// deliberately not a copy of WebView2's user-data directory: cookies,
/// sign-in state and site storage are never placed in this file.
/// </summary>
public sealed record ProfileSettingsSnapshot
{
    public int FormatVersion { get; init; } = 1;
    public string Name { get; init; } = "";
    public StartPageMode StartPage { get; init; }
    public string? CustomStartUrl { get; init; }
    public string? DownloadsFolder { get; init; }
    public string? DisplayColor { get; init; }
    public double? ZoomFactor { get; init; }
    public long DataCapBytes { get; init; }
}

/// <summary>
/// Export and restore the settings that belong to a single account
/// profile, without changing its identity or session state.
/// </summary>
public static class ProfileSettingsTransfer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static ProfileSettingsSnapshot Capture(AccountProfile profile) => new()
    {
        Name = profile.Name,
        StartPage = profile.StartPage,
        CustomStartUrl = profile.CustomStartUrl,
        DownloadsFolder = profile.DownloadsFolder,
        DisplayColor = profile.DisplayColor,
        ZoomFactor = profile.ZoomFactor,
        DataCapBytes = profile.DataCapBytes,
    };

    public static string Export(AccountProfile profile) =>
        JsonSerializer.Serialize(Capture(profile), JsonOptions);

    /// <summary>Parse an export and reject unsupported or unsafe profile settings.</summary>
    public static bool TryImport(string? json, out ProfileSettingsSnapshot? snapshot, out string? error)
    {
        snapshot = null;
        error = null;
        try
        {
            snapshot = JsonSerializer.Deserialize<ProfileSettingsSnapshot>(json ?? "", JsonOptions);
        }
        catch (JsonException)
        {
            error = "The profile settings file is not valid JSON.";
            return false;
        }
        if (snapshot is null)
        {
            error = "The profile settings file is empty.";
            return false;
        }
        if (snapshot.FormatVersion != 1)
        {
            error = "This profile settings file uses an unsupported format version.";
            snapshot = null;
            return false;
        }
        if (string.IsNullOrWhiteSpace(snapshot.Name) || snapshot.Name.Trim().Length > 40)
        {
            error = "The profile name must be between 1 and 40 characters.";
            snapshot = null;
            return false;
        }
        if (!Enum.IsDefined(snapshot.StartPage))
        {
            error = "The start-page choice is not recognized.";
            snapshot = null;
            return false;
        }
        if (snapshot.CustomStartUrl is { Length: > 2048 })
        {
            error = "The custom start page is longer than the supported limit.";
            snapshot = null;
            return false;
        }
        if (snapshot.DisplayColor is not null && ProfileColors.Sanitize(snapshot.DisplayColor) is null)
        {
            error = "The profile color is not one of the supported colors.";
            snapshot = null;
            return false;
        }
        if (snapshot.ZoomFactor is { } zoom && (!double.IsFinite(zoom) || zoom is < 0.25 or > 5.0))
        {
            error = "The profile zoom is outside the supported range.";
            snapshot = null;
            return false;
        }
        if (snapshot.DataCapBytes != ProfileDataCaps.Sanitize(snapshot.DataCapBytes))
        {
            error = "The profile data cap is not one of the supported choices.";
            snapshot = null;
            return false;
        }
        snapshot = snapshot with
        {
            Name = snapshot.Name.Trim(),
            CustomStartUrl = snapshot.StartPage == StartPageMode.Custom ? snapshot.CustomStartUrl!.Trim() : null,
            DownloadsFolder = string.IsNullOrWhiteSpace(snapshot.DownloadsFolder) ? null : snapshot.DownloadsFolder.Trim(),
            DisplayColor = ProfileColors.Sanitize(snapshot.DisplayColor),
        };
        return true;
    }

    /// <summary>
    /// Apply preferences to an existing profile. Its id, sign-in session,
    /// last-used time and cleanup history deliberately remain untouched.
    /// </summary>
    public static void Apply(ProfileSettingsSnapshot snapshot, AccountProfile target)
    {
        target.Name = snapshot.Name;
        target.StartPage = snapshot.StartPage;
        target.StartAtHome = snapshot.StartPage == StartPageMode.Home;
        target.CustomStartUrl = snapshot.CustomStartUrl;
        target.DownloadsFolder = snapshot.DownloadsFolder;
        target.DisplayColor = snapshot.DisplayColor;
        target.ZoomFactor = snapshot.ZoomFactor;
        target.DataCapBytes = snapshot.DataCapBytes;
    }
}
