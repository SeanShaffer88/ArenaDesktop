# Roadmap

Where ArenaDesktop is going, in public. This roadmap covers the
completeness program (plan: `docs/COMPLETENESS_PLAN.md`). Items are
listed with their honest status — "in development" means code exists
on a development branch and is passing the project's full gate; it
does not mean released. Released state is always the
[latest release](https://github.com/SeanShaffer88/ArenaDesktop/releases)
and `CHANGELOG.md`.

## Distribution

**GitHub Releases only** — the Setup installer, the portable zip,
and `SHA256SUMS.txt` from the
[releases page](https://github.com/SeanShaffer88/ArenaDesktop/releases).
There are no package-manager packages (no winget, Chocolatey, or
Scoop listings) and none are planned; that is a standing decision,
not an oversight.

## How to influence this list

- **Vote with 👍 reactions** on
  [feature-request issues](https://github.com/SeanShaffer88/ArenaDesktop/issues?q=is%3Aissue+label%3Aenhancement).
  The most-reacted requests are picked up first when a slice opens.
- File a feature request with the template — say what you are trying
  to do, not only what you want built.
- Bug reports use the bug template; security issues follow
  `SECURITY.md` (private reporting, never a public issue).

## A. Portable sign-in — in development

Portable mode now carries the WebView2 sign-in profiles in the
`data` folder beside the exe, so the whole app is one carryable
folder. Honest limit: WebView2 cookies are protected by Windows for
the user and PC that created them, so a copy taken to another PC or
user still asks for sign-in there. Cross-machine continuity is
covered by E's encrypted backup (settings + profile preferences),
not by moving cookies.

## B. Your own models (BYOK + local) — in development

- **Bring your own key** for OpenAI, Anthropic and Google, each
  called with its real API shape; keys are stored protected by
  Windows (DPAPI) in their own file, never in settings or exports.
- **Local models** via Ollama and LM Studio (localhost,
  OpenAI-compatible), no key required.
- **Scratch comparison**: one prompt fanned out to your BYOK/local
  targets side by side, exportable as Markdown. Arena models are
  not comparison targets — Arena has no API, and ArenaDesktop never
  reads the Arena page; Arena-side comparison stays in Battle Lab.

## C. Developer integration — in development, with two decided limits

- Native **Git panel** (status, diff, stage, commit, push, pull,
  branch/repo switching) and a **terminal**, both over a
  user-chosen project folder and the user's own git credentials.
- **Task notifications** for long-running local work (terminal, Git,
  downloads, comparisons) via the notification centre (D).
- **Decided: no file-system bridge into Arena's web agent.** That
  would require injecting a bridge into the arena.ai page, which the
  project's no-script-injection policy forbids. The agent keeps
  Arena's remote sandbox; the desktop adds its value natively.
- **Decided: connector OAuth stays in the web view**, where Arena's
  own flow runs; the app does not handle Arena account tokens.

## D. Productivity & UX — in development

- **Tabs** (state tabs over the one browsing view; pages reload on
  switch — concurrent live pages remain "Open in new window").
- **Library search & export** across the app's own stores (Battle
  Lab, prompt pad, downloads) as JSON/Markdown. Arena conversations
  live on Arena's servers and are not the app's to export.
- **Notification centre**: durable, per-category, read/unread.
- **Accessibility & language**: README details the accessibility
  support; string externalization has begun (`ShellStrings`) —
  English is the only shipped language today, stated plainly.
- **Script commands**: user-defined palette commands run in the
  terminal — the local automation/plugin surface. No third-party
  code is loaded into the app.

## E. Privacy & trust — in development

- **Encrypted profile backup/restore** (passphrase, PBKDF2 +
  AES-GCM) with a change preview; sign-ins are never in the file.
- **Audit log**: profile switches, downloads, data clearing,
  backups, permission denials — viewable in Settings → Privacy.
- **Permissions transparency**: the exact WebView2 exposure table
  in `SECURITY.md` and Settings, with revoke paths.

## Not planned

- Package-manager distribution (see Distribution above).
- Reading, scraping, or automating the Arena page beyond what the
  web view itself does — the no-script-injection policy in
  `SECURITY.md` is permanent.
- Telemetry of any kind.
