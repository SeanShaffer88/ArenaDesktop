using ArenaDesktop.Core.Rendering;
using Xunit;

namespace ArenaDesktop.Core.Tests;

public class RenderingTests
{
    private const string Sample = "Here is code:\n```csharp title=Program.cs\nvar x = 1; // hi\nConsole.WriteLine(\"<b>\");\n```\nDone.\n```widget:table\n{\"rows\":[[\"a\",\"b\"],[\"1\",\"2\"]]}\n```\n";

    private static RenderDocument ParseWhole(string s) { var p = new StreamParser(); p.Append(s); return p.Complete(); }
    private static RenderDocument ParseChunked(string s, int size)
    {
        var p = new StreamParser();
        for (var i = 0; i < s.Length; i += size) p.Append(s.Substring(i, Math.Min(size, s.Length - i)));
        return p.Complete();
    }

    [Fact] public void ChunkedEqualsWhole_EvenOneCharAtATime()
    {
        var whole = ParseWhole(Sample);
        foreach (var size in new[] { 1, 2, 3, 7, 64 })
            Assert.Equal(
                whole.Blocks.Select(b => (b.Kind, b.ToString())),
                ParseChunked(Sample, size).Blocks.Select(b => (b.Kind, b.ToString())));
    }

    [Fact] public void CodeBlock_ParsesLanguageAndFileName()
    {
        var code = Assert.IsType<CodeBlock>(ParseWhole(Sample).Blocks[1]);
        Assert.Equal("csharp", code.Language); Assert.Equal("Program.cs", code.FileName);
        Assert.Contains("Console.WriteLine", code.Code); Assert.True(code.IsComplete);
    }

    [Fact] public void WidgetBlock_IsDetectedWithPayload()
    {
        var w = Assert.IsType<WidgetBlock>(ParseWhole(Sample).Blocks[3]);
        Assert.Equal("table", w.WidgetType); Assert.Contains("\"rows\"", w.Payload);
    }

    [Fact] public void UnterminatedFence_IsClosedByComplete()
    {
        var doc = ParseWhole("text\n```python\nprint(1)");
        var code = Assert.IsType<CodeBlock>(doc.Blocks[1]);
        Assert.Equal("python", code.Language); Assert.Equal("print(1)", code.Code);
    }

    [Fact] public void SplitFenceAcrossChunks_IsNotText()
    {
        var p = new StreamParser(); p.Append("`"); p.Append("``csharp\nx\n```\n"); var doc = p.Complete();
        Assert.IsType<CodeBlock>(doc.Blocks[0]);
    }

    [Fact] public void Renderer_EscapesCodeAndNeverEmitsRawMarkup()
    {
        var html = ReplitHtmlRenderer.RenderDocument(ParseWhole(Sample));
        Assert.DoesNotContain("<b>", html); Assert.Contains("&lt;b&gt;", html);
        Assert.Contains("tok-keyword", html); Assert.Contains("cb-gutter", html);
        Assert.Contains("#0E1525", ReplitTheme.Css + html); Assert.Contains("data-action=\"run\"", html);
        Assert.Contains("<table", html); // widget table rendered
    }

    [Fact] public void Highlighter_DoesNotHighlightKeywordsInsideStrings()
    {
        var html = SyntaxHighlighter.ToHtml("\"class\" class", "csharp");
        Assert.Contains("<span class=\"tok-string\">&quot;class&quot;</span>", html);
        Assert.Contains("<span class=\"tok-keyword\">class</span>", html);
    }

    [Fact] public void InfoString_AliasesNormalise()
    {
        Assert.Equal(("csharp", "a.cs"), CodeBlock.ParseInfoString("cs:a.cs"));
        Assert.Equal(("javascript", null), CodeBlock.ParseInfoString("js"));
    }

    [Fact] public void HtmlPreviewWidget_IsSandboxedWithoutScripts()
    {
        var doc = ParseWhole("```widget:html-preview\n{\"html\":\"<p>hi</p>\"}\n```\n");
        var html = ReplitHtmlRenderer.RenderDocument(doc);
        Assert.Contains("sandbox=\"\"", html); Assert.DoesNotContain("allow-scripts", html);
    }
}
