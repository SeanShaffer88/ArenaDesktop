namespace ArenaDesktop.Core;

// ───────────────────────── simple mode ─────────────────────────
//
// A calmer shell for people who just want Arena: the advanced
// surfaces (Battle Lab, the prompt pad, the command palette) step
// aside entirely instead of merely looking disabled. The policy is
// Core so every door — the ⋯ menu, the tray, the keyboard, the
// command line and arenadesktop:// links — is judged by the same
// rule, and so the rule itself is unit-tested. The shell owns the
// hiding; Core owns the decision.

/// <summary>What Simple Mode does to one advanced surface.</summary>
public static class SimpleModePolicy
{
    /// <summary>The palette command ids Simple Mode removes: the surfaces it hides, plus the queue action that only makes sense with the Lab behind it.</summary>
    public static readonly IReadOnlyList<string> HiddenPaletteCommandIds = new[]
    {
        "battle-lab-pane",
        "prompt-pad",
        "battle-next-queue",
    };

    /// <summary>Battle Lab (pane, tray item, Ctrl+Shift+B, <c>--battle-lab</c>, <c>arenadesktop://battle-lab</c>) is available only outside Simple Mode.</summary>
    public static bool AllowsBattleLab(bool simpleMode) => !simpleMode;

    /// <summary>The prompt pad (menu, tray item, Ctrl+Shift+P, <c>--prompt-pad</c>, <c>arenadesktop://prompt-pad</c>) is available only outside Simple Mode.</summary>
    public static bool AllowsPromptPad(bool simpleMode) => !simpleMode;

    /// <summary>The command palette (Ctrl+K, menu, tray, <c>--palette</c>, <c>arenadesktop://palette</c>) is disabled entirely in Simple Mode.</summary>
    public static bool AllowsCommandPalette(bool simpleMode) => !simpleMode;

    /// <summary>Whether one palette command may be offered under the given mode. Outside Simple Mode everything passes; inside it the hidden ids above are filtered out (defence in depth — the palette itself cannot open in Simple Mode, but no caller should have to rely on that alone).</summary>
    public static bool AllowsPaletteCommand(string? commandId, bool simpleMode) =>
        !simpleMode
        || commandId is null
        || !HiddenPaletteCommandIds.Contains(commandId);

    // ───────────────────── the tray menu ─────────────────────

    /// <summary>The tray menu entries, by stable id, in the order the full menu shows them.</summary>
    public static readonly IReadOnlyList<string> TrayItemIds = new[]
    {
        "open", "quick-ask", "modes", "favorite-modes", "new-window",
        "reload", "downloads", "battle-lab", "prompt-pad",
        "command-palette", "settings", "lock", "switch-profile", "status",
        "exit",
    };

    /// <summary>The only tray entries Simple Mode keeps: Open and Exit — enough to use the app and to leave it.</summary>
    public static readonly IReadOnlyList<string> SimpleTrayItemIds = new[]
    {
        "open", "exit",
    };

    /// <summary>Whether one tray entry (by <see cref="TrayItemIds"/> id) is visible under the given mode.</summary>
    public static bool IsTrayItemVisible(string? itemId, bool simpleMode) =>
        !simpleMode || (itemId is not null && SimpleTrayItemIds.Contains(itemId));
}
