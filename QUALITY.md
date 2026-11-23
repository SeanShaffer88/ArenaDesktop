# QUALITY.md — ArenaDesktop v0.15.0 (assessment, 2026-11-23)

Scored from evidence only. Every row names what it is scored on, and every
score says what would move it. **No row is scored Verified on a user's
Windows PC** — the on-machine pass in `RELEASE_CHECKLIST.md` has still never
run, and nothing below pretends it has.

This replaces the v0.1.0 pre-release assessment (2026-10-27, 25/40). Much has
changed since: the build and the full test suite run in CI on a Windows runner
for every pull request and every tag, six releases' worth of shell surfaces
have landed (Battle Lab pane, prompt pad with its Save to Battle Lab handoff,
command palette with five arguments, the v0.13.0 error report, and v0.14.0's
reliability layer), and the release pipeline builds, hashes and publishes by
tag rather than by hand — four times now, end to end.

**v0.14.0 scores 31/40, unchanged from v0.13.0 — on purpose.** The release adds
a great deal of *coverage*: a data-health check with a repair that copies
before it deletes, a crash ledger for UI-thread, background-thread and
unobserved-task failures, a 3-in-10-minutes crash-loop guard that offers safe
mode, a field-pass export, palette arguments that reach the user's own prompt
library, defaulted prompt blanks, and an ARM64 build in CI that proves the
x64-only publish has not rotted. Each of those has tests. None of them is an
*observation* of the app on Windows, and the two rows that would move on such
an observation (Windows runtime, Reliability) say so in their own text. Adding
machinery is not the same as watching a machine fail and recover, and this file
does not trade one for the other.

**v0.15.0 scores 31/40, unchanged again — and again on purpose.** The release is
a restyle of the Settings window: section cards, token-only fields and
buttons, a pinned OK/Cancel bar, a wider default window. It adds one drift
guard (the window's card, token and focus-visual structure, read as text the
way the other shell guards read theirs) and changes no behaviour. A restyle
cannot be observed from CI, and this file does not pretend otherwise: no row
moves until the on-machine pass in RELEASE_CHECKLIST.md has run, which now
carries a v0.15.0 item for exactly this.

