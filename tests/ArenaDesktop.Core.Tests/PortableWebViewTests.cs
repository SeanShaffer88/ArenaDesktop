using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>Completeness slice A: portable mode carries the WebView2 profile.</summary>
public sealed class PortableWebViewTests
{
    [Fact]
    public void InstalledMode_KeepsTheLongStandingLocalAppDataLocation()
    {
        var location = SettingsLocation.Resolve(@"C:\Apps\ArenaDesktop", @"C:\Users\U\AppData\Roaming\ArenaDesktop", markerExists: false);
        var dir = location.WebViewProfileDir(@"C:\Users\U\AppData\Local", Profiles.DefaultId);
        Assert.Equal(Path.Combine(@"C:\Users\U\AppData\Local", "ArenaDesktop", "WebView2"), dir);
    }

    [Fact]
    public void InstalledMode_ExtraProfilesAreSiblings()
    {
        var location = SettingsLocation.Installed(@"C:\cfg");
        var dir = location.WebViewProfileDir(@"C:\Local", "work");
        Assert.Equal(Path.Combine(@"C:\Local", "ArenaDesktop", "WebView2-work"), dir);
    }

    [Fact]
    public void PortableMode_PutsProfilesUnderTheDataRoot()
    {
        var location = SettingsLocation.Resolve(@"C:\Stick\ArenaDesktop", @"C:\Roaming\ArenaDesktop", markerExists: true);
        Assert.Equal(
            Path.Combine(@"C:\Stick\ArenaDesktop", "data", "WebView2"),
            location.WebViewProfileDir(@"C:\Local", Profiles.DefaultId));
        Assert.Equal(
            Path.Combine(@"C:\Stick\ArenaDesktop", "data", "WebView2-work"),
            location.WebViewProfileDir(@"C:\Local", "work"));
    }
}
