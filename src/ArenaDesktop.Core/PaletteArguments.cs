using System.Globalization;

namespace ArenaDesktop.Core;

/// <summary>What an argumented palette query asks for (v0.14.0).</summary>
public enum PaletteArgumentKind
{
    /// <summary>The text is not an argumented query — search the catalogue as before.</summary>
    None,

    /// <summary><c>zoom 125</c> — set the page zoom to that percentage.</summary>
    Zoom,

    /// <summary><c>find cables</c> — open the find bar with that text in it.</summary>
    Find,

    /// <summary><c>profile Work</c> — switch to that profile.</summary>
    Profile,

    /// <summary><c>mode side</c> — go to that Arena mode (resolved to a mode id).</summary>
    Mode,

    /// <summary><c>saved cables</c> — find that saved prompt, copy it, open Battle.</summary>
    Prompt,
}

/// <summary>One parsed query: what kind, which argument, and the row the palette shows.</summary>
public sealed record PaletteQuery(PaletteArgumentKind Kind, string Argument, string Title, string Detail)
{
    /// <summary>True when this is a real query rather than a plain search.</summary>
    public bool IsArgumented => Kind != PaletteArgumentKind.None;
}

/// <summary>
/// Arguments for the command palette (v0.14.0).
///
/// Until now Ctrl+K ran commands and nothing else: "zoom 125" was a search for
/// a command with that name, and there is no such command — the palette could
/// only offer the five named levels. Four verbs change that, all of them
/// things the app already does by hand:
///
/// <code>
///   zoom 125          → the page at 125%
///   find cables       → the find bar, with "cables" in it
///   profile Work      → the Work profile
///   mode side         → Side by Side
///   saved cables      → the saved prompt, on the clipboard (v0.15.0)
/// </code>
///
/// The rule is the palette's own: a verb, a space, an argument. Anything else
/// is a search, so typing "zoom" alone still lists the zoom commands. Nothing
/// here navigates, types into the page, or touches Arena — the parsing is pure,
/// and the shell acts on the answer the same way it acts on a command id.
/// </summary>
public static class PaletteArguments
{
    /// <summary>The id prefix the palette reports for an argumented row; the rest is <c>kind:argument</c>.</summary>
    public const string IdPrefix = "arg:";

    /// <summary>The four verbs, in the order help should list them.</summary>
    public static readonly IReadOnlyList<string> Verbs = new[] { "zoom", "find", "profile", "mode", "saved" };

    /// <summary>
    /// Parse a query against the profiles this app knows (names and ids both
    /// work) and the Arena mode registry. Returns a query with
    /// <see cref="PaletteArgumentKind.None"/> for anything that is not a
    /// well-formed argumented request — a bad zoom ("zoom 900") is a search,
    /// not a silent clamp.
    /// </summary>
    public static PaletteQuery Parse(string? text, IReadOnlyList<AccountProfile>? profiles = null)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            return None();
        var space = trimmed.IndexOf(' ');
        if (space <= 0)
            return None();
        var verb = trimmed[..space].ToLowerInvariant();
        var argument = trimmed[(space + 1)..].Trim();
        if (argument.Length == 0)
            return None();

