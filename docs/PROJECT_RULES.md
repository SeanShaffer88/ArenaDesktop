# ArenaDesktop — Project Rules

Prompt in force: The Elite Software Master Prompt v1.1 · Annexes: Windows desktop (Master Prompt as supporting detail)
Purpose in one line: A native Windows client for arena.ai with MuseDesktop-level robustness, plus first-class jumps to Arena's Battle / Direct / Side by Side / Leaderboard modes.
Frozen foundation: `src/ArenaDesktop.Core` = all testable logic (plain .NET 10, no Windows APIs); `src/ArenaDesktop` = thin WPF + WebView2 shell. Operation registry: LaunchIntent + CommandLine + ArenaModes — every capability dispatches identically from GUI, CLI and arenadesktop:// links.
Build: `dotnet build ArenaDesktop.sln -c Release` · Full gate: that build at 0/0 plus the FULL Core suite via the xUnit console runner (vstest cannot connect on the maintainer's VM) · Test corpus: `tests/ArenaDesktop.Core.Tests`
Budgets: startup and idle budgets are declared at the first field pass (measured on the maintainer's PC, not guessed here).
Coverage floors: Core logic is the coverage surface; new Core features ship with tests in the same slice (ArenaModes did).
Special rules for this project: never log a full URL (host + path only, `UrlPolicy.RedactForLog`); never log page content, cookies or tokens; WebView2 SDK pinned 1.0.4258.31 — bump deliberately; no winget, ever; public docs contain only factual product documentation; release assets are re-downloaded and hash-verified before a release is called shipped.
Known limitations live at: `KNOWN_LIMITATIONS.md` · Decision log: `DECISIONS.md` · QUALITY.md: `QUALITY.md`
