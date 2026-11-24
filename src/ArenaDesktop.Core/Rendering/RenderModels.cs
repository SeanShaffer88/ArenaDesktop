namespace ArenaDesktop.Core.Rendering;

/// <summary>Kind of a top-level block in a streamed response.</summary>
public enum RenderBlockKind { Text, Code, Widget, InlineHtmlPreview }

/// <summary>Base for all render blocks. Id is stable across streaming updates for the same block.</summary>
public abstract record RenderBlock(string Id, RenderBlockKind Kind, bool IsComplete);

/// <summary>Plain / markdown text outside any fence. Raw is untrusted — renderers must escape it.</summary>
public sealed record TextBlock(string Id, string Raw, bool IsComplete)
    : RenderBlock(Id, RenderBlockKind.Text, IsComplete);

/// <summary>
/// A fenced code block. Info string forms supported:
/// ```csharp  |  ```csharp title=Program.cs  |  ```python:main.py  |  ```{lang} file={name}
/// </summary>
public sealed record CodeBlock(
    string Id, string Language, string? FileName, string Code, bool IsComplete)
    : RenderBlock(Id, RenderBlockKind.Code, IsComplete)
{
    /// <summary>Normalised language id (lower-case, aliases folded: cs→csharp, js→javascript, ts→typescript, py→python, sh→bash).</summary>
    public static string NormaliseLanguage(string? lang) => (lang ?? "").Trim().ToLowerInvariant() switch
    {
        "cs" or "c#" => "csharp", "js" or "jsx" => "javascript", "ts" or "tsx" => "typescript",
        "py" => "python", "sh" or "shell" or "zsh" => "bash", "yml" => "yaml",
        "md" => "markdown", "" => "text", var x => x,
    };

    public static (string Language, string? FileName) ParseInfoString(string info)
    {
        info = info.Trim();
        if (info.Length == 0) return ("text", null);
        string? file = null;
        var first = info.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        var head = first[0];
        if (head.Contains(':') && !head.Contains("://"))
        { var p = head.Split(':', 2); head = p[0]; if (p[1].Length > 0) file = p[1]; }
        if (first.Length > 1)
            foreach (var tok in first[1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = tok.Split('=', 2);
                if (kv.Length == 2 && kv[0] is "title" or "file" or "filename")
                    file = kv[1].Trim('"', '\'');
            }
        return (NormaliseLanguage(head), file);
    }
}

/// <summary>
/// An embedded widget declared as ```widget:{type} or :::widget {type} {...json attrs}.
/// Payload is the raw body (usually JSON); it is data, never executed by Core.
/// </summary>
public sealed record WidgetBlock(
    string Id, string WidgetType, IReadOnlyDictionary<string, string> Attributes, string Payload, bool IsComplete)
    : RenderBlock(Id, RenderBlockKind.Widget, IsComplete);

/// <summary>Events emitted by <see cref="StreamParser"/> / <see cref="ArenaRenderEngine"/> as a stream arrives.</summary>
public abstract record RenderEvent(string BlockId);
public sealed record BlockStarted(string BlockId, RenderBlock Block) : RenderEvent(BlockId);
public sealed record BlockUpdated(string BlockId, RenderBlock Block) : RenderEvent(BlockId);
public sealed record BlockCompleted(string BlockId, RenderBlock Block) : RenderEvent(BlockId);
public sealed record StreamCompleted(IReadOnlyList<RenderBlock> Document) : RenderEvent("");

/// <summary>An immutable snapshot of everything parsed so far.</summary>
public sealed record RenderDocument(IReadOnlyList<RenderBlock> Blocks, bool IsComplete);
