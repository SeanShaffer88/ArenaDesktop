# Changelog

All notable changes to ArenaDesktop. Format follows Keep a Changelog; versions use semantic versioning.

## [Unreleased]

### Changed

- **Updating is hands-free once you accept it.** When an install runs while ArenaDesktop is open — window up, hidden in the tray, or behind the app lock — the installer now closes the app itself, installs over it, and reopens it when it finishes, with no prompts in between. The update window's Install button runs the installer silently to do this; running the installer yourself behaves the same way, except that the finish page's "Run ArenaDesktop" checkbox (pre-ticked when the app was open — untick it if you don't want the app back) is what reopens the app, as it fires only once the installer has closed. Uninstalling likewise closes a running instance before removing its files. Underneath is a new `--exit-for-update` command-line flag: it asks the running instance to close for an update, applies at once even while the app is locked, and simply exits when nothing is running.

## [0.3.0] — 2026-11-02

### Added

- **Copy a prompt to any text mode.** The Battle Lab copy button gains a target picker beside it — Battle, Direct Chat, or Side by Side — and the button's label follows the pick ("Copy prompt & open Direct Chat"). The flow is unchanged otherwise: blanks are filled in first, the finished text goes on the clipboard, and the main window comes forward on the picked page, ready to paste. Battle stays the default.
- **Battle Lab library export and import.** "Export library…" saves the whole prompt library (names, categories, and texts — not the queue or journal) as one JSON file with an ArenaDesktop format marker; "Import library…" merges such a file into the library, giving every imported prompt a fresh id and skipping any prompt whose name and text are already there. A file that isn't a Battle Lab library imports nothing.
- **Duplicate prompt.** A Duplicate button under Delete copies the selected prompt — same category and text — as "… copy" ("… copy 2", and so on, when the name is taken), ready to edit into a variant.
- **Add a whole category to the run queue.** "Add category to queue" lines up every prompt in the selected prompt's category that isn't queued yet, in library order, and reports how many were added.
- **Journal scoreboard.** The results journal now opens with its scoreboard — "12 battles · A won 5 · B won 4 · Ties 3" — counted over the entries the filter is currently showing, so filtering by a prompt name gives that prompt's record.
- **Journal export.** "Export journal…" saves the journal entries currently shown (after the filter) as a Markdown file: the scoreboard line, then one row per battle with its date, prompt, result, and note.

- **Mode route check in Diagnostics.** The Diagnostics window gains a "Check mode routes" button: it asks each of the nine Arena mode routes to answer (a HEAD request, or GET where a server refuses HEAD; about five seconds per route, all nine at once) and reports one plain verdict per mode at the end of the report — Reachable, Problem (the server answered 404, so Arena has moved that route), or Unreachable (no answer, a timeout, or an odd status) — plus a summary line. The check is reachability only: response bodies are discarded unread, nothing about any page's content is looked at, and the log records only each route's host and path with its verdict.
- **Startup mode.** Settings → Behavior gains a Startup mode choice — Battle (Arena home), Last mode I used, or a specific mode — that picks which Arena mode a launch opens with when it would otherwise land on the Arena home page. The mode each profile last used is remembered per profile (mode jumps are account-scoped, like last pages). Each profile's own start page still wins wherever it restores a page or names a custom one, and safe mode still starts at Home, so the default (Battle) changes nothing for existing installs.
- **Favorite modes.** Settings → Behavior also takes up to three favorite modes, in your order. Favorites lead the tray menu's Arena modes submenu (starred, with a separator before the rest) and the taskbar Jump List's Arena modes category, ahead of the Battle and Leaderboard tasks the list has always carried. With no favorites set, both menus keep their exact previous order.

- **The update window shows what's new.** When a newer version is offered, the update window now displays that version's release notes — formatted as plain readable text and shortened with a pointer to the full notes when they run long — so the choice to download and install is an informed one. The notes arrive with the update check itself; nothing extra is fetched. When a release has no notes, the window looks exactly as before.

## [0.2.0] — Unreleased

### Added

- **Battle Lab** — a new window (⋯ menu → Battle Lab…, the tray menu, `--battle-lab`, or an `arenadesktop://battle-lab` link) for running Battle prompts in an organised way. A prompt library (searchable, grouped by category, saved as you edit) holds reusable prompts; square-bracket blanks like [topic] in a prompt are filled in at copy time, and "Copy prompt & open Battle" puts the finished text on the clipboard and brings the main window forward on the Battle page, ready to paste. A run queue lines prompts up in order. A results journal records, by hand, which side won each battle (A won / B won / Tie) with a note — ArenaDesktop reads nothing from the Arena page, so results exist only when you log them. Four sample prompts are included on first run and can be edited or deleted.
- **Three shell layouts, with a picker.** The main window's own chrome can now be arranged three ways, chosen in Settings → Appearance & toolbar or from the ⋯ menu: **Standard** (the classic toolbar across the top and the status bar along the bottom), **Compact Focus** (a slim toolbar, and the status bar becomes a small floating pill in the page's bottom-right corner, giving the Arena page the extra height), and **Left Rail** (navigation, the Arena modes, Downloads and profile switching in a slim rail down the left edge, a thin title strip on top, and the status bar along the bottom). New installs start in Compact Focus; a layout you have saved is always kept — the new default applies only where no layout was ever saved, so upgrading never rearranges an existing window. Focus mode (F8) and full screen (F11) hide the rail along with the rest of the chrome and restore it on exit, and the Arena mode shortcuts (Ctrl+1…Ctrl+9) work in every layout.
- **Field pass** — a guided window (⋯ menu → Field pass…, Diagnostics → Field pass…, `--field-pass`, or an `arenadesktop://field-pass` link) that walks nine checks proving the app on this PC: the three layouts, Battle Lab, Arena mode jumps, a battle with its journal entry, downloads, the tray and hotkey, the app lock, profile switching, and the update check. Each check says what to do and what working looks like, and offers a button that opens the feature it is about; you mark Pass, Problem, or Skip and can add a note. Verdicts and notes are saved on this PC as you go (closing the window keeps your progress), and "Copy summary" puts the whole pass on the clipboard as plain text for a bug report. The field pass records what you saw — it doesn't verify anything by itself, and nothing is read from the Arena page.

