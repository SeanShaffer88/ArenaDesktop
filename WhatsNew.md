# What's new

The release notes ArenaDesktop shows on the first launch of a new version. This file is generated from CHANGELOG.md's current-version section at release time (see RELEASE_CHECKLIST.md) — do not edit it by hand; a test pins it to the changelog so the two can't drift.

## [0.14.0] — 2026-11-23

Everything in this release came from one sentence in a bug report — "error message when accessing Settings" — and from the list of things that sentence made obvious: the app could not name its own broken file, could not remember its own crashes, and asked the user to do by hand four things it could take as an argument.

### Added

- **The app inspects its own files.** Diagnostics now carries a *Data files* section — every store the app owns (`settings.json`, `downloads.json`, `battle-lab.json`, `prompt-pad.json`, `field-pass.json`, `privacy-stats.json`, `crash-history.json`) with its size and either *ok* or what is wrong with it, in words: "unreadable: the file is empty", "…: the file holds a json array, not an object". Beside it, **Repair data files…** copies each unreadable file beside itself (the `.corrupt-` name the app already uses) **before** deleting it, so the worst outcome of pressing it is nothing happening.
- **A crash ledger.** Crashes are recorded — the UI thread, a background thread (`AppDomain.UnhandledException`), and a task nobody awaited — in `crash-history.json`. A clean exit clears the marker, so the next launch can say **the last run ended in an error** in the status bar, with the detail in Diagnostics. Three crashes inside ten minutes is treated as a loop, and the crash dialog then offers **Restart in safe mode**.
- **Arguments for the command palette.** Ctrl+K now understands `zoom 125`, `find cables`, `profile Work` and `mode side` — the specific row goes to the top and Enter runs it, through the same code paths the menus use. Anything malformed (`zoom 900`, an unknown profile) stays a search, and the argument is never written to the log.
- **Blanks with defaults.** `[tone=neutral]` is a blank named *tone* whose default is *neutral*: the user's own answer wins, the default fills in when nobody supplies one, and a defaulted blank is no longer reported as "left as it was". Plain `[topic]` behaves exactly as before.
- **`saved cables` — the palette can now copy one of your saved prompts.** A fifth argument: **`saved <text>`** finds a prompt in Battle Lab, copies it with its defaulted blanks filled and opens Battle. The ranking is stated rather than felt — exact name, then a name that starts with your words, then a name that contains them, then a category, then a body — and the first rank that finds anything wins; a tie is a *shortlist*, not a guess, so several matches open Battle Lab filtered to them instead of copying one at random. A prompt that still has real blanks is refused by name, exactly as the queue command refuses it. The verb is `saved` and not `prompt` on purpose: **`prompt pad` has to keep opening the pad**, and a test now says so.
- **The head of the run queue, without opening the pane.** ⋯ → Tools → **Copy the next queued prompt** (and a palette row of the same name) puts the next prompt in the run queue on the clipboard with its defaulted blanks filled and opens the Battle page — the pane's *Use next*, one gesture away. A prompt that still has blanks to fill is refused by name ("has blanks to fill first ([article])") instead of being copied half-finished, and the queue itself is untouched: it is the pane's own result-logging flow that retires a prompt, so a copy from the menu cannot consume it behind your back.
- **The field pass exports itself.** **Export report…** writes the pass as Markdown or JSON (the file dialog decides), with a `schemaVersion` and the same rule as before: verdicts and notes only, nothing from the Arena page.
- **Which prompts win.** The Battle Lab journal gains a cross-prompt leaderboard — battles, A wins, B wins and ties per prompt, ranked by decided battles — and "Copy journal" includes it once more than one prompt is recorded.
- **An ARM64 build in CI.** A separate job publishes `win-arm64` self-contained and checks the WebView2 arm64 loader is in the payload, so the publish cannot rot. Nothing ships ARM64 yet; a guard test keeps it that way.
- **`scripts/verify-windows.ps1`.** Runs the start-up smoke test, scans the log for errors, asks for a verdict on each release-checklist item, and writes `docs/verification/verify-<stamp>.md` — the file the "Unverified" rows in QUALITY.md are waiting for.

### Changed

- The High Contrast support that shipped in v0.9.0 is now *audited*: a test fails if a brush the shell paints with has no High Contrast override, or if the override stops being re-applied when Windows' preference changes.

## [0.13.0] — 2026-11-23

A bug report arrived as a screenshot of an error box: "ArenaDesktop hit an unexpected error but kept your session safe. Details were written to the log: C:\Users\…\arenadesktop.log". Settings would not open. The box named no exception, the frame that threw, or what to do — so this release fixes the two halves of that: **the app says what broke**, and **Settings cannot be taken down by one panel**.

