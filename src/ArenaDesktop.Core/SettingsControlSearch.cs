namespace ArenaDesktop.Core;

/// <summary>Word-prefix matching for the live Settings control index.</summary>
public static class SettingsControlSearch
{
    /// <summary>True when every query word prefixes at least one searchable control word.</summary>
    public static bool Matches(string? query, string? searchableText)
    {
        var terms = Words(query).ToList();
        if (terms.Count == 0)
            return false;
        var words = Words(searchableText).ToList();
        return terms.All(term => words.Any(word => word.StartsWith(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string> Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;
        var word = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) word.Append(char.ToLowerInvariant(ch));
            else if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }
        if (word.Length > 0) yield return word.ToString();
    }
}
