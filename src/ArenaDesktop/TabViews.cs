using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ArenaDesktop;

/// <summary>
/// One tab's live WebView2 view (multi-view tabs —
/// docs/MULTI_VIEW_TABS.md). The shell creates the WPF control when
/// a tab is first shown; the CoreWebView2 is ensured lazily against
/// the profile's ONE shared environment, so every tab shares the
/// profile's sign-in. Per-view facts that used to be MainWindow
/// globals live here instead: the page a crash should restore, the
/// microphone-grant heuristic (S4 #35), and whether tray suspend
/// currently holds this view.
/// </summary>
internal sealed class TabView
{
    public required string TabId { get; init; }
    public required WebView2 Control { get; init; }

    public CoreWebView2? Core => Control.CoreWebView2;

    /// <summary>True once the view's core exists and its handlers are wired.</summary>
    public bool Ready { get; set; }

    /// <summary>The freshest in-app page this view showed — what recovery restores for THIS tab.</summary>
    public string? LastGoodUrl { get; set; }

    /// <summary>S4 #35, per view: a microphone grant on this page means a voice session may be live here.</summary>
    public bool MicGrantedThisSession { get; set; }

    /// <summary>True while tray suspend holds this view (it was offered and WebView2 accepted).</summary>
    public bool Suspended { get; set; }
}
