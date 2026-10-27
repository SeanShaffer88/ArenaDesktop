# Release checklist

Run before publishing any release. CI covers the automated half; this is the on-machine half that automation can't prove.

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
- [ ] Voice input: microphone grant prompt appears only for arena.ai; Quick dictate focuses the page and the status line is honest about the next step
- [ ] Tray: close-to-tray, suspend after the configured timeout (and NOT during a download), Ctrl+Alt+A shows/hides, single-instance (second launch wakes, no duplicate)
- [ ] Profiles: create a second profile, sign in differently, switch from menu and tray, per-profile shortcut launches it, data sizes shown in Settings
- [ ] Privacy: clear cache only (still signed in), clear cookies only (signed out), permissions manager reflects Arena's saved choices, app lock asks at startup and on Lock now, wrong-password pause works
- [ ] Recovery: kill the renderer (Task Manager) — one automatic reload, no loop; go offline mid-load — one retry when back; safe mode (hold Shift) starts at home with no update check
- [ ] Updates: with a newer release published, Check for updates finds it, downloads with progress, verifies the fingerprint, installs on demand only; What's New shows once
- [ ] Uninstall: both choices (keep data / delete data) behave as labelled; reinstall finds the kept sign-in
- [ ] Screenshots per docs/screenshots/CAPTURE.md
