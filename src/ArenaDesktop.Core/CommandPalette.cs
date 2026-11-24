namespace ArenaDesktop.Core;

// ───────────────────────── the command palette (v0.10.0) ─────────────────────────

/// <summary>
/// One command the palette can run (v0.10.0): a stable id the shell
/// dispatches on, the words the user reads, the group it sits in, and
/// the extra words that should find it. <see cref="ShortcutId"/> names
/// the entry in <see cref="ShortcutCatalog"/> whose (possibly remapped)
/// keys the palette prints beside the row — null when the command has
/// no key of its own, which is most of them.
/// </summary>
public sealed record PaletteCommand(
    string Id,
    string Title,
    string Group,
    string Keywords,
    string? ShortcutId = null);

/// <summary>
/// The command palette's catalogue and search rule (v0.10.0).
///
/// The palette exists because the app grew menus: thirty-odd commands is
/// fine to browse once and slow to browse every day. It is a keyboard
/// surface first — Ctrl+K, type a few letters, Enter — and it is also the
/// reason the layout cycle finally has a home: a command that is useful
/// but does not deserve a menu item or a global key (see DECISIONS.md).
///
/// Everything here is data and one search rule; the shell owns what each
/// id actually does. The search rule is deliberately the same
/// prefix-match-every-term rule Settings, Help and the shortcut sheet
/// use, so one habit works in all four places. The catalogue is built
/// from <see cref="ArenaModes"/> rather than restating the nine modes,
/// so a mode added there appears in the palette the same day.
/// </summary>
public static class CommandPalette
{
    /// <summary>The id prefix for a "go to this Arena mode" command; the rest of the id is the mode's id.</summary>
    public const string ModePrefix = "mode:";

    /// <summary>The id prefix for a zoom-preset command; the rest of the id is the preset's id in <see cref="ZoomPresets"/>.</summary>
    public const string ZoomPresetPrefix = "zoom-preset:";

    /// <summary>The groups, in the order the palette lists them.</summary>
    public static readonly IReadOnlyList<string> GroupOrder = new[]
    {
        "Arena modes", "Open", "View", "Navigate", "Zoom", "Page",
    };

    /// <summary>Every command the palette offers, in catalogue order (modes first, then the surfaces people reach for most).</summary>
    public static readonly IReadOnlyList<PaletteCommand> All = Build();

