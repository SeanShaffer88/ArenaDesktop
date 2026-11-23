# QUALITY.md — ArenaDesktop v0.12.0 (assessment, 2026-11-23; release row re-checked after publication)

Scored from evidence only. Every row names what it is scored on, and every
score says what would move it. **No row is scored Verified on a user's
Windows PC** — the on-machine pass in `RELEASE_CHECKLIST.md` has never run,
and nothing below pretends it has.

This replaces the v0.1.0 pre-release assessment (2026-10-27, 25/40). Much has
changed since: the build and the full test suite now run in CI on a Windows
runner for every pull request and every tag, four releases' worth of shell
surfaces have landed (Battle Lab pane, prompt pad with its Save to Battle Lab
handoff, command palette), and the release pipeline builds, hashes and
publishes by tag rather than by hand — twice now, end to end.

| Dimension | Score /5 | Evidence |
| --- | --- | --- |
| Correctness (Core logic) | 5 | The full Core suite is a **required gate** on every PR and on every release tag (`ci.yml`, `release.yml`): 729 test methods (651 `[Fact]`, 78 `[Theory]` with 477 inline cases) over URL policy, modes, settings/profiles, downloads, updates, the app lock, layouts, Battle Lab, the prompt pad and the palette. Three of them are drift guards rather than unit tests — WhatsNew↔CHANGELOG, Help.md↔HelpCatalog, and every ⋯ menu handler ↔ a palette command or a written exemption — so the class of bug they cover cannot come back unnoticed. |
| Shell build health | 5 | `dotnet build ArenaDesktop.sln -c Release` and then `dotnet publish -r win-x64 --self-contained` on the Windows runner, for every PR: green at v0.12.0 (post-merge run 45095928861, tag run 45096207713). Two real shell build failures were caught this way in v0.10.0/v0.11.0 and fixed rather than merged (an ambiguous `KeyEventArgs`, a CRLF-sensitive test). Caveat: the workflow fails on errors, not on warnings, and its log is not readable through the API — the **0-warnings** half of the gate is still the maintainer's local run. |
| Windows runtime | 3 | **Partially verified.** CI launches the published exe with `--smoke-test` on Windows Server 2025: the window is constructed and WebView2 is initialised, and a failure reports the app's own log line (run 45094003272 passed that step). That covers "it starts at all" — not keystrokes, menu behaviour, WebView2 rendering, the installer, or the update handshake, all of which remain **Unverified**. |
| Accessibility | 3 | Inherited shell pass plus this product's own work: named elements, polite/assertive live regions, a status line short enough to be spoken, the shortcut sheet and the command palette both keyboard-first with `FocusReturn` after every dialog. Unverified with a screen reader on real Windows; no High Contrast pass has been recorded since v0.1.0. |
| Privacy / security | 4 | URL policy and log redaction pinned by tests; no telemetry by construction; the app lock's scope stated wherever it is described; `arenadesktop://` links take no arguments (a test refuses `…/palette/settings`). Nothing in the palette, pad or Lab reaches the Arena page — no injection, no reading. The lock does not encrypt data on disk, and that is said in three places rather than implied. |
| Reliability / recovery | 3 | Recovery coordinator, watchdog and safe mode are inherited and test-covered in Core; a hung page and a dead network are handled by code paths with tests. The real behaviours — a genuine crash, a genuinely dead network, a killed WebView2 process — remain **Unverified** on Windows. |
| Documentation | 4 | README, CHANGELOG, WhatsNew, DECISIONS, KNOWN_LIMITATIONS, PARITY and CROSS_CLIENT_PARITY are current through v0.12.0 — the README in particular stopped naming v0.1.0's download assets and gained a "How complete is it?" section that points at this file rather than implying the app is finished. Each version has release notes; three guard tests keep the copies honest. Held below 5 by the screenshot set: 1 of 7 captures done (`main-window.png`), because captures must come from a real Windows run. |
| Release readiness | 4 | **v0.12.0 is published** (2026-11-23T01:46Z, run 45096207713) — the second release this pipeline produced rather than a hand-built one: tag shape checked, tag↔`<Version>` checked, a WhatsNew section required, the full suite run, NSIS from a pinned install action, three assets named to the updater's contract, `SHA256SUMS.txt` written ASCII/LF/no-BOM, the sums verified against the files on disk, the published exe smoke-tested, and then **every asset re-downloaded and re-hashed after publication**. Both releases so far shipped within minutes of the merge that preceded them, which is the pipeline behaving as designed. Held at 4 by the one step CI cannot take: the in-app updater on a real Windows PC finding v0.12.0, downloading it, verifying it and installing it. |

**Overall: 31/40** (v0.1.0: 25/40).

## What moved, and why

| Row | v0.1.0 | Now | What changed |
| --- | --- | --- | --- |
| Correctness | 4 | 5 | The suite is now a required gate on every PR and tag, not a run the maintainer remembered, and three drift guards make whole classes of staleness fail CI. |
| Shell build health | 4 | 5 | Build + publish + smoke test run on Windows for every change; the v0.9.0-era build break that reached `main` cannot recur silently. |
| Windows runtime | 2 | 3 | The published binary now starts, constructs its window and initialises WebView2 in CI. Interactive behaviour is still unverified. |
| Release readiness | 2 | 4 | The pipeline replaced the hand-built assets, verifies its own output, and has now published v0.11.0 and v0.12.0 end to end with every hash re-checked after upload. |
| Documentation | 4 | 4 | Kept, against three new guard tests; still missing the screenshots. |
| Accessibility, Privacy, Reliability | 3 / 4 / 3 | unchanged | No new evidence on either side — this assessment will not move them on hope. |

## What would move the remaining rows

- **Windows runtime 3 → 4/5:** the `RELEASE_CHECKLIST.md` on-machine pass on Windows 10 and 11 — sign-in, the three layouts, Battle Lab, the prompt pad and its draft plus the v0.12.0 Save to Battle Lab handoff (pane open, pane closed, at the 200-prompt cap), the palette (including the two conditional rows), downloads with progress/ETA, the app lock, an install → update → rollback cycle. One recording, not a rewrite.
- **Accessibility 3 → 4:** a screen-reader pass over the four shell windows (Settings, Shortcuts, prompt pad, palette) plus a High Contrast run, recorded the same way.
- **Reliability 3 → 4:** a deliberate crash, a network kill, and a WebView2 process kill, each with the recovery path observed rather than reasoned about.
- **Documentation 4 → 5:** the six remaining captures in `docs/screenshots/CAPTURE.md`, taken from a real run.
- **Release readiness 4 → 5:** one real Windows run of the in-app updater against the published v0.12.0 — check, download, hash, install, relaunch, and a rollback for good measure.

## The honest floor

Nothing here is a claim about how the app *feels*. Nine rows of a table can be
green while the window is unusable on a machine with 125% scaling and a
second monitor — which is precisely the class of thing the on-machine pass
exists to find. Until it runs, the top of this file stays accurate and the
product stays **Unverified**, and that is the whole point of writing the
number down.
