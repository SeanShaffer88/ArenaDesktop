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
| CLI + arenadesktop:// protocol | Inherited + extended with mode flags/routes | CommandLine/LaunchIntegration tests green |
| Arena modes (9: Battle/Direct/SideBySide/Leaderboard/Agent/Image/Code/Video/Web Search, URLs verified live 2026-10-27) | **New** | ArenaModesTests — registry, CLI, protocol, JSON round-trip, shortcut catalog |
| Jump List mode tasks | **New** | JumpListBuilder (Battle, Leaderboard) |
| Tray Arena modes submenu | **New** | TrayController, from the Core registry |
| Windows runtime behaviour | **Unverified** | RELEASE_CHECKLIST.md on-machine pass owed |