        return verb switch
        {
            "zoom" => ParseZoom(argument),
            "find" => new PaletteQuery(PaletteArgumentKind.Find, argument,
                $"Find “{Shorten(argument)}” on the page", "Opens the find bar with this text"),
            "profile" => ParseProfile(argument, profiles),
            "mode" => ParseMode(argument),
            // "saved", not "prompt": "prompt pad" must keep opening the pad,
            // and it does — the verb is a word no command title starts with.
            // The library is not known here (parsing stays pure), so the verb
            // only carries the words; PromptPick resolves them against the
            // library the shell hands it.
            "saved" => new PaletteQuery(PaletteArgumentKind.Prompt, argument,
                $"Copy the saved prompt “{Shorten(argument)}”", "Finds it in Battle Lab, copies it, opens Battle"),
            _ => None(),
        };
    }

    /// <summary>Rebuild a query from the id the palette reported (the shell never parses the id by hand).</summary>
    public static PaletteQuery? FromId(string? id)
    {
        if (id is null || !id.StartsWith(IdPrefix, StringComparison.Ordinal))
            return null;
        var rest = id[IdPrefix.Length..];
        var colon = rest.IndexOf(':');
        if (colon <= 0)
            return null;
        var kind = rest[..colon] switch
        {
            "zoom" => PaletteArgumentKind.Zoom,
            "find" => PaletteArgumentKind.Find,
            "profile" => PaletteArgumentKind.Profile,
            "mode" => PaletteArgumentKind.Mode,
            "saved" => PaletteArgumentKind.Prompt,
            _ => PaletteArgumentKind.None,
        };
        if (kind == PaletteArgumentKind.None)
            return null;
        var argument = rest[(colon + 1)..];
        return argument.Length == 0 ? null : new PaletteQuery(kind, argument, Title(kind, argument), "");
    }

    /// <summary>The id an argumented row reports; <see cref="FromId"/> is its inverse.</summary>
    public static string ToId(PaletteQuery query)
    {
        var kind = query.Kind switch
        {
            PaletteArgumentKind.Zoom => "zoom",
            PaletteArgumentKind.Find => "find",
            PaletteArgumentKind.Profile => "profile",
            PaletteArgumentKind.Mode => "mode",
            PaletteArgumentKind.Prompt => "saved",
            _ => throw new ArgumentOutOfRangeException(nameof(query), "Not an argumented query"),
        };
        return $"{IdPrefix}{kind}:{query.Argument}";
    }

    /// <summary>The one line of help the palette shows under an argumented row.</summary>
    public static string HelpText() =>
        "Arguments: zoom 125 · find cables · profile Work · mode side · saved cables";

    private static PaletteQuery ParseZoom(string argument)
    {
        var digits = argument.TrimEnd('%');
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
            return None();
        // The standard range, so the answer is always something the page can
        // actually do; the extended range is a step in Settings, not a thing to
        // guess at here.
        if (percent < (int)Math.Round(ZoomMath.MinFor(false) * 100)
            || percent > (int)Math.Round(ZoomMath.MaxFor(false) * 100))
            return None();
        return new PaletteQuery(PaletteArgumentKind.Zoom, percent.ToString(CultureInfo.InvariantCulture),
            $"Zoom to {percent}%", "Sets the page zoom for every profile");
    }

    private static PaletteQuery ParseProfile(string argument, IReadOnlyList<AccountProfile>? profiles)
    {
        if (profiles is null || profiles.Count == 0)
            return None();
        var match = profiles.FirstOrDefault(p =>
                        string.Equals(p.Name, argument, StringComparison.OrdinalIgnoreCase))
                    ?? profiles.FirstOrDefault(p =>
                        string.Equals(p.Id, argument, StringComparison.OrdinalIgnoreCase))
                    ?? profiles.FirstOrDefault(p =>
                        p.Name.StartsWith(argument, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? None()
            : new PaletteQuery(PaletteArgumentKind.Profile, match.Id,
                $"Switch to the {match.Name} profile", "Swaps the Arena sign-in this window shows");
    }

    private static PaletteQuery ParseMode(string argument)
    {
        var match = ArenaModes.All.FirstOrDefault(m =>
                        string.Equals(m.Id, argument, StringComparison.OrdinalIgnoreCase))
                    ?? ArenaModes.All.FirstOrDefault(m =>
                        m.Label.StartsWith(argument, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? None()
            : new PaletteQuery(PaletteArgumentKind.Mode, match.Id,
                $"Go to {match.Label}", "One jump to that Arena mode");
    }

    private static PaletteQuery None() => new(PaletteArgumentKind.None, "", "", "");

    private static string Title(PaletteArgumentKind kind, string argument) => kind switch
    {
        PaletteArgumentKind.Zoom => $"Zoom to {argument}%",
        PaletteArgumentKind.Find => $"Find “{Shorten(argument)}” on the page",
        PaletteArgumentKind.Profile => $"Switch to the {argument} profile",
        PaletteArgumentKind.Prompt => $"Copy the saved prompt “{Shorten(argument)}”",
        _ => $"Go to {argument}",
    };

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..39] + "…";
}
