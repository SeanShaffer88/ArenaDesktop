using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

public class UrlPolicyTests
{
    [Theory]
    [InlineData("https://arena.ai/")]
    [InlineData("https://arena.ai/chat/abc")]
    [InlineData("https://www.arena.ai/")]
    [InlineData("https://chat.arena.ai/")]
    [InlineData("https://accounts.google.com/signin")]
    [InlineData("https://lmarena.ai/")]
    [InlineData("about:blank")]
    public void NewWindow_InApp_StaysInApp(string url) =>
        Assert.Equal(LinkDisposition.InApp, UrlPolicy.ClassifyNewWindow(url));

    [Theory]
    [InlineData("https://example.com/article")]
    [InlineData("http://example.com/article")]
    [InlineData("https://www.nytimes.com/")]
    [InlineData("https://github.com/SeanShaffer88")]
    public void NewWindow_OrdinaryWeb_GoesToBrowser(string url) =>
        Assert.Equal(LinkDisposition.ExternalBrowser, UrlPolicy.ClassifyNewWindow(url));

    [Theory]
    // Look-alike hosts must never count as in-app.
    [InlineData("https://arena.ai.evil.com/")]
    [InlineData("https://evilarena.ai/")]
    [InlineData("https://arena-ai.com/")]
    [InlineData("https://accounts.google.com.evil.com/")]
    public void NewWindow_LookAlikeHosts_GoToBrowser(string url) =>
        Assert.Equal(LinkDisposition.ExternalBrowser, UrlPolicy.ClassifyNewWindow(url));

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ms-settings:display")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a url")]
    public void NewWindow_DangerousOrInvalid_Blocked(string? url) =>
        Assert.Equal(LinkDisposition.Block, UrlPolicy.ClassifyNewWindow(url));

    [Theory]
    [InlineData("https://arena.ai/")]
    [InlineData("https://any-provider.example/oauth/callback")]
    [InlineData("blob:https://arena.ai/6f9619ff-8b86")]
    [InlineData("about:blank")]
    public void Navigation_HttpsFamily_Allowed(string url) =>
        Assert.Equal(LinkDisposition.InApp, UrlPolicy.ClassifyNavigation(url));

    [Theory]
    [InlineData("http://arena.ai/")] // plain HTTP is never a top-level page
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    public void Navigation_NonHttps_Blocked(string? url) =>
        Assert.Equal(LinkDisposition.Block, UrlPolicy.ClassifyNavigation(url));

    [Theory]
    [InlineData("ARENA.AI", true)]
    [InlineData("arena.ai.", true)] // trailing-dot FQDN form of the same host
    [InlineData("sub.arena.ai", true)]
    [InlineData("notarena.ai", false)]
    [InlineData("arena.ai.evil.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void InAppHost_Matching(string? host, bool expected) =>
        Assert.Equal(expected, UrlPolicy.IsInAppHost(host));

    [Theory]
    [InlineData("https://arena.ai/", "notifications", true)]
    [InlineData("https://arena.ai/chat/1", "microphone", true)]
    [InlineData("https://arena.ai/", "camera", false)]
    [InlineData("https://arena.ai/", "geolocation", false)]
    [InlineData("https://example.com/", "notifications", false)]
    [InlineData("http://arena.ai/", "notifications", false)]
    [InlineData(null, "notifications", false)]
    public void AutoGrant_OnlyArenaNotificationsAndMic(string? origin, string kind, bool expected) =>
        Assert.Equal(expected, UrlPolicy.IsAutoGrantedPermission(origin, kind));

    [Fact]
    public void RedactForLog_StripsQueryAndFragment()
    {
        var redacted = UrlPolicy.RedactForLog("https://arena.ai/chat/1?token=SECRET#frag");
        Assert.Equal("https://arena.ai/chat/1", redacted);
        Assert.DoesNotContain("SECRET", redacted);
    }
}
