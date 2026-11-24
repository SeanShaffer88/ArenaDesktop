using System.Text;

namespace ArenaDesktop.Core.Rendering;

/// <summary>
/// Incremental parser for streamed AI responses. Feed chunks of ANY size —
/// including a fence split as "`" + "``csharp\n" — and it emits block events.
/// Invariant (tested): chunked parsing == whole-string parsing, block for block.
/// Handles ``` / ~~~ fences, widget fences, CRLF, and unterminated fences
/// (closed by <see cref="Complete"/>). A line is only classified once its
/// newline arrives, so a partial fence is never guessed at.
/// </summary>
public sealed class StreamParser
{
    private readonly StringBuilder _pending = new();
    private readonly StringBuilder _current = new();
    private readonly List<RenderBlock> _done = new();
    private readonly List<RenderEvent> _events = new();
    private int _seq;
    private string _openId = "";
    private bool _openIsFence;
    private bool _inFence;
    private char _fenceChar = '`';
    private int _fenceLen;
    private string _fenceInfo = "";
    private bool _completed;

    public IReadOnlyList<RenderEvent> Append(string chunk)
    {
        if (_completed) throw new InvalidOperationException("Stream already completed.");
        if (!string.IsNullOrEmpty(chunk))
            _pending.Append(chunk.Replace("\r\n", "\n").Replace('\r', '\n'));
        ProcessLines();
        return Drain();
    }

    public RenderDocument Complete()
    {
        if (_completed) return Snapshot();
        _completed = true;
        // Flush any partial last line through the same line classifier.
        if (_pending.Length > 0)
        {
            var last = _pending.ToString(); _pending.Clear();
            HandleLine(last, isFinalLine: true);
        }
        CloseCurrent();
        var doc = Snapshot();
        _events.Add(new StreamCompleted(doc.Blocks));
        return doc;
    }

    public RenderDocument Snapshot()
    {
        var blocks = new List<RenderBlock>(_done);
        if (_openId.Length > 0) blocks.Add(MakeBlock(complete: false));
        return new RenderDocument(blocks, _completed && _openId.Length == 0);
    }

    private IReadOnlyList<RenderEvent> Drain() { var e = _events.ToArray(); _events.Clear(); return e; }

    private void ProcessLines()
    {
        while (true)
        {
            var text = _pending.ToString();
            var nl = text.IndexOf('\n');
            if (nl < 0) return; // hold partial line — may be a fence
            var line = text[..nl];
            _pending.Remove(0, nl + 1);
            HandleLine(line, isFinalLine: false);
        }
    }

    private void HandleLine(string line, bool isFinalLine)
    {
        if (!_inFence)
        {
            if (TryOpenFence(line, out var info, out var ch, out var len))
            {
                CloseCurrent();
                _inFence = true; _fenceChar = ch; _fenceLen = len; _fenceInfo = info;
                _openId = NextId(); _openIsFence = true; _current.Clear();
                _events.Add(new BlockStarted(_openId, MakeBlock(false)));
                return;
            }
            if (_openId.Length == 0) { _openId = NextId(); _openIsFence = false; _events.Add(new BlockStarted(_openId, MakeBlock(false))); }
            _current.Append(line); if (!isFinalLine) _current.Append('\n');
            _events.Add(new BlockUpdated(_openId, MakeBlock(false)));
        }
        else if (IsCloseFence(line))
        {
            CloseCurrent(); _inFence = false;
        }
        else
        {
            _current.Append(line); if (!isFinalLine) _current.Append('\n');
            _events.Add(new BlockUpdated(_openId, MakeBlock(false)));
        }
    }

    private static bool TryOpenFence(string line, out string info, out char ch, out int len)
    {
        info = ""; ch = '`'; len = 0;
        var t = line.TrimStart();
        if (t.Length < 3 || (t[0] != '`' && t[0] != '~')) return false;
        ch = t[0]; var i = 0; while (i < t.Length && t[i] == ch) i++;
        if (i < 3) return false;
        if (ch == '`' && t[i..].Contains('`')) return false;
        len = i; info = t[i..].Trim(); return true;
    }

    private bool IsCloseFence(string line)
    {
        var t = line.Trim();
        return t.Length >= _fenceLen && t.Length > 0 && t.All(c => c == _fenceChar);
    }

    private RenderBlock MakeBlock(bool complete)
    {
        var body = _current.ToString();
        if (_openIsFence)
        {
            if (IsWidgetInfo(_fenceInfo, out var type, out var attrs))
                return new WidgetBlock(_openId, type, attrs, body.TrimEnd('\n'), complete);
            var (lang, file) = CodeBlock.ParseInfoString(_fenceInfo);
            return new CodeBlock(_openId, lang, file, body.TrimEnd('\n'), complete);
        }
        return new TextBlock(_openId, body, complete);
    }

    private void CloseCurrent()
    {
        if (_openId.Length == 0) { _current.Clear(); return; }
        if (!_openIsFence && _current.Length == 0) { _openId = ""; return; } // no empty text blocks
        var block = MakeBlock(complete: true);
        if (block is TextBlock tb) block = tb with { Raw = tb.Raw.TrimEnd('\n') };
        if (block is CodeBlock cb) block = cb with { Code = cb.Code.TrimEnd('\n') };
        _done.Add(block);
        _events.Add(new BlockCompleted(_openId, block));
        _openId = ""; _openIsFence = false; _current.Clear(); _fenceInfo = "";
    }

    internal static bool IsWidgetInfo(string info, out string type, out IReadOnlyDictionary<string, string> attrs)
    {
        type = ""; var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); attrs = d;
        var t = info.Trim();
        if (!t.StartsWith("widget", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = t["widget".Length..].TrimStart(':', ' ');
        var parts = rest.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        type = parts[0].Trim().ToLowerInvariant();
        if (parts.Length > 1)
            foreach (var tok in parts[1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            { var kv = tok.Split('=', 2); if (kv.Length == 2) d[kv[0]] = kv[1].Trim('"', '\''); }
        return type.Length > 0;
    }

    private string NextId() => $"b{++_seq}";
}
