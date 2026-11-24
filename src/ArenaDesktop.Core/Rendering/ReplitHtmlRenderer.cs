using System.Text;
using System.Text.Json;

namespace ArenaDesktop.Core.Rendering;

/// <summary>
/// Renders a <see cref="RenderDocument"/> to one self-contained HTML page for
/// WebView2 (NavigateToString / SetVirtualHost). Security posture: all stream
/// content is escaped; widget payloads are data-attributes / text, never
/// script; html-preview widgets render in a sandboxed iframe with srcdoc.
/// </summary>
public static class ReplitHtmlRenderer
{
    public static string RenderDocument(RenderDocument doc, bool showStreamingCursor = true)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">")
          .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; frame-src 'self'\">")
          .Append("<style>").Append(ReplitTheme.Css).Append("</style></head><body>");
        foreach (var b in doc.Blocks) sb.Append(RenderBlock(b, showStreamingCursor));
        sb.Append("<script>").Append(ReplitTheme.BridgeJs).Append("</script></body></html>");
        return sb.ToString();
    }

    public static string RenderBlock(RenderBlock block, bool cursor = true) => block switch
    {
        TextBlock t => $"<div class=\"text{(t.IsComplete || !cursor ? "" : " cursor")}\" data-block=\"{t.Id}\">{SyntaxHighlighter.Escape(t.Raw)}</div>",
        CodeBlock c => RenderCode(c),
        WidgetBlock w => RenderWidget(w),
        _ => "",
    };

    private static string RenderCode(CodeBlock c)
    {
        var lines = c.Code.Length == 0 ? 1 : c.Code.Count(ch => ch == '\n') + 1;
        var gutter = string.Join("<br>", Enumerable.Range(1, lines));
        var title = c.FileName ?? $"main.{ExtensionFor(c.Language)}";
        return $"""
        <div class="codeblock{(c.IsComplete ? "" : " streaming")}" data-block="{c.Id}">
          <div class="cb-head"><span class="cb-file">{SyntaxHighlighter.Escape(title)}</span>
            <span class="cb-lang">{SyntaxHighlighter.Escape(c.Language)}</span><span class="cb-spacer"></span>
            <button class="cb-btn" data-action="run" title="Run (host decides)">▶ Run</button>
            <button class="cb-btn" data-action="copy" title="Copy code">Copy</button>
            <button class="cb-btn" data-action="expand" title="Expand">Expand</button></div>
          <div class="cb-body"><div class="cb-gutter">{gutter}</div>
            <pre class="cb-code"><code>{SyntaxHighlighter.ToHtml(c.Code, c.Language)}</code></pre></div>
        </div>
        """;
    }

    private static string RenderWidget(WidgetBlock w)
    {
        var head = $"<div class=\"widget\" data-block=\"{w.Id}\"><div class=\"widget-head\">{SyntaxHighlighter.Escape(w.WidgetType)} widget</div>";
        try
        {
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(w.Payload) ? "{}" : w.Payload);
            return w.WidgetType switch
            {
                // Sandboxed preview: srcdoc is attribute-escaped, sandbox has no allow-scripts by default.
                "html-preview" when json.RootElement.TryGetProperty("html", out var h)
                    => head + $"<iframe sandbox=\"\" srcdoc=\"{SyntaxHighlighter.Escape(h.GetString() ?? "")}\"></iframe></div>",
                "table" when json.RootElement.TryGetProperty("rows", out var rows) => head + RenderTable(rows) + "</div>",
                "chart" => head + $"<div data-chart=\"{SyntaxHighlighter.Escape(w.Payload)}\">Chart data ready — host renders chart.</div></div>",
                _ => head + $"<pre>{SyntaxHighlighter.Escape(w.Payload)}</pre></div>",
            };
        }
        catch (JsonException)
        {
            return head + $"<pre>{SyntaxHighlighter.Escape(w.Payload)}</pre></div>";
        }
    }

    private static string RenderTable(JsonElement rows)
    {
        var sb = new StringBuilder("<table class=\"w-table\">");
        foreach (var row in rows.EnumerateArray())
        {
            sb.Append("<tr>");
            foreach (var cell in row.EnumerateArray())
                sb.Append("<td>").Append(SyntaxHighlighter.Escape(cell.ToString())).Append("</td>");
            sb.Append("</tr>");
        }
        return sb.Append("</table>").ToString();
    }

    private static string ExtensionFor(string lang) => lang switch
    {
        "csharp" => "cs", "python" => "py", "javascript" => "js", "typescript" => "ts",
        "json" => "json", "html" => "html", "css" => "css", "bash" => "sh", _ => "txt",
    };
}