| Dimension | Score /5 | Evidence |
| --- | --- | --- |
| Correctness (Core logic) | 5 | The full Core suite is a **required gate** on every PR and on every release tag (`ci.yml`, `release.yml`): 846 test methods (764 `[Fact]`, 82 `[Theory]` with 494 inline cases) over URL policy, modes, settings/profiles, downloads, updates, the app lock, layouts, Battle Lab, the prompt pad, the palette and its five arguments, data health, the crash ledger, the field-pass export and the queue runner. Four of them are drift guards rather than unit tests — WhatsNew↔CHANGELOG, Help.md↔HelpCatalog, every ⋯ menu handler ↔ a palette command or a written exemption, and the Settings window's card/token/focus-visual structure — so the class of bug they cover cannot come back unnoticed. The v0.14.0 slices added guards of their own: every `App.xaml` brush must have a High Contrast override and the theme re-apply handler must still exist, the ARM64 build may exist in CI but no workflow may ship an arm64 asset, the queue's log line may describe the copy without naming the prompt, and the Settings window may not hardcode a colour or lose its focus ring. | 
| Shell build health | 5 | `dotnet build ArenaDesktop.sln -c Release` and then `dotnet publish -r win-x64 --self-contained` on the Windows runner, for every PR: green at v0.14.0 (post-merge run 45103673473, tag run 45104415528), and an `arm64` job publishes `win-arm64` alongside it and asserts the WebView2 arm64 loader is in the payload. Four real shell build failures were caught this way during the v0.14.0 work and fixed rather than merged: a parameter name that did not exist, an ambiguous `Application`, new types parsed as *nested* types inside `BattleLabLibrary` (legal C#, invisible to every caller), and a record and a class sharing one name. Caveat: the workflow fails on errors, not on warnings, and its log is not readable through the API — the **0-warnings** half of the gate is still the maintainer's local run. | 
| Windows runtime | 3 | **Partially verified.** CI launches the published exe with `--smoke-test` on Windows Server 2025: the window is constructed and WebView2 is initialised, and a failure reports the app's own log line (v0.14.0 tag run, step "Smoke test (window construction + WebView2 init)": passed). That covers "it starts at all" — not keystrokes, menu behaviour, WebView2 rendering, the installer, the update handshake, the new repair button or the safe-mode relaunch, all of which remain **Unverified**. The arm64 job changes nothing here: it proves the *publish* succeeds, not that anything runs on arm64. | 
| Accessibility | 3 | Inherited shell pass plus this product's own work: named elements, polite/assertive live regions, a status line short enough to be spoken, the shortcut sheet and the command palette both keyboard-first, `FocusReturn` after every dialog, and — new in v0.14.0 — an audit test that fails if a brush has no High Contrast override or if the theme stops re-applying on `UserPreferenceChanged`, so the v0.9.0 HC work cannot rot silently. Still **Unverified** with a screen reader on real Windows, and no High Contrast *run* has been recorded; a test that the overrides exist is not a pass in front of a user who needs them. | 
| Privacy / security | 4 | URL policy and log redaction pinned by tests; no telemetry by construction; the app lock's scope stated wherever it is described; `arenadesktop://` links take no arguments (a test refuses `…/palette/settings`). v0.14.0 held the line on four new surfaces: the crash ledger stores an exception type, a frame and a timestamp and never a prompt or a page; the data-health check reports Ok/Missing/Unreadable and **never echoes file content**; the palette logs the *kind* of an argument and never the words typed (`saved cables` leaves no trace of "cables"); and the repair copies a broken file beside itself before deleting it. Nothing in the palette, pad or Lab reaches the Arena page — no injection, no reading. The lock does not encrypt data on disk, and that is said in three places rather than implied. | 
| Reliability / recovery | 3 | **Held at 3, deliberately.** v0.14.0 adds a great deal — a data-health inventory with a copy-then-delete repair, a crash ledger (UI thread, background thread, unobserved task) that survives the process, a next-launch "the last run ended in an error" marker, and a 3-in-10-minutes loop guard that offers **Restart in safe mode** — and all of it is test-covered in Core, including the guard's exact boundaries. But the row moves on an *observed* failure with the recovery path seen, not on more code: a real crash, a real network kill and a real killed WebView2 process are still **Unverified**. What the new work does change is the cost of that observation: it is now one deliberate crash rather than an afternoon of staging. | 
| Documentation | 4 | README, CHANGELOG, WhatsNew, DECISIONS, KNOWN_LIMITATIONS, PARITY and CROSS_CLIENT_PARITY are current through v0.14.0, and v0.14.0's fourteen new KNOWN_LIMITATIONS entries are the part worth reading — each one is a place the feature stops. A release notes document exists per version; four guard tests keep the copies honest. Held below 5 by the screenshot set: 1 of 7 captures done (`main-window.png`), because captures must come from a real Windows run. | 
| Release readiness | 4 | **v0.14.0 is published** (2026-11-23T03:13:12Z, tag run 45104415528) — the fourth release this pipeline produced rather than a hand-built one: tag shape checked, tag↔`<Version>` checked, a WhatsNew section required, the full suite run, NSIS from a pinned install action, three assets named to the updater's contract, `SHA256SUMS.txt` written ASCII/LF/no-BOM, the sums verified against the files on disk, the published exe smoke-tested, the release published, and then **every asset re-downloaded and re-hashed after publication**. The tag run's own step list shows all seven of those as green. Held at 4 by the one step CI cannot take: the in-app updater on a real Windows PC finding v0.14.0, downloading it, verifying it and installing it. | 

**Overall: 31/40** (v0.14.0: 31/40; v0.13.0: 31/40; v0.1.0: 25/40).

## What moved, and why

| Row | v0.1.0 | v0.13.0 | v0.14.0 | v0.15.0 | What changed |
| --- | --- | --- | --- | --- | --- |
| Correctness | 4 | 5 | 5 | 5 | Held. 846 test methods against 840, plus a drift guard for the Settings restyle. |
| Shell build health | 4 | 5 | 5 | 5 | Held. |
| Windows runtime | 2 | 3 | 3 | 3 | Held. The smoke test is the same evidence it was in v0.14.0. |
| Accessibility | 3 | 3 | 3 | 3 | Held. The restyle keeps the focus ring on every field (pinned by test); it is not a screen-reader pass. |
| Privacy / security | 4 | 4 | 4 | 4 | Held. |
| Reliability / recovery | 3 | 3 | 3 | 3 | **Held on purpose.** A restyle adds no failure observation. See the note above. |
| Documentation | 4 | 4 | 4 | 4 | Held; more documents, same missing screenshots. |
| Release readiness | 2 | 4 | 4 | 4 | Held. v0.15.0 is not yet published; the row still describes v0.14.0's pipeline run. |

## What would move the remaining rows

- **Windows runtime 3 → 4/5:** run `pwsh -File scripts/verify-windows.ps1` on Windows 10 and 11. It does the smoke test, scans the log for `[ERROR]` lines, walks the `RELEASE_CHECKLIST.md` items one at a time with a Pass / Problem / Skip verdict and writes `docs/verification/verify-<stamp>.md` — so the recording this row is waiting for is now a command rather than a discipline. The v0.14.0 items are in the checklist: the data-health repair button, a crash that offers safe mode, the field-pass export, the palette's five arguments, and Copy the next queued prompt — and the v0.15.0 Settings restyle (cards, pinned OK/Cancel, light-theme and High Contrast spot checks).
- **Accessibility 3 → 4:** a screen-reader pass over the four shell windows (Settings, Shortcuts, prompt pad, palette) plus a High Contrast run, recorded the same way. The audit test says the overrides exist; only a run says they work.
- **Reliability 3 → 4:** one deliberate crash, one network kill and one killed WebView2 process, each with the recovery path observed — and, since v0.14.0, the loop guard exercised for real by crashing three times inside ten minutes.
- **Documentation 4 → 5:** the six remaining captures in `docs/screenshots/CAPTURE.md`, taken from a real run.
- **Release readiness 4 → 5:** one real Windows run of the in-app updater against the published v0.14.0 — check, download, hash, install, relaunch, and a rollback for good measure.

## The honest floor

Nothing here is a claim about how the app *feels*. Nine rows of a table can be
green while the window is unusable on a machine with 125% scaling and a
second monitor — which is precisely the class of thing the on-machine pass
exists to find. Until it runs, the top of this file stays accurate and the
product stays **Unverified**, and that is the whole point of writing the
number down. A flat score on a release this size is not a failure to grade
new work; it is the difference between work that has been *built* and work
that has been *seen*.