### Added

- **An error report instead of a pointer to a file.** The unexpected-error box now shows the exception type and message, the app frame that threw it, and the path to the log — with **Copy details** (a bounded report: the exception, the throwing frame and the last 25 log lines, capped at about 4 KB) and **Open log folder** beside it. Esc closes it. The log tail goes through the same redaction pass the support bundle uses, so a query string, an email address or a token inside a log line is stripped before it can be pasted anywhere.
- **Settings opens even when a panel fails.** Every load-time step that touches the disk, the registry or the page is now run under a guard: a failure is logged with the step's name (`Settings: the profile list failed: …`) and listed in a banner at the top of the window — "Some settings could not be shown: the profile list…" — while the rest of Settings loads and works. Before this, any one of those throws meant the window never appeared at all.
- **The failure names the window, and the reason.** A Settings window that cannot even be constructed now says so ("Settings could not open." + the exception summary) instead of surfacing as an anonymous "unexpected error", and a main window that cannot be created reports the same way before the app exits.
- **A Help page for the box itself** — *"ArenaDesktop showed me an error — what now?"* — offline, with what the buttons do, why the report is safe to paste, and what to do if it happens at launch.

## [0.12.0] — 2026-11-23

The prompt pad has always been the place for a prompt you only want once — which is exactly why "actually, keep this one" was the thought it could not act on. Now it can.

### Added

- **Save to Battle Lab, from the pad.** A **Save to Battle Lab** button, or **Ctrl+S**, hands the box's text to Battle Lab's library. The prompt lands in a `Prompt pad` category, named after its first line, and the pad says where it went — no more copy, open the Lab, New prompt, paste. The box keeps its text afterwards, because keeping a prompt and copying it are two different intentions and you may still want both.
- **The library refuses what it already holds.** An exact duplicate — the same text with surrounding whitespace ignored, wherever it was saved from — is not added twice: the pad says *Already in Battle Lab · (its name)*. At the library's 200-prompt cap it says the library is full instead of growing the file, and two prompts that open with the same line are named "… 2", "… 3" rather than looking identical in the list.
- **One writer for a file two surfaces can see.** If the Battle Lab pane has been opened, its live library is the copy on disk, so the prompt goes through it and the pane's list refreshes; if it has never been opened there is no second copy, so the store is opened, written and left. The prompt's text is never written to the log — only what happened.

## [0.11.0] — 2026-11-23

v0.10.0 gave the app one list to type at. It did not quite cover the app: the ⋯ menu still held about a dozen commands the palette had never reached — the log, the support bundle, About, Save as PDF, a second window, five named zoom levels. This release closes that gap and pins it shut with a test.

### Added

- **The rest of the ⋯ menu is in the palette.** *View the log…*, *Open log folder*, *Export a support bundle…*, *Connection security…*, *About ArenaDesktop*, *Save as PDF…*, *Open in a new window*, *Quick ask* (which focuses the Arena page, ready to type — until now the tray and `--ask` were the only ways to it) and *Lock ArenaDesktop*. Every one of them calls the same handler its menu item calls.
- **The five named zoom levels.** 50%, 75%, 100%, 150% and 200% come from Core's own preset list, so the View menu's Zoom submenu and the palette can never offer different ones. 100% carries Ctrl+0, because that key already did it, and its keywords include "reset" and "normal" — the plain "Reset zoom to 100%" row is gone rather than duplicated beside it.
- **Commands that cannot work are hidden, not offered-and-refused.** The palette now asks the shell whether a command applies: *Lock ArenaDesktop* appears only when a lock is set, and a zoom level outside the active range (150% and 200% with the extended range off) is not listed at all. The count under the list follows what is available, so "12 of 50" never implies commands are missing.

### Changed

- **A test now forbids the drift coming back.** Every clickable item in the ⋯ menu must either be a palette command or carry a written exemption with a reason — the find bar's own buttons, the recovery prompts, the pinned-mode rows, the parameterised mode rows and the items that open a submenu are the documented exceptions. Adding a menu item without deciding which it is fails CI.

## [0.10.0] — 2026-11-23

Around thirty commands, four menus to find them in, and one command — the window layout cycle — with no way to be reached at all. This release adds one list you can type at: **Ctrl+K**, a few letters, Enter.

### Added