    private static IReadOnlyList<PaletteCommand> Build()
    {
        var commands = new List<PaletteCommand>();
        // The nine modes come from the registry, so the palette can never
        // fall out of step with Ctrl+1…Ctrl+9, the menu or the tray.
        foreach (var mode in ArenaModes.All)
        {
            commands.Add(new PaletteCommand(
                ModePrefix + mode.Id,
                $"Go to {mode.Label}",
                "Arena modes",
                $"{mode.Label} {mode.Id} {mode.Note}",
                $"mode-{mode.Id}"));
        }

        commands.AddRange(new[]
        {
            // ── Open: the windows and surfaces ──
            new PaletteCommand("settings", "Open Settings", "Open",
                "settings preferences options configure startup downloads privacy appearance layout shortcuts notifications",
                ShortcutCatalog.SettingsId),
            new PaletteCommand("downloads", "Open Downloads", "Open",
                "downloads files history pause resume cancel open folder",
                ShortcutCatalog.DownloadsId),
            new PaletteCommand("battle-lab-pane", "Show or hide the Battle Lab pane", "Open",
                "battle lab prompts library queue journal results pane side",
                "side-pane"),
            new PaletteCommand("prompt-pad", "Open the prompt pad", "Open",
                "prompt pad scratchpad draft jot write copy paste one prompt",
                "prompt-pad"),
            new PaletteCommand("battle-next-queue", "Copy the next queued prompt", "Open",
                "battle lab queue next run prompt copy automate one by one"),
            new PaletteCommand("profile-backup", "Back up profile (encrypted)", "Open",
                "profile backup encrypted export passphrase settings preferences"),
            new PaletteCommand("profile-restore", "Restore profile backup", "Open",
                "profile restore backup import encrypted passphrase settings"),
            new PaletteCommand("script-commands", "Manage script commands", "Open",
                "scripts commands automation plugins extensions manage custom"),
            new PaletteCommand("notifications", "Open Notifications", "Open",
                "notifications centre unread downloads tasks events alerts"),
            new PaletteCommand("library", "Open Library search & export", "Open",
                "library search export prompts journal downloads archive markdown json"),
            new PaletteCommand("git-panel", "Open the Git panel", "Open",
                "git status diff stage commit push pull branch repository project"),
            new PaletteCommand("terminal", "Open the terminal", "Open",
                "terminal shell command prompt run build project folder console"),
            new PaletteCommand("scratch-compare", "Open Scratch comparison", "Open",
                "scratch compare comparison side by side models byok local prompt test"),
            new PaletteCommand("providers", "Open AI providers (BYOK)", "Open",
                "byok api key openai anthropic google ollama lm studio local model providers"),
            new PaletteCommand("field-pass", "Open the field pass", "Open",
                "field pass checks verify prove tour guided problem skip verdicts"),
            new PaletteCommand("shortcuts", "Keyboard shortcuts", "Open",
                "shortcuts keys keyboard list cheat sheet bindings",
                "shortcuts"),
            new PaletteCommand("diagnostics", "Open Diagnostics", "Open",
                "diagnostics versions runtime health memory startup routes report"),
            new PaletteCommand("session", "This session…", "Open",
                "session stats time pages jumps downloads copied summary"),
            new PaletteCommand("help", "Help & troubleshooting", "Open",
                "help troubleshooting problem fix guide offline pages support"),
            new PaletteCommand("check-updates", "Check for updates", "Open",
                "update upgrade release newer version check"),
            // v0.11.0: the rest of the ⋯ menu. A command that is only in
            // the palette would be a new capability; these are the same
            // doors, finally reachable from one box.
            new PaletteCommand("quick-ask", "Quick ask — focus the page", "Open",
                "quick ask dictate type ready focus prompt box start typing voice"),
            new PaletteCommand("connection-security", "Connection security…", "Open",
                "connection security certificate tls https encryption address verified lock"),
            new PaletteCommand("view-log", "View the log…", "Open",
                "log viewer errors warnings info redaction read diagnostics tail"),
            new PaletteCommand("open-log-folder", "Open the log folder", "Open",
                "log folder explorer files open directory"),
            new PaletteCommand("export-support-bundle", "Export a support bundle…", "Open",
                "support bundle zip export diagnostics report redacted attach"),
            new PaletteCommand("about", "About ArenaDesktop", "Open",
                "about version channel architecture licenses credits build"),
            // Availability is the shell's call (Core has no idea whether a
            // lock is set), which is what the filter on Search is for.
            new PaletteCommand("lock-now", "Lock ArenaDesktop", "Open",
                "lock password app lock privacy step away now secure"),

            // ── View: the window's own shape ──
            new PaletteCommand("focus-mode", "Toggle focus mode", "View",
                "focus mode hide toolbar status chrome reading full",
                "focus-mode"),
            new PaletteCommand("always-on-top", "Toggle always on top", "View",
                "always on top pin float window stays above"),
            new PaletteCommand("layout-standard", "Use the Standard layout", "View",
                "layout standard toolbar status bar classic"),
            new PaletteCommand("layout-compact", "Use the Compact Focus layout", "View",
                "layout compact focus slim pill corner"),
            new PaletteCommand("layout-left-rail", "Use the Left Rail layout", "View",
                "layout left rail side navigation slim"),
            new PaletteCommand("layout-cycle", "Cycle the window layout", "View",
                "layout cycle next rotate change standard compact rail"),
            new PaletteCommand("find", "Find in page", "View",
                "find search page text bar whole word match case",
                "find"),

            // ── Navigate ──
            new PaletteCommand("back", "Back", "Navigate",
                "back previous page history", "back"),
            new PaletteCommand("forward", "Forward", "Navigate",
                "forward next page history", "forward"),
            new PaletteCommand("home", "Go to Arena home", "Navigate",
                "home arena start beginning page", ShortcutCatalog.HomeId),
            new PaletteCommand("reload", "Reload the page", "Navigate",
                "reload refresh page again", "reload"),
            new PaletteCommand("restore-closed", "Restore the last closed page", "Navigate",
                "restore closed reopen tab page back", "restore-closed"),

            // ── Zoom ──
            new PaletteCommand("zoom-in", "Zoom in", "Zoom",
                "zoom in bigger larger scale", "zoom-in"),
            new PaletteCommand("zoom-out", "Zoom out", "Zoom",
                "zoom out smaller scale", "zoom-out"),
        });

        // The five named zoom levels come from Core's own preset list, so
        // the View menu and the palette can never offer different ones. The
        // 100% preset is the Ctrl+0 action, so it carries that key rather
        // than a duplicate "reset zoom" command.
        foreach (var preset in ZoomPresets.All)
        {
            // 100% is the one people reach for by another name ("reset",
            // "normal"), so it carries those words as well as the level.
            var isActual = preset.Id == "actual";
            commands.Add(new PaletteCommand(
                ZoomPresetId(preset),
                $"Zoom to {preset.Display}",
                "Zoom",
                isActual
                    ? $"zoom preset {preset.Display} level scale actual size reset normal default 100"
                    : $"zoom preset {preset.Display} level scale actual size",
                isActual ? "zoom-reset" : null));
        }

        commands.AddRange(new[]
        {
            // ── Page ──
            new PaletteCommand("print", "Print…", "Page",
                "print paper printer", "print"),
            new PaletteCommand("save-as-pdf", "Save as PDF…", "Page",
                "save pdf export document file print"),
            new PaletteCommand("copy-address", "Copy the page address", "Page",
                "copy address link url page current"),
            new PaletteCommand("open-in-browser", "Open in your browser", "Page",
                "open browser external default web page"),
            new PaletteCommand("open-in-new-window", "Open in a new window", "Page",
                "new window second monitor same profile another"),
        });

        return commands;
    }

