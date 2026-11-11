# Known limitations

Honest list of what ArenaDesktop does not do or has not proven yet. Kept current as work proceeds.

## Verification status

- **Windows runtime: Unverified.** v0.1.0 is built and fully gated on a Linux VM (build 0 errors / 0 warnings + the full Core test suite). Nothing about real Windows behaviour — sign-in persistence, tray, downloads, voice, updates — is claimed until the RELEASE_CHECKLIST.md pass runs on a real Windows machine.
- Arena's live routes (Battle home, `/leaderboard`) were set from the site's public structure on 2026-10-27; a live in-app confirmation is part of the same field pass.

## Product limits

- **Inside Direct Chat and Side by Side, you pick the model(s).** ArenaDesktop takes you to the right page; the model choice at the top of those pages is Arena's own picker — the app never injects script into the page, so it cannot choose for you.
- **Battle Lab lives in the main window.** Since v0.8.0 it is a side pane, not a window, so it cannot be dragged to a second monitor — the page and the Lab share one window by design. In a narrow window the pane folds to one tabbed column, and below 706px of content width the split is refused outright (the status bar says so) rather than showing two unusable columns.
- **Battle Lab results are recorded by hand.** The app never reads anything from the Arena page, so the results journal only contains what you log yourself (A won / B won / Tie plus your note); winners are never detected automatically.
- **The field pass records your own verdicts.** Its nine checks are steps you perform by hand; the pass stores the Pass / Problem / Skip marks and notes you enter and does not verify anything by itself.
- **Arena has no voice dictation.** There is no microphone button in Arena's prompt box (checked on the live site, 2026-10-27). For voice, click in the prompt box and use Windows dictation (Win+H); ArenaDesktop's Quick ask (tray / `--ask`) just gets you to the page, focused and ready to type.
- **Portable mode does not carry your sign-in.** Settings, download history and logs travel in the `data` folder beside the exe; the WebView2 sign-in profile stays in `%LOCALAPPDATA%`.
- **The app lock guards the open app only.** It does not encrypt your sign-ins or settings on disk, and the app says so wherever the lock is described.
- **Rollback covers one previous version**, and only a version that was itself reached through the in-app updater.
- **Seamless updates — two honest edges.** (a) **Updating FROM v0.3.0 or older is closed by the installer's fallback, not the handshake.** Those builds don't know `--exit-for-update`: they tolerate the flag and merely wake their window, so the new installer waits, then asks the process to close its main window (a close-to-tray build may hide to the tray instead of exiting), then force-closes it if it still hasn't gone. The update is still fully automatic and the app still relaunches afterwards — the exit is just less gentle that one time. The clean handshake applies once both sides speak it. (b) **Rollback keeps the old flow.** Reinstalling the previous version from Diagnostics launches a kept installer that predates this feature: it shows its classic wizard (you click through it), the app exits so setup can take over, and nothing relaunches ArenaDesktop afterwards — you start it yourself when the rollback finishes.
- **Update checks need a published release to find.** Before the first ArenaDesktop release exists on GitHub, Check for updates reports it couldn't find release information.
- **Unsigned binaries.** There is no code-signing certificate; Windows SmartScreen may warn on first run (More info → Run anyway).
- **Architectures:** x64 only since v0.7.0. No ARM64 build (it shipped up to and including v0.6.0; a Windows-on-ARM PC can run the x64 build under emulation, but that is not the same as a tested native build). No x86 (32-bit) build and no MSI installer yet.
- **No screenshots yet.** README captures come from real Windows runs only; the capture list is in docs/screenshots/CAPTURE.md.
- **Downloads history follows this app's activity.** While the window is open, ArenaDesktop refreshes history after a download completes or fails and after local delete or clear actions. Reopen the window to refresh after external edits.
- **The session window's numbers are this-run-only.** Pages, time on Arena, mode jumps, downloads and copied prompts are counted in memory and vanish when the app closes; time only counts while the window is on screen, and gaps over four hours are ignored.
- **Pinned mode is a shortcut, not a setting that changes behavior.** It adds a status-bar jump and two ⋯ items; it never changes which page a launch opens.
- **Duplicate detection compares prompts, not intent.** It reports identical text (whitespace and case folded) and reused names — it can't know whether two prompts were meant to be different.
- **The long-prompt warning guesses no limit.** ArenaDesktop can't see Arena's prompt-box length limit, so it warns above 6,000 characters and offers an export; it never truncates or refuses the copy.
- **Downloads history never stores source URLs** — a downloaded file can be opened, shown in its folder, or deleted, but not re-downloaded from history after the session's retry window.