- **The command palette (Ctrl+K).** One searchable list of everything the app can be asked to do: the nine Arena modes, every window (Settings, Downloads, the Battle Lab pane, the prompt pad, the field pass, the shortcut list, Diagnostics, This session…, Help, Check for updates), the three layouts and their cycle, focus mode, always-on-top, Find in page, Back / Forward / Home / Reload / Restore the last closed page, the three zoom steps, Print, and the two page hand-offs. Type a few letters — every word must start a word in the command's name or keywords, the same rule Settings and Help search by — move with ↑ ↓, run with Enter, leave with Esc. Each row prints the key that actually works, including one you remapped. It opens from ⋯ → **Tools** → *Command palette…*, the tray menu, **Ctrl+K**, `--palette` / `--command-palette`, or `arenadesktop://palette`, and it starts nothing new: every command is a second door onto a menu item or key that already existed. It refuses to open while the app is locked.
- **A key for the layout cycle: Ctrl+Shift+K.** Standard → Compact Focus → Left Rail → Standard in one gesture, also in ⋯ → **View** → **Layout** → *Cycle layout* and in the palette. `ShellLayoutCycle` has been in Core since v0.2.0; nothing could call it, because the parity target keeps its cycle inside a command palette this app did not have.
- **The prompt pad keeps an unsent draft.** Closing the pad no longer loses what is in the box — the text comes back the next time it opens, stored in `prompt-pad.json` beside the remembered prompts and carried by portable mode like the rest of your data. Copying a prompt clears the draft, because by then the prompt is in the remembered list.

### Changed

- **The shortcut sheet lists Ctrl+K** with the shortcuts that open something, Help has a page for the palette, and the tray menu has an item for it. Nothing was removed or rebound: every key that worked in v0.9.0 still does the same thing.

## [0.9.0] — 2026-11-23

A scratchpad for the prompt you only want once. Battle Lab is the library — kept, organised, journaled — and it is the wrong place for a prompt you are about to try exactly one time. The prompt pad is that place: open it, write or paste the prompt, and it goes on the clipboard while the Arena page you picked comes forward with the keyboard already in it.

### Added

- **The prompt pad.** A small window (⋯ menu → **Tools** → *Prompt pad…*, the tray menu, `--prompt-pad`, `arenadesktop://prompt-pad`, or **Ctrl+Shift+P**) holding one prompt and the page it is for. **Copy & open** (the button names the page; or Ctrl+Enter) puts the text on the clipboard and brings that page forward, ready for your own Ctrl+V. Nothing is typed into Arena and nothing is read from it — the pad copies, you paste, exactly as Battle Lab's copy button has always worked. Enter is a new line, Esc closes the window.
- **The page you used last is remembered.** The picker beside the copy button offers every Arena mode (Battle by default) and the pad reopens on the one you chose last, stored in the pad's own file rather than guessed from the current page.
- **It remembers the last ten prompts.** A copied prompt is kept in `prompt-pad.json` beside your settings, and portable mode carries it with the rest of your data. Double-click one to load it back into the box, **Forget** removes the selected prompt, **Forget all** empties the list — neither touches the text in the box, and none of it is sent anywhere.
- **Blanks can be filled, and long prompts are checked first.** A prompt with `[blanks]` shows **Fill blanks…**, which opens the dialog Battle Lab uses and puts the filled text back in the box, so the box always shows what will be pasted. A prompt past 6,000 characters asks before it is copied, with its exact size, using the same rule and wording as Battle Lab.
- **The clipboard lifetime setting covers the pad.** With "Clear copied prompts from the clipboard after…" turned on, a pad copy is cleared on the same schedule as a Battle Lab copy. The pad closes the moment it copies, so the countdown is kept by the main window instead — it never clears while the app is locked, and it only ever clears text this app put there.

### Fixed

- **The session window's "prompts copied" counter never moved.** It has been offered since v0.4.0, but nothing ever incremented it, so it always read zero. A prompt copied from Battle Lab's copy button and a prompt copied from the pad now each count once — still this-run-only, still saved nowhere, still nothing read from the Arena page.

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

## [0.8.0] — 2026-11-11

Battle Lab stops being a window you switch to and becomes a pane beside the page you are working on.

### Changed

- **Battle Lab is a side pane.** It used to open in a window of its own, so running a prompt meant leaving the battle you were mid-way through. It now opens beside the Arena page in the same window, behind a draggable splitter (⋯ menu → **Tools** → Battle Lab, the tray menu, `--battle-lab`, `arenadesktop://battle-lab`, or the new **Ctrl+Shift+B**). The page keeps its width: the pane takes space from it, never covers it.
- **The pane reflows to fit.** The Lab was drawn for a 1220px window, so its three columns would leave the editor a hundred pixels wide in half a screen. It now picks the arrangement that fits: three columns when the pane is 940px or wider, two with the results journal underneath at 600px and up, and one tabbed column (Prompts / Editor / Journal) below that. The thresholds are pinned in Core with tests.
- **The split has a floor, and says so.** Below 706px of content width the window is widened first; if it still will not fit, the app says Battle Lab needs a wider window in the status bar and stays single-pane rather than showing two unusable columns. Dragging the splitter can never squeeze the page under its own floor.
- **Closing the pane keeps your place.** The Lab is built once and kept after it is hidden, so reopening lands on the same prompt, filters and run queue. The ⋯ item reads **Hide Battle Lab pane** while it is open.
- **Ctrl+Shift+B opens or closes it**, listed with the other shortcuts.

