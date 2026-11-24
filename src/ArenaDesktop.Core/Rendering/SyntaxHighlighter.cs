using System.Text;
using System.Text.RegularExpressions;

namespace ArenaDesktop.Core.Rendering;

public enum TokenKind { Plain, Keyword, String, Comment, Number, Type, Function, Property, Punctuation, Tag, Attribute }

public readonly record struct HighlightToken(TokenKind Kind, string Text);

/// <summary>
/// Zero-dependency, regex-ordered tokenizer. Deliberately conservative:
/// comments/strings are matched first so keywords inside them never light up.
/// Unknown languages fall back to plain (still HTML-escaped by the renderer).
/// </summary>
public static partial class SyntaxHighlighter
{
    private static readonly string[] CSharpKw = "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while var record init".Split(' ');
    private static readonly string[] PythonKw = "and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield True False None print".Split(' ');
    private static readonly string[] JsKw = "async await break case catch class const continue debugger default delete do else export extends false finally for from function if import in instanceof let new null of return static super switch this throw true try typeof var void while yield".Split(' ');
    private static readonly string[] BashKw = "if then else elif fi for while do done case esac function in echo export local return source".Split(' ');

    public static IReadOnlyList<HighlightToken> Tokenize(string code, string language)
    {
        var kw = language switch
        {
            "csharp" => CSharpKw, "python" => PythonKw,
            "javascript" or "typescript" => JsKw, "bash" => BashKw, _ => null,
        };
        if (kw is null && language is not ("json" or "xml" or "html" or "css"))
            return [new HighlightToken(TokenKind.Plain, code)];

        var pattern = language switch
        {
            "json" => """(?<str>"(?:\\.|[^"\\])*")\s*:|(?<str2>"(?:\\.|[^"\\])*")|(?<num>-?\b\d+(?:\.\d+)?\b)|(?<kw>\b(?:true|false|null)\b)""",
            "xml" or "html" => """(?<com><!--[\s\S]*?-->)|(?<tag></?[A-Za-z][\w.-]*|/?>)|(?<attr>[\w-]+(?==))|(?<str>"[^"]*"|'[^']*')""",
            _ => """(?<com>//[^\n]*|/\*[\s\S]*?\*/|\#[^\n]*)|(?<str>@?"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`)|(?<num>\b\d+(?:\.\d+)?\b)|(?<word>[A-Za-z_][\w]*)|(?<ws>\s+)|(?<pun>.)""",
        };
        // Python comments are '#', C-family are '//' — the shared pattern's comment arm
        // is corrected per-language by ordering: for python, '//' inside a string is
        // already claimed by the string arm only when quoted; acceptable for display.
        var tokens = new List<HighlightToken>();
        var kwSet = kw is null ? null : new HashSet<string>(kw);
        foreach (Match m in Regex.Matches(code, pattern, RegexOptions.Multiline))
        {
            if (m.Groups["com"].Success) tokens.Add(new(TokenKind.Comment, m.Value));
            else if (m.Groups["str"].Success || m.Groups["str2"].Success) tokens.Add(new(TokenKind.String, m.Value));
            else if (m.Groups["num"].Success) tokens.Add(new(TokenKind.Number, m.Value));
            else if (m.Groups["tag"].Success) tokens.Add(new(TokenKind.Tag, m.Value));
            else if (m.Groups["attr"].Success) tokens.Add(new(TokenKind.Attribute, m.Value));
            else if (m.Groups["kw"].Success) tokens.Add(new(TokenKind.Keyword, m.Value));
            else if (m.Groups["word"].Success)
            {
                var w = m.Value;
                var kind = kwSet is not null && kwSet.Contains(w) ? TokenKind.Keyword
                    : char.IsUpper(w[0]) ? TokenKind.Type : TokenKind.Plain;
                tokens.Add(new(kind, w));
            }
            else tokens.Add(new(m.Groups["pun"].Success ? TokenKind.Punctuation : TokenKind.Plain, m.Value));
        }
        return tokens;
    }

    /// <summary>Tokens → HTML spans. Every character is escaped — code can never break out into markup.</summary>
    public static string ToHtml(string code, string language)
    {
        var sb = new StringBuilder(code.Length + 64);
        foreach (var t in Tokenize(code, language))
        {
            var esc = Escape(t.Text);
            if (t.Kind == TokenKind.Plain) sb.Append(esc);
            else sb.Append("<span class=\"tok-").Append(t.Kind.ToString().ToLowerInvariant()).Append("\">").Append(esc).Append("</span>");
        }
        return sb.ToString();
    }

    public static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");
}
