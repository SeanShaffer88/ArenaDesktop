using System.Text.Json;
using ArenaDesktop.Core.Rendering;
using Microsoft.Web.WebView2.Core;

namespace ArenaDesktop.Rendering;

/// <summary>Host-side request from a rendered block's button. The host decides what Run means — Core never executes code.</summary>
public sealed record BlockActionRequest(string Action, string BlockId, string Code);

/// <summary>
/// WebView2 host for <see cref="ArenaRenderEngine"/>. Feed it the same stream
/// the chat shows; it re-renders the (small) document per completed block and
/// throttles in-flight updates to animation frames, so token-rate streams
/// don't thrash the browser.
/// </summary>
public partial class ArenaRenderView : System.Windows.Controls.UserControl
{
    private readonly ArenaRenderEngine _engine = new();
    private bool _ready;
    private bool _renderQueued;

    public event EventHandler<BlockActionRequest>? BlockActionRequested;
    public ArenaRenderEngine Engine => _engine;

    public ArenaRenderView()
    {
        InitializeComponent();
        _engine.BlockChanged += (_, e) => { if (e is BlockCompleted or StreamCompleted) QueueRender(); else QueueRender(); };
        Loaded += async (_, _) => await InitialiseAsync();
    }

    private async Task InitialiseAsync()
    {
        await Web.EnsureCoreWebView2Async();
        Web.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        Web.CoreWebView2.Settings.IsScriptEnabled = true;
        Web.CoreWebView2.WebMessageReceived += OnWebMessage;
        _ready = true;
        RenderNow();
    }

    public IReadOnlyList<RenderEvent> Append(string chunk) { var e = _engine.Append(chunk); QueueRender(); return e; }
    public async Task FeedAsync(IAsyncEnumerable<string> stream, CancellationToken ct = default)
    { await _engine.FeedAsync(stream, ct); RenderNow(); }
    public void CompleteStream() { _engine.Complete(); RenderNow(); }

    private void QueueRender()
    {
        if (!_ready || _renderQueued) return;
        _renderQueued = true;
        _ = Dispatcher.InvokeAsync(() => { _renderQueued = false; RenderNow(); },
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RenderNow() { if (_ready) Web.CoreWebView2.NavigateToString(_engine.RenderHtml()); }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc2 = JsonDocument.Parse(e.TryGetWebMessageAsString() ?? "{}");
            var r = doc2.RootElement;
            BlockActionRequested?.Invoke(this, new BlockActionRequest(
                r.GetProperty("type").GetString() ?? "",
                r.TryGetProperty("blockId", out var b) ? b.GetString() ?? "" : "",
                r.TryGetProperty("code", out var c) ? c.GetString() ?? "" : ""));
        }
        catch (JsonException) { /* untrusted page message — ignore malformed input */ }
        catch (InvalidOperationException) { /* message was not a string — ignore */ }
    }
}
