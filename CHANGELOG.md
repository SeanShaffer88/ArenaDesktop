# Changelog

All notable changes to ArenaDesktop. Format follows Keep a Changelog; versions use semantic versioning.

## [0.5.0] — Unreleased

A hardening pack: durability, honest status, and the small everyday frictions between a person and the text they are working with. Nothing here changes what the app is for.

### Added

- **Settings survive a crash mid-write.** Every settings, library, field-pass, downloads and session file is now written to a unique temporary file and swapped into place, with the swap retried a few times against a virus scanner's brief hold on the file. An interrupted save can no longer leave a half-written file where the previous good one was.
- **A corrupt file is preserved, not discarded.** When a store can't be read, the unreadable file is copied aside as `<name>.corrupt-<date>-<time>` before defaults are written, so a bug report can still include it and a hand-repair is still possible.
- **Settings writes are coalesced.** Flipping several checkboxes used to mean several full-file writes; changes are now batched into one write at most every two seconds, with forced writes on close, profile switch, pin and settings-OK so nothing is lost. A tally of writes, deferred, coalesced and failed appears in the diagnostics report.
- **Clipboard lifetime (opt-in, off by default).** Battle Lab can now take a copied prompt back off the clipboard after 1, 2, 5 or 15 minutes (Settings → Behavior). It only ever clears text it put there itself, so anything you copy afterwards always wins. Prompts can name private projects or a customer; the clipboard is readable by every app on the PC and kept by clipboard-history tools.
- **Spoken status messages (opt-in).** Accessibility setting that reads status-line changes to a screen reader through a polite live region. Filler ("Loading…"), repeats of the last message and anything over 140 characters are filtered out first, so the announcement is information rather than noise.
- **F6 focuses the Arena page,** so you can start typing in the page without a mouse, and **Ctrl+Shift+S copies the status line** — both fixed, both in the Shortcuts window.
- **Find in the log.** The log viewer gains a find bar with Next/Previous, match counts ("3 of 12") and Enter/Shift+Enter. Enter and Escape behave as expected; the match is selected and scrolled to.
- **The tray's Downloads submenu no longer offers dead links.** History records what was downloaded, not what still exists; entries whose file has been deleted or moved are now filtered out, so every item opens.
- **Prompt library export as Markdown or CSV.** The export dialog now offers JSON (unchanged round trip), Markdown for sharing, and CSV for a spreadsheet. CSV output is quoted and escaped per RFC 4180 and defused against spreadsheet formula injection, because a prompt body is arbitrary user text meant to be opened by someone else.

### Changed

- **Downloads list keeps your place.** Typing in the search box rebuilt the whole list, which threw away the selection and the scroll position. Rows are now reused when the record is unchanged, so the list stays where you left it — and a selection survives a filter change.
- **Blank prompt names are impossible.** The editor saves on every keystroke, so an emptied name box wrote a nameless prompt into the library, the run queue and the journal. A blank name now becomes "Untitled prompt", and a new prompt never reuses an existing label.
- **Skipped blanks are reported.** The fill-in dialog names the blanks that will keep their `[placeholder]` before you press Copy, and the status line says which ones survived after the copy, instead of leaving the placeholder to turn up in the pasted prompt.
- **The Downloads window is keyboard-operable.** Enter opens the selected file, Delete removes it (with the same confirmation as the button), Ctrl+C copies its path, and F5 reloads the history.
- **The update check uses one re-entry guard** instead of a hand-managed flag, so a second check while the first is still running is refused rather than racing it.
- **Log reads are bounded.** Diagnostics and the support bundle now read the tail of the log instead of the whole file, so a large log no longer slows down a report.

## [0.4.0] — Unreleased

### Added