    /// <summary>
    /// Commands matching <paramref name="query"/> (every term must
    /// prefix-match a word of the title, the group or the keywords), in
    /// catalogue order. A blank query lists everything — the palette then
    /// doubles as a browsable index, which is how people discover the
    /// commands they did not know existed.
    /// </summary>
    /// <summary>
    /// The commands that are available right now. <paramref name="isAvailable"/>
    /// is the shell's answer to "does this one apply?" — whether an app
    /// lock exists, whether a zoom level is inside the current range — so
    /// the palette can hide a command instead of offering one that would
    /// only refuse. Null means everything is available.
    /// </summary>
    public static IReadOnlyList<PaletteCommand> Available(Func<PaletteCommand, bool>? isAvailable) =>
        isAvailable is null ? All : All.Where(isAvailable).ToList();

    /// <summary>Available commands including caller-supplied extras (the Scripts family, slice D5).</summary>
    public static IReadOnlyList<PaletteCommand> Available(
        Func<PaletteCommand, bool>? isAvailable, IReadOnlyList<PaletteCommand>? extras) =>
        extras is null || extras.Count == 0
            ? Available(isAvailable)
            : Available(isAvailable).Concat(extras.Where(c => isAvailable?.Invoke(c) ?? true)).ToList();

    /// <summary>The palette id for a zoom preset (see <see cref="ZoomPresets"/>).</summary>
    public static string ZoomPresetId(ZoomPreset preset) => ZoomPresetPrefix + preset.Id;

    /// <summary>
    /// The zoom preset an id names, or null when it names none (a stale
    /// id from a hand-edited file is not a crash).
    /// </summary>
    public static ZoomPreset? ZoomPresetFor(string? id) =>
        id is not null && id.StartsWith(ZoomPresetPrefix, StringComparison.Ordinal)
            ? ZoomPresets.Find(id[ZoomPresetPrefix.Length..])
            : null;

    public static IReadOnlyList<PaletteCommand> Search(
        string? query, Func<PaletteCommand, bool>? isAvailable = null,
        IReadOnlyList<PaletteCommand>? extras = null)
    {
        var pool = Available(isAvailable, extras);
        if (string.IsNullOrWhiteSpace(query))
            return pool;
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Select(t => t.ToLowerInvariant())
                         .ToList();
        if (terms.Count == 0)
            return pool;
        return pool.Where(c => terms.All(t => Matches(c, t))).ToList();
    }

    private static bool Matches(PaletteCommand command, string term)
    {
        var haystack = (command.Title + " " + command.Group + " " + command.Keywords).ToLowerInvariant();
        return haystack.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                       .Any(word => word.StartsWith(term, StringComparison.Ordinal));
    }

    /// <summary>Matches grouped for display: (group title, commands), empty groups dropped, groups in <see cref="GroupOrder"/>.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<PaletteCommand> Commands)> Grouped(
        string? query, Func<PaletteCommand, bool>? isAvailable = null)
    {
        var matches = Search(query, isAvailable);
        return GroupOrder
            .Select(group => (Group: group, Commands: (IReadOnlyList<PaletteCommand>)matches
                .Where(c => c.Group == group).ToList()))
            .Where(g => g.Commands.Count > 0)
            .ToList();
    }

    /// <summary>The command with this id, or null (a stale id from a hand-edited file is not a crash).</summary>
    public static PaletteCommand? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// The line under the list: what the keys do when the box is empty,
    /// how many matched when it is not, and the plain refusal when
    /// nothing does. No exclamation marks and no "Oops" — a palette that
    /// finds nothing is information, not an error.
    /// </summary>
    public static string SummaryText(int shown, int total, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return $"{total} commands · ↑↓ to move · Enter runs · Esc closes";
        if (shown == 0)
            return $"No command matches “{query.Trim()}” — clear the box or try a mode name · Esc closes";
        return shown == 1
            ? $"1 of {total} commands · Enter runs it"
            : $"{shown} of {total} commands · Enter runs the highlighted one";
    }
}
