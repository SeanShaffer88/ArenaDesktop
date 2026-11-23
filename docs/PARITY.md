# PARITY.md — ArenaDesktop vs its MuseDesktop parity target

Scored only from evidence (build + test runs on 2026-10-27, and the code
itself). Gate at v0.1.0: build Release 0 errors / 0 warnings; full Core
suite 697/697 via the xUnit console runner. "Inherited" = carried from the MuseDesktop fork base and
renamed; "New" = written for ArenaDesktop.

| Capability | Status | Evidence |
| --- | --- | --- |
| Persistent sign-in (WebView2 profile) | Inherited | Core AppPaths/Profiles + tests green |
| Tray + close-to-tray + suspend | Inherited | TrayController; Core Presence tests green |
| Downloads manager (% / ETA / history) | Inherited | Core Downloads tests green |
| Profiles (up to 32) | Inherited | Core Profiles tests green |
| Settings + search + encrypted export/import | Inherited | Core SettingsExperience/EncryptedExport tests green |
| Privacy, permissions, app lock | Inherited, re-pinned to arena.ai | UrlPolicy tests green (arena hosts) |
| Appearance (themes/accents/toolbar/text size) | Inherited | Core Appearance tests green |
| Global hotkey | Inherited, changed to Ctrl+Alt+A | GlobalHotkey tests green |
| Recovery (crash/network/hung page), safe mode | Inherited | Core Reliability tests green |
| Updates (SHA-256 verified, rollback) | Inherited, re-pointed to SeanShaffer88/ArenaDesktop | Core Updates tests green |
| Help / Diagnostics / support bundle | Inherited, reworded for Arena | Help drift-guard test green |
| Crash ledger + data health (v0.14.0: UI, background-thread and unobserved-task crashes recorded, last-run marker, three-in-ten-minutes → safe mode, a per-file inventory with backup-first repair) | **New** | Core CrashHistory/DataHealth + CrashHistoryTests/DataHealthTests; nothing is uploaded |
| Unexpected-error report (v0.13.0: exception type, throwing app frame, bounded + redacted log tail, Copy details / Open log folder, Esc closes; guarded Settings load with a named-panel banner) | **New** | Core CrashReport + CrashReportTests (type/message/frame/inner, bounds, redaction, never-throws) and CrashWiringTests (dialog, presenter fallback, per-step guards, help page); nothing is uploaded — the report leaves the machine only if the user pastes it |
| CLI + arenadesktop:// protocol | Inherited + extended with mode flags/routes | CommandLine/LaunchIntegration tests green |
| Arena modes (9: Battle/Direct/SideBySide/Leaderboard/Agent/Image/Code/Video/Web Search, URLs verified live 2026-10-27) | **New** | ArenaModesTests — registry, CLI, protocol, JSON round-trip, shortcut catalog |
| Jump List mode tasks | **New** | JumpListBuilder (Battle, Leaderboard) |
| Battle Lab (prompt library, blanks, run queue, manual results journal; side pane + tray + `--battle-lab` + `arenadesktop://battle-lab`) | **New** (v0.2.0), pane in v0.8.0 | Core BattleLab + BattleLabTests; copy-to-clipboard only — never injects into or reads the Arena page |
| Prompt pad (one-prompt scratchpad: box → clipboard → page, last ten prompts remembered, draft kept, **Save to Battle Lab** handoff in v0.12.0; menu, tray, Ctrl+Shift+P, `--prompt-pad`, `arenadesktop://prompt-pad`) | **New** (v0.9.0) | Core PromptPad/PromptPadStore/PadHandoff + PromptPadTests and PadHandoffTests; copy-to-clipboard only — never injects into or reads the Arena page; the handoff goes through the Lab's live library, one writer per file |
| Command palette (`Ctrl+K`; 51 commands — modes, windows, layouts + cycle, five named zoom levels, page actions, log/support/About/lock, queue-next; prefix-per-term search; **arguments** since v0.14.0: `zoom 125`, `find cables`, `profile Work`, `mode side`; availability filter; menu, tray, `--palette`, `arenadesktop://palette`) | **New** (v0.10.0), covers the ⋯ menu in v0.11.0 | Core CommandPalette + CommandPaletteTests, incl. a menu-coverage drift test (every `Click` handler is a command or a written exemption); every id maps to an existing menu item or key handler — never to the Arena page |
| Layout cycle (`Ctrl+Shift+K`, ⋯ → View → Layout → Cycle layout, palette) | Inherited model (Core since v0.2.0), reachable since v0.10.0 | ShellLayoutTests (`ShellLayoutCycle.Next`) + CommandPaletteTests; the cycle's key came with the palette |
| Tray Arena modes submenu | **New** | TrayController, from the Core registry |
| Windows runtime behaviour | **Unverified** | RELEASE_CHECKLIST.md on-machine pass owed |