### Fixed

- **Quick ask is now named correctly everywhere.** A few places still used the feature's old name — the README's tray list and command-line table, and the status line shown when Arena is not ready yet. The command-line table also described the shortcut as getting you ready for a voice button; it now correctly says it focuses the Arena page ready to type, and lists `--ask` as the main flag (`--dictate` still works as an alias).

## [0.1.1] — 2026-10-27

### Changed

- **New ArenaDesktop icon** — an original column mark (pediment, three columns, red base step) on a black rounded square, drawn for this app and shipped as a multi-size icon (16–256 px) for the taskbar, tray, window and shortcuts. It replaces the icon carried over from the project this one was forked from.

### Fixed

- Wording: the tray / Jump List / command-line shortcut that focuses Arena ready to type is named **Quick ask** everywhere, including this changelog (v0.1.0's notes used the old name). Arena has no voice button; for voice input, click the prompt box and use Windows dictation (Win+H).

## [0.1.0] — 2026-10-27

### Added

- Native Windows client for arena.ai — a dedicated window, taskbar and tray presence, and a persistent sign-in that survives restarts, instead of a browser tab.
- **Arena modes** — Battle, Direct Chat, Side by Side, Leaderboard, Agent, Image, Code, Video and Web Search, reachable from the ⋯ menu, the tray menu, the taskbar Jump List, Ctrl+1…Ctrl+9, the command line (--battle, --direct, --side-by-side, --leaderboard, --agent, --image, --code, --video, --search, --mode <id>) and arenadesktop:// links, all dispatched through one registry in ArenaDesktop.Core. Every mode is its own page on arena.ai (addresses verified on the live site); the models used inside Direct Chat and Side by Side are picked at the top of those pages.
- Downloads manager — parallel downloads with progress %, speed and ETA in the status bar and on the taskbar button, pause / resume / cancel / retry, and a searchable history with Open / Show in folder / Copy path / Open with / Delete file.
- Multiple profiles — separate Arena sign-ins on one PC (up to 32), each with its own session data, zoom, downloads folder, accent colour and start page; per-profile desktop shortcuts and --profile <id> launches.
- Tray support — run the whole app while hidden, close-to-tray, optional suspend of the hidden web view to free memory, a global show/hide hotkey (Ctrl+Alt+A), Quick ask, and single-instance behaviour (a second launch wakes the running window).
- Settings window (Ctrl+,) with a search box — startup, downloads, updates, appearance (themes, accents, toolbar layout, shell text size), export/import (optionally password-encrypted), a first-run setup wizard, and reset-with-backup.
- Privacy & security — granular clear-data (cookies only / cache only / all site data, per profile or all), a permissions manager, a counts-only site-data viewer, an optional app lock (password/PIN; guards the open app, does not encrypt the disk), a log viewer with a redaction check, and a connection-security view.
- Reliability — one shared recovery budget for page crashes, network returns and sleep/wake, a hung-page watchdog that offers Reload / Wait and never reloads on its own, safe mode (--safe-mode or hold Shift), a guided reset, and rotating backups of settings and download history.
- Updates — checks GitHub Releases, downloads the installer in-app with progress, verifies its SHA-256 fingerprint before Install is offered, and never installs on its own; snooze, check frequency, Stable / pre-release channels, What's New, and one-step rollback to the previous in-app-updated version.
- Help & support — offline Help & troubleshooting pages inside the app, a Diagnostics window with a one-click copyable report, a preview-first support-bundle export (uploaded nowhere), and guided microphone and notification tests.
- Portable mode — an empty portable.txt beside the exe keeps settings, history and logs in a data folder beside the exe.
- Find in page (Ctrl+F), print and Save as PDF (Ctrl+P), focus mode (F8), full screen (F11), always-on-top pin, a second window on the same sign-in, history menus on Back/Forward, and Ctrl+Shift+T to reopen the last closed page.

### Notes

- ArenaDesktop is an independent open-source project. It is not made by, endorsed by, or affiliated with Arena Intelligence, Inc.
- All Windows runtime behaviour in this version is **Unverified** until the RELEASE_CHECKLIST.md pass has been run on a real Windows machine.
- The app is not code-signed, so Windows SmartScreen may warn on first run — choose More info → Run anyway.
