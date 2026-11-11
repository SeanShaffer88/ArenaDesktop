# What's new

The release notes ArenaDesktop shows on the first launch of a new version. This file is generated from CHANGELOG.md's current-version section at release time (see RELEASE_CHECKLIST.md) — do not edit it by hand; a test pins it to the changelog so the two can't drift.

## [0.3.1] — 2026-11-02

### Changed

- **Updating is hands-free once you accept it.** When an install runs while ArenaDesktop is open — window up, hidden in the tray, or behind the app lock — the installer now closes the app itself, installs over it, and reopens it when it finishes, with no prompts in between. The update window's Install button runs the installer silently to do this; running the installer yourself behaves the same way, except that the finish page's "Run ArenaDesktop" checkbox (pre-ticked when the app was open — untick it if you don't want the app back) is what reopens the app, as it fires only once the installer has closed. Uninstalling likewise closes a running instance before removing its files. Underneath is a new `--exit-for-update` command-line flag: it asks the running instance to close for an update, applies at once even while the app is locked, and simply exits when nothing is running.

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
