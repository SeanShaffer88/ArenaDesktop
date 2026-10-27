# Known limitations

Honest list of what ArenaDesktop does not do or has not proven yet. Kept current as work proceeds.

## Verification status

- **Windows runtime: Unverified.** v0.1.0 is built and fully gated on a Linux VM (build 0 errors / 0 warnings + the full Core test suite). Nothing about real Windows behaviour — sign-in persistence, tray, downloads, voice, updates — is claimed until the RELEASE_CHECKLIST.md pass runs on a real Windows machine.
- Arena's live routes (Battle home, `/leaderboard`) were set from the site's public structure on 2026-10-27; a live in-app confirmation is part of the same field pass.

## Product limits

- **Inside Direct Chat and Side by Side, you pick the model(s).** ArenaDesktop takes you to the right page; the model choice at the top of those pages is Arena's own picker — the app never injects script into the page, so it cannot choose for you.
- **Arena has no voice dictation.** There is no microphone button in Arena's prompt box (checked on the live site, 2026-10-27). For voice, click in the prompt box and use Windows dictation (Win+H); ArenaDesktop's Quick ask (tray / `--ask`) just gets you to the page, focused and ready to type.
- **Portable mode does not carry your sign-in.** Settings, download history and logs travel in the `data` folder beside the exe; the WebView2 sign-in profile stays in `%LOCALAPPDATA%`.
- **The app lock guards the open app only.** It does not encrypt your sign-ins or settings on disk, and the app says so wherever the lock is described.
- **Rollback covers one previous version**, and only a version that was itself reached through the in-app updater.
- **Update checks need a published release to find.** Before the first ArenaDesktop release exists on GitHub, Check for updates reports it couldn't find release information.
- **Unsigned binaries.** There is no code-signing certificate; Windows SmartScreen may warn on first run (More info → Run anyway).
- **Architectures:** x64 and ARM64 only in this version. No x86 (32-bit) build and no MSI installer yet.
- **No screenshots yet.** README captures come from real Windows runs only; the capture list is in docs/screenshots/CAPTURE.md.
- **Downloads history never stores source URLs** — a downloaded file can be opened, shown in its folder, or deleted, but not re-downloaded from history after the session's retry window.
