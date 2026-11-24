namespace ArenaDesktop.Core.Rendering;

/// <summary>
/// Facade the shell uses: feed it a token stream (from Arena / any provider),
/// get block events + a ready-to-display Replit-style HTML page back.
/// Usage:
///   var engine = new ArenaRenderEngine();
///   engine.BlockChanged += (_, e) => view.Update(e);
///   await engine.FeedAsync(stream);            // or engine.Append(chunk) per token
///   var html = engine.RenderHtml();
/// Threading: not thread-safe — call from one (UI / dispatcher) thread, like a text box.
/// </summary>
public sealed class ArenaRenderEngine
{
    private readonly StreamParser _parser = new();
    public event EventHandler<RenderEvent>? BlockChanged;
    public RenderDocument Document => _parser.Snapshot();

    public IReadOnlyList<RenderEvent> Append(string chunk)
    {
        var events = _parser.Append(chunk);
        foreach (var e in events) BlockChanged?.Invoke(this, e);
        return events;
    }

    public async Task<RenderDocument> FeedAsync(IAsyncEnumerable<string> stream, CancellationToken ct = default)
    {
        await foreach (var chunk in stream.WithCancellation(ct)) Append(chunk);
        return Complete();
    }

    public async Task<RenderDocument> FeedAsync(Stream utf8Stream, CancellationToken ct = default)
    {
        using var reader = new StreamReader(utf8Stream, leaveOpen: true);
        var buffer = new char[1024];
        int n;
        while ((n = await reader.ReadAsync(buffer, ct)) > 0) Append(new string(buffer, 0, n));
        return Complete();
    }

    public RenderDocument Complete()
    {
        var doc = _parser.Complete();
        BlockChanged?.Invoke(this, new StreamCompleted(doc.Blocks));
        return doc;
    }

    public string RenderHtml() => ReplitHtmlRenderer.RenderDocument(Document);
    public string RenderBlockHtml(RenderBlock block) => ReplitHtmlRenderer.RenderBlock(block);
}
