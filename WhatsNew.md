# What's new

The release notes ArenaDesktop shows on the first launch of a new version. This file is generated from CHANGELOG.md's current-version section at release time (see RELEASE_CHECKLIST.md) — do not edit it by hand; a test pins it to the changelog so the two can't drift.

## [0.3.1] — 2026-11-02

### Changed

- **Updating is hands-free once you accept it.** When an install runs while ArenaDesktop is open — window up, hidden in the tray, or behind the app lock — the installer now closes the app itself, installs over it, and reopens it when it finishes, with no prompts in between. The update window's Install button runs the installer silently to do this; running the installer yourself behaves the same way, except that the finish page's "Run ArenaDesktop" checkbox (pre-ticked when the app was open — untick it if you don't want the app back) is what reopens the app, as it fires only once the installer has closed. Uninstalling likewise closes a running instance before removing its files. Underneath is a new `--exit-for-update` command-line flag: it asks the running instance to close for an update, applies at once even while the app is locked, and simply exits when nothing is running.

