using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

public class ProfileSettingsTransferTests
{
    [Fact]
    public void ExportImport_RoundTripsPreferencesWithoutChangingIdentityOrSession()
    {
        var source = new AccountProfile
        {
            Id = "work", Name = "Work", StartPage = StartPageMode.Custom,
            CustomStartUrl = "https://arena.ai/leaderboard", DownloadsFolder = "C:\\WorkDownloads",
            DisplayColor = "Blue", ZoomFactor = 1.25, DataCapBytes = ProfileDataCaps.Gb1,
            LastUsedUtc = DateTimeOffset.UtcNow,
            LastCacheCleanupUtc = DateTimeOffset.UtcNow.AddDays(-1), LastCacheCleanupFreedBytes = 123,
        };
        var json = ProfileSettingsTransfer.Export(source);
        Assert.DoesNotContain("LastUsedUtc", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(ProfileSettingsTransfer.TryImport(json, out var snapshot, out var error), error);
        Assert.NotNull(snapshot);

        var target = new AccountProfile
        {
            Id = "existing", Name = "Old name", LastUsedUtc = DateTimeOffset.UnixEpoch,
            LastCacheCleanupUtc = DateTimeOffset.UnixEpoch, LastCacheCleanupFreedBytes = 99,
        };
        ProfileSettingsTransfer.Apply(snapshot!, target);

        Assert.Equal("existing", target.Id);
        Assert.Equal(DateTimeOffset.UnixEpoch, target.LastUsedUtc!.Value);
        Assert.Equal(DateTimeOffset.UnixEpoch, target.LastCacheCleanupUtc!.Value);
        Assert.Equal(99L, target.LastCacheCleanupFreedBytes);
        Assert.Equal("Work", target.Name);
        Assert.Equal(StartPageMode.Custom, target.StartPage);
        Assert.Equal("https://arena.ai/leaderboard", target.CustomStartUrl);
        Assert.Equal("C:\\WorkDownloads", target.DownloadsFolder);
        Assert.Equal("Blue", target.DisplayColor);
        Assert.Equal(1.25, target.ZoomFactor!.Value);
        Assert.Equal(ProfileDataCaps.Gb1, target.DataCapBytes);
    }

    [Theory]
    [InlineData("{\"formatVersion\":99,\"name\":\"Work\"}", "unsupported format")]
    [InlineData("{\"formatVersion\":1,\"name\":\"Work\",\"displayColor\":\"#FF0000\"}", "supported colors")]
    public void Import_RejectsUnsupportedOrUnsafeValues(string json, string expectedError)
    {
        Assert.False(ProfileSettingsTransfer.TryImport(json, out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void Import_PreservesInvalidCustomPageForTheExistingSafeHomeFallback()
    {
        const string json = "{\"formatVersion\":1,\"name\":\"Work\",\"startPage\":2,\"customStartUrl\":\"https://example.com\"}";
        Assert.True(ProfileSettingsTransfer.TryImport(json, out var snapshot, out var error), error);
        Assert.NotNull(snapshot);
        Assert.Equal("https://example.com", snapshot!.CustomStartUrl);
        var resolved = StartPagePolicy.Resolve(new AccountProfile
        {
            StartPage = snapshot.StartPage,
            CustomStartUrl = snapshot.CustomStartUrl,
        }, lastUrl: null);
        Assert.True(resolved.CustomFellBack);
        Assert.Equal(UrlPolicy.HomeUrl, resolved.Url);
    }

    [Fact]
    public void Import_RejectsMalformedJson()
    {
        Assert.False(ProfileSettingsTransfer.TryImport("{", out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.Contains("valid JSON", error);
    }
}

public class SettingsRestorePreviewTests
{
    [Fact]
    public void Compare_NamesOnlyChangedAreasAndProfileNames()
    {
        var current = new AppSettings();
        current.Sanitized();
        var restored = current.Clone();
        restored.Theme = ThemeMode.Light;
        restored.DownloadsFolder = "D:\\Downloads";
        restored.Profiles[0].Name = "Personal";

        var preview = SettingsRestorePreview.Compare(current, restored);
        Assert.True(preview.HasChanges);
        Assert.Contains("appearance and toolbar", preview.ChangedAreas);
        Assert.Contains("downloads", preview.ChangedAreas);
        Assert.Contains("profile list, preferences or remembered pages", preview.ChangedAreas);
        Assert.Contains("Personal", preview.ProfileNames);
        Assert.Contains("Changed areas:", preview.Summary);
    }

    [Fact]
    public void Compare_IdenticalSettingsSaysThereAreNoChanges()
    {
        var current = new AppSettings();
        current.Sanitized();
        var preview = SettingsRestorePreview.Compare(current, current.Clone());
        Assert.False(preview.HasChanges);
        Assert.Equal("This backup has the same settings as the current window.", preview.Summary);
    }
}
