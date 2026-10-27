# QUALITY.md — ArenaDesktop v0.1.0 (pre-release assessment, 2026-10-27)

Scored from evidence only. Any dimension scored from a build/test run on
this VM says so; Windows runtime behaviour is Unverified and is not
scored as a pass anywhere below.

| Dimension | Score /5 | Evidence |
| --- | --- | --- |
| Correctness (Core logic) | 4 | Full Core suite green at the v0.1.0 gate: 697/697 via the xUnit console runner (build 0 errors / 0 warnings); fork base carried a large, previously CI-verified suite |
| Shell build health | 4 | `dotnet build -c Release` 0 errors / 0 warnings on the VM |
| Windows runtime | 2 | Unverified — no on-machine run yet; RELEASE_CHECKLIST.md pass is the gate to raise this |
| Accessibility | 3 | Inherited shell pass (focus visuals, High Contrast, announcements) — Unverified on Windows |
| Privacy / security | 4 | URL policy + log redaction pinned by tests; no telemetry by construction; app-lock scope stated honestly |
| Reliability / recovery | 3 | Recovery coordinator inherited and test-covered in Core; real crash/network behaviour Unverified |
| Documentation | 4 | README / CHANGELOG / DECISIONS / KNOWN_LIMITATIONS rewritten for this product; screenshot captures still owed (listed, not faked) |
| Release readiness | 2 | Assets built locally and hash-verified; publishing is the remaining step at assessment time; x86 + MSI gaps declared |

**Overall: 25/40.** No dimension is claimed Verified on Windows. The two
2-scores are the honest blockers: the on-machine field pass and the
published, hash-verified release. Both are follow-ups with owners
(field pass = the maintainer on his PC; release = the publish job),
not silent waivers.
