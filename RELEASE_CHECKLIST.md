# Release checklist

Run before publishing any release. CI covers the automated half; this is the on-machine half that automation can't prove.

## Cutting a release

Releases are built by `.github/workflows/release.yml`, on Windows runners, from the tagged commit. The steps were previously done by hand on one machine (the automated half below); that is now the pipeline's job, so a release is one tag.

1. **Bump `<Version>`** in `src/ArenaDesktop/ArenaDesktop.csproj`, add the matching `## [x.y.z]` section to `CHANGELOG.md`, and regenerate `WhatsNew.md` from it (a Core test pins the two together).
2. **Write `docs/release-notes/vx.y.z.md`** when the release needs to say more than its own changelog section — a version jump, a migration note, a known gap. Without this file the workflow uses the changelog section verbatim. It never edits either file.
3. **Tag the merge commit on `main` and push the tag:** `git tag vx.y.z && git push origin vx.y.z`. Nothing is published before this.
4. **The workflow verifies the tag against the code before building:** the tag must be `v<major>.<minor>.<patch>`, it must equal `<Version>`, and `WhatsNew.md` must carry that version's section. A mismatch fails in under a minute instead of shipping an app that cannot find its own release notes.
5. **It then runs the full gate** (build at 0/0 and the FULL Core suite — never a subset, per AGENTS.md), publishes both architectures, builds both NSIS installers from the same staged folder the zips use, writes `SHA256SUMS.txt` over all four, verifies the sums against the files on disk, smoke-tests the real exe, publishes the release, and **re-downloads the published assets and re-verifies every hash** — the checklist's "a release is not shipped until its assets are hash-verified".
6. **The pipeline refuses to overwrite a published release.** Re-running for an existing tag is a hard stop; delete the release or bump the version.

The five assets are `ArenaDesktop-v<version>-windows-{x64,arm64}.zip`, `ArenaDesktop-Setup-v<version>-{x64,arm64}.exe`, and `SHA256SUMS.txt`. The installer names are a contract the in-app updater depends on; `ReleaseWorkflowTests` pins the workflow to them.

### Node the pipeline needed (learned cutting 0.6.0)

- **`windows-latest` is Windows Server 2025 and no longer ships NSIS.** The workflow installs it with `negrutiu/nsis-install@v2` and uses that action's `instdir` output directly.
- **`choco install nsis` is not a substitute.** choco only updates the *machine* PATH, and a later step's process environment is a snapshot taken at runner start — so `makensis` never appears. That failure looks exactly like "makensis is not on PATH" one step after a green install step.
- **NSIS resolves a relative `OutFile` against the `.nsi` file's directory**, not the working directory, so the installer lands in `installer/`; the workflow moves it into the staging folder.
- **`SHA256SUMS.txt` must be written as ASCII with LF and no BOM.** `SumsFile.Parse` takes the first 64 bytes of each line as the hash, so a BOM makes every line unreadable and the release is refused by its own updater.

## Automated gate (run by the maintainer's agent, every slice)

- [ ] `dotnet build ArenaDesktop.sln -c Release` — 0 errors, 0 warnings
- [ ] FULL Core test suite via the xUnit console runner — all passing, never a filtered subset
- [ ] Release assets re-downloaded after publishing and SHA-256 verified against the locally built files

## On-machine pass — v0.1.0 (Windows 10/11, x64 and ARM64 where available)

- [ ] Fresh install: installer runs per-user with no admin prompt; app opens to Arena, setup wizard appears once
- [ ] Sign in with Google and with email; close and reopen — still signed in; restart the PC — still signed in
- [ ] Arena modes: ⋯ → Arena modes → each of Battle / Direct Chat / Side by Side / Leaderboard / Agent / Image / Code / Video / Web Search lands on its own Arena page; Ctrl+1…Ctrl+9 do the same; tray Arena modes and the Jump List Battle/Leaderboard tasks work; `--battle` / `--agent` / `arenadesktop://leaderboard` on a running instance navigate it; Quick ask from the tray focuses the page ready to type
- [ ] Battle flow: run a battle, vote, identities reveal; Direct Chat with a named model; Side by Side with two named models
- [ ] Upload a PDF and an image to Arena — drag-and-drop onto the page works, the frame lights on drag-over the shell
- [ ] Download a generated image: progress % / speed / ETA in the status bar, taskbar progress, pause/resume/cancel, retry after a forced failure, history entry opens / shows in folder / deletes
- [ ] Voice input: microphone grant prompt appears only for arena.ai; Quick ask focuses the page and the status line is honest about the next step
- [ ] Tray: close-to-tray, suspend after the configured timeout (and NOT during a download), Ctrl+Alt+A shows/hides, single-instance (second launch wakes, no duplicate)
- [ ] Profiles: create a second profile, sign in differently, switch from menu and tray, per-profile shortcut launches it, data sizes shown in Settings
- [ ] Privacy: clear cache only (still signed in), clear cookies only (signed out), permissions manager reflects Arena's saved choices, app lock asks at startup and on Lock now, wrong-password pause works
- [ ] Recovery: kill the renderer (Task Manager) — one automatic reload, no loop; go offline mid-load — one retry when back; safe mode (hold Shift) starts at home with no update check
- [ ] Updates: with a newer release published, Check for updates finds it, downloads with progress, verifies the fingerprint, installs on demand only; What's New shows once
- [ ] Seamless update (in-app): with the app running and the window up, Install closes ArenaDesktop with no wizard and no further prompts, installs over it, and reopens it on the new version, still signed in; repeat with the app hidden in the tray (it must never appear while closing) and with the app lock on (it closes without an unlock; the lock asks again at the reopened launch)
- [ ] Seamless update (installer run by hand): with the app running, the installer closes it and the finish page's "Run ArenaDesktop" box is pre-ticked — the app starts only after the installer window closes, and unticking the box means it does not start; with nothing running, the box keeps its stock state (nothing about it changes) and a silent (/S) run does not launch the app either; a silent (/S) run WITH the app running does reopen it
- [ ] Uninstall: both choices (keep data / delete data) behave as labelled; reinstall finds the kept sign-in; uninstalling while the app is running closes it first and removes the program files completely
- [ ] Screenshots per docs/screenshots/CAPTURE.md