- **Find options.** The find bar (Ctrl+F) gains an options button with **Whole word** and **Match case**, handed straight to WebView2's own finder. Changing an option re-runs the search, so the count never describes the old rules, and the result line is worded by one shared formatter ("3 of 12", "No results").
- **Zoom presets.** The ⋯ menu's View area gains **Zoom presets** — 50%, 75%, 100% (actual size), 150%, 200% — beside the existing Ctrl+± steps. A preset outside the active zoom range says so instead of silently clamping.
- **This session.** A new window (⋯ menu → *This session…*) counts what the run has actually done: pages visited, time on Arena, mode jumps, downloads finished, prompts copied, and reading time by mode. It lives in memory for the life of the process — saved nowhere, sent nowhere, nothing read from the Arena page — and "Copy summary" puts it on the clipboard for a bug report.
- **Pinned mode.** Pin the page you are on (⋯ → *Pin this mode*; unpin the same way) and the status bar carries a one-click "Pinned: <mode>" label, with a matching *Go to pinned mode* item in the ⋯ menu. The choice is saved with your settings and clears with a settings reset.
- **Download ordering and size filters.** The Downloads window can now sort oldest-first and smallest-first (for finding what to clean up), filter by size band (under 1 MB, 1–10 MB, over 10 MB, over 100 MB), and shows the total size of what the current search/filter/sort is showing. All of it composes with the existing search and completed/failed filter.
- **Downloads folder check.** Settings → Downloads checks the chosen folder when the window opens and whenever you pick a new one: it says whether the folder exists, can be written to, and how much room is left — in one plain line, never a green tick it didn't earn.
- **Download balloons name the size and folder.** A finished download's tray balloon now says how big the file is and where it landed, not just its name.
- **Startup trend in Diagnostics.** The Diagnostics report gains a trend line computed from the recorded startup timings — "steady", "slower by about 400 ms" or "faster by about…" — using medians so one slow launch isn't reported as the app getting slower. It needs a few launches before it says anything.
- **Session block in Diagnostics.** The report also names this run's pages, time on Arena, mode jumps, downloads, and the pinned mode, and says plainly that none of it is saved.
- **What a load error means.** The failure screen now prints the raw WebView2 status under the explanation (HOSTNAMENOTRESOLVED, CERTIFICATEEXPIRED, …) with its plain cause and the next step — useful in a bug report, and in plain words on screen.
- **Clearer update-check failures.** A manual update check that ends badly now says what kind of failure it was and the one thing to try next, instead of a shrug in the status bar.
- **Searchable shortcut cheat sheet.** Ctrl+/ now lists shortcuts grouped by area (Opening things, The page, Moving around, Zoom, The window, Arena modes) and searches names, key text and group titles with the same whole-word rule Settings and Help use. Clicking a row copies its keys.
- **Find duplicates.** Battle Lab gains a *Find duplicates* button: it reports prompts whose text is identical after folding whitespace and case, and names used more than once (with their categories), or says the library is clean.
- **Long prompts are checked before they are pasted.** Copying a prompt longer than 6,000 characters now asks first, with the exact size and a preview, and offers to export it as a .txt file instead of copying it.
- **Arrival notice after a jump.** When a mode jump or a page load finishes, the status bar says where you landed ("Arrived at Leaderboard · Ctrl+4") for four seconds, then goes back to Ready. A message that speaks after it is never overwritten by the timer.
- **Reading time is measured honestly.** Time on Arena only counts while the window is on screen, ignores absurd gaps (a sleep or a clock jump), and is capped per visit — so the session window can't overstate attention.
- **Dim the toolbar while reading.** Settings → Appearance gains an opt-in that cools the toolbar to a faint presence while the Arena page has focus, restoring it the moment the pointer arrives or focus returns to the shell. Off by default; the page is never touched.
- **Run-queue position labels.** Queue rows read "2 of 5 · Prompt name" from one shared formatter, so the queue and the status line can't describe the same entry two different ways.
- **Journal filters: mode and date range.** Journal entries now record which mode the battle ran in — the log dialog asks (it starts on the page you last copied a prompt to; change it if the battle ran elsewhere) — and the journal can be filtered by mode and by date (All time, Last 7 days, or Last 30 days) alongside the prompt-name filter. The scoreboard and the Markdown export always describe the filtered view, as they already did for the name filter. Entries logged before this change have no mode recorded and appear in unfiltered views.
- **Favorite modes in the tray.** The tray menu gains a Favorite modes submenu listing just your favorite modes (up to three, set in Settings → Behavior); clicking one jumps straight to it, through the same dispatch as every other mode jump. With no favorites set, the submenu shows a disabled "No favorites yet".
- **Import clash strategies.** "Import library…" now asks once, per import, what to do when an incoming prompt's title is already in the library: Skip it, Replace yours (the file's text and category take over; your prompt keeps its identity, so run-queue entries survive), or Keep both (the file's prompt arrives under a "… copy" name). A prompt whose title and text both match is always skipped, new titles are always added, and the 200-prompt library cap still applies to everything that grows the library.

### Changed

- **The Diagnostics route check keeps itself fresh.** While the Diagnostics window is open, the mode-route check re-runs every two minutes, and the last finished check's time ("Last checked 14:32") shows beside the button; closing the window stops the re-checks. The Check mode routes button works exactly as before when you want a check right now.

## [0.3.1] — 2026-11-02

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
