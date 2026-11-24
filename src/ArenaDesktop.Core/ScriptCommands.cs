namespace ArenaDesktop.Core;

/// <summary>
/// One user-defined script command (completeness slice D5): a name
/// and the command line it runs, in the project folder (or its own
/// folder when set). This is the plugin surface: local automation
/// the user writes for themselves — no third-party code is ever
/// loaded into the app process.
/// </summary>
public sealed class ScriptCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string? WorkingFolder { get; set; }
}

/// <summary>Script-command registry: sanitization + the palette family they generate.</summary>
public static class ScriptCommandCatalog
{
    public const string Prefix = "script:";
    public const string Group = "Scripts";
    public const int MaxCommands = 32;

    public static List<ScriptCommand> Sanitize(List<ScriptCommand>? commands)
    {
        var result = new List<ScriptCommand>();
        if (commands is null) return result;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>();
        foreach (var c in commands)
        {
            if (c is null || result.Count >= MaxCommands) continue;
            c.Name = (c.Name ?? "").Trim();
            c.Command = (c.Command ?? "").Trim();
            if (c.Name.Length == 0 || c.Command.Length == 0 || !names.Add(c.Name)) continue;
            if (string.IsNullOrWhiteSpace(c.Id) || !ids.Add(c.Id)) c.Id = Guid.NewGuid().ToString("n");
            ids.Add(c.Id);
            if (string.IsNullOrWhiteSpace(c.WorkingFolder)) c.WorkingFolder = null;
            else c.WorkingFolder = c.WorkingFolder.Trim();
            result.Add(c);
        }
        return result;
    }

    /// <summary>The generated palette family — one command per script, titled with its full command text so Enter is never a surprise.</summary>
    public static IReadOnlyList<PaletteCommand> ToPaletteCommands(IReadOnlyList<ScriptCommand> commands) =>
        commands.Select(c => new PaletteCommand(
            Prefix + c.Id,
            $"Run: {c.Name} — {c.Command}",
            Group,
            $"script run automation {c.Name} {c.Command}")).ToList();

    public static ScriptCommand? ForPaletteId(IReadOnlyList<ScriptCommand> commands, string? paletteId) =>
        paletteId is not null && paletteId.StartsWith(Prefix, StringComparison.Ordinal)
            ? commands.FirstOrDefault(c => c.Id == paletteId[Prefix.Length..])
            : null;
}