### Removed

- **The Battle Lab window.** `BattleLabWindow` is deleted; the same logic lives in `BattleLabView`, a control the main window hosts. Every entry point opens the single pane, so two Battle Labs can no longer exist at once.

## [0.7.0] — 2026-11-11

Two changes to the app's shape rather than its features: it ships for x64 only, and the ⋯ menu is organised into five groups instead of one long list.

### Changed

- **The ⋯ menu is grouped.** It had grown to a flat list of thirty entries at the top level, so finding one meant scanning all of them. The top level is now five groups — **Navigate** (Arena modes, per-mode pinning, back/forward/home/reload/restore), **View** (zoom presets, focus mode, always-on-top, the three layouts, find, this session, connection security), **Page** (print, save as PDF, copy address, open in browser, open in new window), **Tools** (downloads, Battle Lab, field pass, keyboard shortcuts, lock now) and **Help** (help, logs, diagnostics, support bundle, about). Profile switching, Settings and Check for updates stay at the top, where they were. Nothing was removed and no shortcut changed; every command is one or two clicks away, and the menu's own test-visible names are untouched.
- **Back, Forward, Home and Reload are in the menu.** They were keyboard-only (Alt+←/→/Home, F5) and had no visible entry anywhere; they now sit at the foot of **Navigate**, through the same dispatch as the keys.
- **The menu's key hints follow your remaps.** The ⋯ menu prints the key beside Settings and Home, and both are remappable — so the shell now rewrites those two hints from the shortcut catalog at startup and on Settings OK, instead of trusting the markup. Back, Forward and Reload are fixed and still read Alt+Left, Alt+Right and F5.

### Removed

- **ARM64 builds.** v0.7.0 ships x64 only: no ARM64 CI job, no `-arm64.exe` installer, no `-windows-arm64.zip`. ARM64 was carried because the fork base's CI built it, and it was never actually run through the on-machine pass on real hardware — an unverified native build on offer is worse than none. A Windows-on-ARM PC can still run the x64 build under emulation. **Anyone on an ARM64 build of 0.6.0 or earlier stays on it:** the updater still names their architecture, finds no matching installer in a newer release, and offers nothing, rather than handing them x64 binaries silently.

## [0.6.0] — 2026-11-11

A second small pack on the same theme as 0.5.0: things that were silent now speak, and things that were one-at-a-time now take the whole selection. The idle lock is the one genuinely new capability — the app could lock itself on startup or when it hid to the tray, but not when you simply walked away.

### Added

- **Idle auto-lock (opt-in, off by default).** Settings → Privacy → App lock gains a **Lock after … with no input** picker: Never, 5, 15, 30, 60 minutes, or 2 hours. It uses the same password the lock already asks for, so it only does anything when one is set — the picker says so instead of pretending otherwise. When the timer fires the app hides to the tray and locks, without stealing focus from whatever you moved on to; unlocking brings the window back. A quick check every 30 seconds decides, and if Windows can't report the idle time the app does nothing rather than guess.
- **Zoom tells you where it landed.** Ctrl+= / Ctrl+- (and the toolbar buttons) were silent, so a keyboard user could not tell whether the key did anything, or how far they were from where they started. The status line now reads "Zoom 125%", and at either end of the range says which end it is rather than naming a level that did not change. Ctrl+0 resets to 100% and reports that too.
- **Delete several downloads at once.** The Downloads list now supports Ctrl+click and Shift+click; selecting more than one row reveals **Delete N selected…**, and the confirmation names how many files, their total size, and how many are already missing — so the number is about what will actually happen. One history write for the batch, per-file failures are reported and kept in the list rather than aborting the rest, and the Delete key does whatever the visible button would.
- **Filter the log by level.** The log viewer gains Errors / Warnings / Info / Unparsed chips with a count beside each, composing with find. Levels are read from the `[LEVEL]` marker the logger itself writes, not by searching the message text, so an INFO line that mentions an error is not promoted to one. Unparsed lines are shown as their own group rather than dropped, and the redaction audit and "Copy redacted log" always work on the whole file, never just the filtered view.

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
