namespace ArenaDesktop.Core;

/// <summary>
/// UI string externalization groundwork (completeness slice D4).
/// Historically every shell string was a hard-coded English literal;
/// this table is the pipeline new surfaces use: keys are looked up
/// for the requested culture, falling back to English, then to the
/// key itself (a missing string is visible, never blank). Only
/// English ships today — the table proves the mechanism and gives
/// future translations one home; claiming more would be fiction.
/// Existing windows migrate to the table surface by surface.
/// </summary>
public static class ShellStrings
{
    public const string DefaultCulture = "en";

    /// <summary>Cultures with a shipped table. Honest list: English only, for now.</summary>
    public static readonly IReadOnlyList<string> SupportedCultures = [DefaultCulture];

    private static readonly Dictionary<string, string> English = new()
    {
        ["tab.new"] = "New tab",
        ["tab.new.tip"] = "New tab (Ctrl+T)",
        ["tab.close"] = "Close tab",
        ["tab.close.tip"] = "Close tab (Ctrl+W)",
        ["status.newtab"] = "New tab",
        ["notifications.title"] = "Notifications",
        ["terminal.title"] = "Terminal — project folder",
        ["git.title"] = "Git — project folder",
        ["compare.title"] = "Scratch comparison",
        ["providers.title"] = "AI providers (bring your own key)",
        ["library.title"] = "Library — search & export",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase) { [DefaultCulture] = English };

    /// <summary>Look up a string: exact culture, then its language, then English, then the key.</summary>
    public static string Get(string? culture, string key)
    {
        if (!string.IsNullOrWhiteSpace(culture))
        {
            if (Tables.TryGetValue(culture.Trim(), out var exact) && exact.TryGetValue(key, out var hit))
                return hit;
            var language = culture.Trim().Split('-', '_')[0];
            if (Tables.TryGetValue(language, out var byLanguage) && byLanguage.TryGetValue(key, out var langHit))
                return langHit;
        }
        return English.TryGetValue(key, out var fallback) ? fallback : key;
    }

    /// <summary>Resolve a settings value ("", "system", a culture name) to a usable culture.</summary>
    public static string ResolveCulture(string? setting, string systemCulture)
    {
        if (string.IsNullOrWhiteSpace(setting) || setting.Equals("system", StringComparison.OrdinalIgnoreCase))
            return systemCulture;
        return setting.Trim();
    }

    /// <summary>Sanitize the UiLanguage setting: "", "system", or a supported culture name.</summary>
    public static string SanitizeUiLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var trimmed = value.Trim();
        if (trimmed.Equals("system", StringComparison.OrdinalIgnoreCase)) return "system";
        return SupportedCultures.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            ? SupportedCultures.First(c => c.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            : "";
    }
}
