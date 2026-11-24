# Arena Render Engine

Custom rendering engine for streamed responses in ArenaDesktop, styled after Replit's workspace UI.

## Pipeline
`stream chunks → StreamParser → RenderDocument (Text / Code / Widget blocks) → SyntaxHighlighter → ReplitHtmlRenderer → ArenaRenderView (WebView2)`

## Use
```csharp
var engine = new ArenaRenderEngine();
engine.BlockChanged += (_, e) => { /* e is BlockStarted / BlockUpdated / BlockCompleted / StreamCompleted */ };
await engine.FeedAsync(providerStream);   // any IAsyncEnumerable<string> or UTF-8 Stream
string html = engine.RenderHtml();        // self-contained, CSP-locked page
```
In XAML: `<rendering:ArenaRenderView x:Name="RenderView"/>`, then `RenderView.Append(chunk)` per token and handle `RenderView.BlockActionRequested` for Run/Expand. Copy is handled in-page. **Run is a host decision — the engine never executes streamed code.**

## Stream format
- Fenced code: ` ```csharp title=Program.cs `, ` ```python:main.py `, `~~~` fences also work. Aliases: cs/js/ts/py/sh/yml fold to canonical languages.
- Widgets: ` ```widget:table ` / ` ```widget:chart ` / ` ```widget:html-preview ` with a JSON body. html-preview renders in a script-less sandboxed iframe.
- Everything else is text. Unterminated fences are closed cleanly at end-of-stream.

## Guarantees
- Chunk-safe: parsing 1 char at a time gives the identical document to parsing whole (tested).
- Safe: all streamed content is HTML-escaped; keywords inside strings/comments are never highlighted; no remote fonts, scripts, or code execution.
- Zero new dependencies: parser, highlighter (csharp/python/javascript/typescript/json/html/css/bash), and theme are all in Core and unit-tested.
