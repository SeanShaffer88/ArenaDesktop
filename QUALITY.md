# QUALITY.md — ArenaDesktop v0.18.0 (assessment, 2026-11-23)

Scored from evidence only. No row is scored Verified on a user's Windows PC: the on-machine pass in `RELEASE_CHECKLIST.md` has still not run. Static checks, CI compilation, and a Windows Server smoke test are useful evidence, but none demonstrates the interactive experience on a user's desktop.

**Overall: 31/40, held deliberately.** v0.17.0 adds left-pane Settings section navigation, while the v0.16.0 Settings search, profile-preference transfer, restore preview, and accessibility wiring remain in place. CI and static checks do not supply the missing observations of Narrator, keyboard flow, text scaling, Light theme, Windows High Contrast, or the new navigation on a real desktop. No score moves on the strength of declarations alone.

| Dimension | Score /5 | Evidence |
| --- | ---: | --- |
| Correctness (Core logic) | 5 | The full Core suite is a required gate on every PR and release tag. The current suite has 860 test methods (776 `[Fact]`, 84 `[Theory]`, 501 `[InlineData]` cases), including the Settings navigation/search wiring alongside profile-transfer validation, restore-preview summaries, accessibility declarations, and release/docs drift guards. The profile transfer model rejects malformed files, unsupported versions and unsupported colors; it preserves even an invalid custom start-page string for the app's existing safe Home fallback, and applies preferences without overwriting profile identity or session history. The v0.17.0 PR gate passed on Windows CI (run 45234933817); the release workflow gate remains required before publishing. |
| Shell build health | 5 | `ci.yml` builds the solution and runs the full Core suite on Windows for each PR; the release workflow repeats the gate and smoke-tests the published executable. v0.15.0's PR and release runs passed; v0.16.0's full build and Core suite passed on PR #39 (run 45231408684), and v0.17.0's CI checks passed on PR #40 (run 45234933817). The v0.17.0 release-tag run remains required before publishing. The workflow fails on errors, not warnings; a local 0-warning claim is not made. |
| Windows runtime | 3 | **Partially verified.** CI's Windows Server smoke test previously confirmed window construction and WebView2 initialization for the v0.15.0 release. That does not verify v0.16.0's new Settings keyboard interactions, profile-folder workflow, v0.17.0's section navigation, or ordinary Windows 10/11 use. These interactive behaviors remain **Unverified** until the on-machine checklist runs. |
| Accessibility | 3 | Static checks now pin named Settings/Shortcuts roots, polite search/status live regions, the assertive Settings-load warning, continuous tab navigation, search Up/Down/Enter wiring and preserved focus visuals. Prompt pad and Command palette already expose names and live status. This is wiring evidence, not a Narrator session: keyboard-only traversal, announcement quality, scaling and High Contrast remain **Unverified** on a real desktop. |
| Privacy / security | 4 | The profile-settings JSON contains preferences only, not cookies or site data; it keeps the target profile id and session history on import. The user-facing site-data folder action warns against copying while WebView2 is running, and the backup guide explains Windows DPAPI's same-user/same-PC limitation. No one-click site-data archive or cross-PC sign-in migration is claimed. Existing URL policy, log redaction and no-telemetry guarantees remain covered by tests. |
| Reliability / recovery | 3 | Held on observed recovery evidence. The restore preview validates a settings backup before offering to load it and changes only the Settings draft after confirmation; Cancel still leaves the draft alone. This is not an observed recovery from a real damaged profile or Windows failure. |
| Documentation | 4 | CHANGELOG and WhatsNew carry identical v0.17.0 sections; the checklist now includes real-Windows navigation, appearance and accessibility checks. The screenshot set remains incomplete: only `main-window.png` is captured; Settings Dark/Light/High Contrast/search and the other listed captures require real Windows runs. |
| Release readiness | 4 | v0.16.0 is published and its release pipeline passed the full gate, Windows smoke test, packaging and re-download hash verification (run 45232462197). v0.17.0's PR is merged and CI passed; the release-tag build, publication, and asset verification remain pending. |

## What would move the score

- **Windows runtime 3 → 4/5:** run `pwsh -File scripts/verify-windows.ps1` on Windows 10 and 11, then complete the v0.16.0 profile-transfer items and v0.17.0 Settings navigation/accessibility items in `RELEASE_CHECKLIST.md`. The script writes `docs/verification/verify-<stamp>.md`; only record actions actually observed.
- **Accessibility 3 → 4:** complete the Narrator and keyboard-only pass over Settings, Shortcuts, Prompt pad and Command palette, plus 150% text scaling and Windows High Contrast; record both failures and passes.
- **Reliability 3 → 4:** observe a deliberate crash, a network interruption and a killed WebView2 process with their recovery paths, rather than relying only on wiring tests.
- **Documentation 4 → 5:** capture every remaining image listed in `docs/screenshots/CAPTURE.md` from a real Windows session.
- **Release readiness 4 → 5:** complete the v0.17.0 release workflow and the in-app updater check on a real Windows PC.

## Evidence labels

- **Verified:** directly observed in the environment named (for example, CI build/test or a Windows desktop run).
- **Partially verified:** one bounded part was observed; the rest is named explicitly.
- **Unverified:** no direct observation yet. A test that code or a declaration exists does not upgrade this label.
