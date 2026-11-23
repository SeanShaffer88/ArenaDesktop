# QUALITY.md — ArenaDesktop v0.11.0 (assessment, 2026-11-23)

Scored from evidence only. Every row names what it is scored on, and every
score says what would move it. **No row is scored Verified on a user's
Windows PC** — the on-machine pass in `RELEASE_CHECKLIST.md` has never run,
and nothing below pretends it has.

This replaces the v0.1.0 pre-release assessment (2026-10-27, 25/40). Much has
changed since: the build and the full test suite now run in CI on a Windows
runner for every pull request and every tag, three releases' worth of shell
surfaces have landed (Battle Lab pane, prompt pad, command palette), and the
release pipeline builds, hashes and publishes by tag rather than by hand.

| Dimension | Score /5 | Evidence |
| --- | --- | --- |
| Correctness (Core logic) | 5 | The full Core suite is a **required gate** on every PR and on every release tag (`ci.yml`, `release.yml`): 729 test methods (651 `[Fact]`, 78 `[Theory]` with 477 inline cases) over URL policy, modes, settings/profiles, downloads, updates, the app lock, layouts, Battle Lab, the prompt pad and the palette. Three of them are drift guards rather than unit tests — WhatsNew↔CHANGELOG, Help.md↔HelpCatalog, and every ⋯ menu handler ↔ a palette command or a written exemption — so the class of bug they cover cannot come back unnoticed. |
| Shell build health | 5 | `dotnet build ArenaDesktop.sln -c Release` and then `dotnet publish -r win-x64 --self-contained` on the Windows runner, for every PR: green at v0.11.0 (run 45094003272). Two real shell build failures were caught this way in v0.10.0/v0.11.0 and fixed rather than merged (an ambiguous `KeyEventArgs`, a CRLF-sensitive test). Caveat: the workflow fails on errors, not on warnings, and its log is not readable through the API — the **0-warnings** half of the gate is still the maintainer's local run. |
| Windows runtime | 3 | **Partially verified.** CI launches the published exe with `--smoke-test` on Windows Server 2025: the window is constructed and WebView2 is initialised, and a failure reports the app's own log line (run 45094003272 passed that step). That covers "it starts at all" — not keystrokes, menu behaviour, WebView2 rendering, the installer, or the update handshake, all of which remain **Unverified**. |
| Accessibility | 3 | Inherited shell pass plus this product's own work: named elements, polite/assertive live regions, a status line short enough to be spoken, the shortcut sheet and the command palette both keyboard-first with `FocusReturn` after every dialog. Unverified with a screen reader on real Windows; no High Contrast pass has been recorded since v0.1.0. |
| Privacy / security | 4 | URL policy and log redaction pinned by tests; no telemetry by construction; the app lock's scope stated wherever it is described; `arenadesktop://` links take no arguments (a test refuses `…/palette/settings`). Nothing in the palette, pad or Lab reaches the Arena page — no injection, no reading. The lock does not encrypt data on disk, and that is said in three places rather than implied. |
| Reliability / recovery | 3 | Recovery coordinator, watchdog and safe mode are inherited and test-covered in Core; a hung page and a dead network are handled by code paths with tests. The real behaviours — a genuine crash, a genuinely dead network, a killed WebView2 process — remain **Unverified** on Windows. |
| Documentation | 4 | README, CHANGELOG, WhatsNew, DECISIONS, KNOWN_LIMITATIONS, PARITY and CROSS_CLIENT_PARITY are current through v0.11.0, each version has release notes, and three guard tests keep the copies honest. Held below 5 by the screenshot set: 1 of 7 captures done (`main-window.png`), because captures must come from a real Windows run. |
| Release readiness | 3 | The pipeline does the whole checklist's automated half and enforces its own contract: tag shape, tag↔`<Version>` equality, a WhatsNew section for that version, the full suite, NSIS from a pinned install action, a three-asset `SHA256SUMS.txt` written ASCII/LF/no-BOM, a re-download and re-verification of every published hash, and a refusal to overwrite an existing release. What is missing is the act itself: **this assessment was written minutes before v0.11.0's tag**, so no release has yet been published *by* this pipeline (v0.8.0 and earlier were built by hand). |

**Overall: 30/40** (v0.1.0: 25/40).

## What moved, and why

| Row | v0.1.0 | Now | What changed |
| --- | --- | --- | --- |
| Correctness | 4 | 5 | The suite is now a required gate on every PR and tag, not a run the maintainer remembered, and three drift guards make whole classes of staleness fail CI. |
| Shell build health | 4 | 5 | Build + publish + smoke test run on Windows for every change; the v0.9.0-era build break that reached `main` cannot recur silently. |
| Windows runtime | 2 | 3 | The published binary now starts, constructs its window and initialises WebView2 in CI. Interactive behaviour is still unverified. |
| Release readiness | 2 | 3 | The pipeline replaced the hand-built assets and verifies its own output; the first publication through it is this version. |
| Documentation | 4 | 4 | Kept, against three new guard tests; still missing the screenshots. |
| Accessibility, Privacy, Reliability | 3 / 4 / 3 | unchanged | No new evidence on either side — this assessment will not move them on hope. |

## What would move the remaining rows

- **Windows runtime 3 → 4/5:** the `RELEASE_CHECKLIST.md` on-machine pass on Windows 10 and 11 — sign-in, the three layouts, Battle Lab, the prompt pad and its draft, the palette (including the two conditional rows), downloads with progress/ETA, the app lock, an install → update → rollback cycle. One recording, not a rewrite.
- **Accessibility 3 → 4:** a screen-reader pass over the four shell windows (Settings, Shortcuts, prompt pad, palette) plus a High Contrast run, recorded the same way.
- **Reliability 3 → 4:** a deliberate crash, a network kill, and a WebView2 process kill, each with the recovery path observed rather than reasoned about.
- **Documentation 4 → 5:** the six remaining captures in `docs/screenshots/CAPTURE.md`, taken from a real run.
- **Release readiness 3 → 5:** v0.11.0's published assets, hash-verified on download, and the in-app updater finding them.

## The honest floor

Nothing here is a claim about how the app *feels*. Nine rows of a table can be
green while the window is unusable on a machine with 125% scaling and a
second monitor — which is precisely the class of thing the on-machine pass
exists to find. Until it runs, the top of this file stays accurate and the
product stays **Unverified**, and that is the whole point of writing the
number down.
