# What's new

The release notes ArenaDesktop shows on the first launch of a new version. This file is generated from CHANGELOG.md's current-version section at release time (see RELEASE_CHECKLIST.md) — do not edit it by hand; a test pins it to the changelog so the two can't drift.

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

