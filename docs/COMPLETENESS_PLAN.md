# Completeness program — plan (v0.20.0 development line)

Audit + design for the A–F completeness brief, written before code
(Phase 0). Each item: current state found in the code, the design that
was implemented (or the honest finding where a slice stopped), files
touched, and limitations. Status lines are updated as slices land.

Ground rules that bind every slice: Core holds all testable logic, the
shell stays thin; no new external dependencies (.NET BCL only);
SECURITY.md's no-script-injection policy is law — nothing in this
program adds script injection, host objects, or a message bridge into
arena.ai pages; never log full URLs; Windows runtime is Unverified
throughout (no on-machine pass has run).

## A. Portable sign-in — Status: implemented (same-machine)

**Current state.** Portable mode (`portable.txt` beside the exe,
`SettingsLocation.Resolve` in Core `SettingsExperience.cs`) moves the
settings root to `<exe>\data` — settings, Battle Lab, prompt pad,
downloads history, logs, backups, updater slots all ride along because
they are built from `AppPaths.RootDir`. The one holdout is the WebView2
profile: `AppPaths.WebViewDataDir` / `AppPaths.ProfileDir` hardcode
`%LOCALAPPDATA%\ArenaDesktop\WebView2[-<id>]`, so a portable copy on
the same machine still signs in from the machine's folder, and a copy
taken to another machine has no session at all.

**Design.** Branch both `AppPaths` methods on `Location.IsPortable`:
portable → `Path.Combine(RootDir, Profiles.FolderName(id))` (default
profile folder name `WebView2`, siblings `WebView2-<id>`), installed →
today's `%LOCALAPPDATA%` paths, byte-identical. Every consumer (env
creation in `MainWindow.InitWebViewAsync`, `WebPrivacyGateway`,
profile deletion, cache cleanup, Settings site-data viewer) already
funnels through these two methods, so no call sites change. A pure
resolver `SettingsLocation.WebViewDir(root, localAppData, id)` keeps
the decision testable. No migration: an installed profile stays where
it is; a portable launch starts its own beside-exe profile (same
no-migration rule portable settings already follow).

**Honest limitation (also in README + KNOWN_LIMITATIONS).** WebView2
cookies are DPAPI-bound to the Windows user + machine. A portable
folder carried to another PC or another Windows user does **not**
carry a live sign-in — Arena will ask for sign-in there, and no export
format can change that (the cookie encryption key never leaves the
original machine). What this slice delivers is same-machine
portability: everything the app owns, including the sign-in session,
lives in one folder you can move around, back up, or run from a USB
stick on that machine. Cross-machine continuity is section E's
encrypted backup of settings + profile metadata (not cookies).

**Files.** `src/ArenaDesktop.Core/AppPaths.cs`,
`src/ArenaDesktop.Core/SettingsExperience.cs` (resolver), tests,
README/KNOWN_LIMITATIONS wording.

## B1. BYOK providers — Status: implemented (Core + Settings surface)

**Current state.** Nothing: no provider code, no API-key storage, no
chat client, no HttpClient in Core. The only crypto primitives are
`EncryptedExport` (PBKDF2-HMAC-SHA256 210k + AES-256-GCM envelope) and
`AppLock` (PBKDF2 hash records).

**Design.** Core `Providers.cs` + `ChatClient.cs`:
- `ChatProviderKind { OpenAI, Anthropic, Google, Ollama, LMStudio,
  OpenAiCompatible }`. Each provider keeps its **real** API shape:
  OpenAI/Ollama/LM Studio/custom use chat-completions JSON; Anthropic
  uses `/v1/messages` with `x-api-key` + `anthropic-version` and its
  own system/message split; Google uses
  `:generateContent`/`:streamGenerateContent` with `contents[]` parts
  and the key in a header. Request builders and response/SSE parsers
  are pure functions, unit-tested against recorded-shape payloads.
- `ChatClient` takes an injected `HttpMessageHandler` (tests use a
  fake handler — no live network in tests), exposes
  `CompleteAsync` and `StreamAsync` (SSE line parsing per provider),
  and `ListModelsAsync` per provider.
- **Key storage.** Core `ApiKeyVault` (`api-keys.json` beside the
  settings) stores each key only as an opaque protected blob produced
  by an injected `ISecretProtector`. The shell's protector uses
  Windows DPAPI (`CryptProtectData`, current-user scope, via P/Invoke
  — no new package) for installed and portable installs alike (DPAPI
  is per user+machine either way); Core also ships a passphrase
  protector (PBKDF2 + AES-GCM, the `EncryptedExport` pattern) behind
  the same seam, exercised in tests. Keys are never written to settings.json,
  never logged, and are masked in every UI surface.
- Shell: a Settings → Providers section (add/remove provider, paste
  key, pick model) and CLI parity is intentionally not offered for
  key entry (keys never travel through command lines); the chat
  surface is the scratch comparison window (B3).

**Limitations.** No token counting/billing; provider error bodies are
surfaced truncated and redacted; model lists depend on each
provider's own endpoint. Windows runtime Unverified.

## B2. Local models — Status: implemented (with B1)

Ollama (`http://localhost:11434/v1`) and LM Studio
(`http://localhost:1234/v1`) are provider presets on the same
OpenAI-compatible path, no key required (a placeholder key is sent
only if the server demands one). Base URLs are user-editable for
non-default ports. Limitation: the app does not start or manage the
local server; a stopped server surfaces as a plain connection error.

## B3. Scratch comparison — Status: implemented (Core + window)

**Design.** Core `ScratchCompare.cs`: `CompareTarget` (provider +
model + display name), `ScratchCompare.RunAsync(prompt, targets,
Func<target, prompt, ct, Task<ChatReply>>)` fans out concurrently
(capped), collects per-target reply/error/elapsed, never throws for
an individual target's failure. The shell window lets the user pick
configured BYOK/local targets, type one prompt, and read results
side by side. **Arena models are not targets**: Arena offers no API,
and reading results out of the Arena page would need the script
bridge SECURITY.md forbids. Arena-side comparison stays in the
existing Battle Lab (which is Arena's own Battle mode in the web
view, plus the app's prompt queue/journal).

## C. Agent / developer integration — Status: implemented (native side)

**Current state.** No git code, no terminal, no process runner with
captured output anywhere; the only hidden-console process is the
profile-switch relaunch. Long-running work follows per-feature
patterns (DownloadTracker events; UpdateService async +
IProgress + CancellationToken).

**Design.**
- **Project folder.** New setting `ProjectFolder` (a user-chosen
  local folder). Everything in section C is scoped to it.
- **Git.** Core `GitPanel.cs`: argument builders + output parsers
  (porcelain status, unified diff, log) behind an injected
  `IProcessRunner`; operations: status, diff (staged/unstaged),
  stage/unstage, commit, push, pull, branch list/switch, repo list
  is the folder itself + its immediate git subfolders. The shell
  implements the runner over `System.Diagnostics.Process` (git CLI,
  hidden, captured, cancellable) and a Git window renders
  status/diff and the commit/push controls. No credentials are ever
  handled by the app — push/pull use the user's own git credential
  setup, non-interactively (`GIT_TERMINAL_PROMPT=0`), so a missing
  credential fails plainly instead of hanging.
- **Terminal.** Core `ShellCommands.cs`: a command queue model
  (command text, working folder, streamed output lines, exit code,
  cancellation). Shell `TerminalWindow` hosts commands as processes
  in the project folder with streamed output and a Stop button;
  completion raises a notification-centre entry (D3) and a tray
  balloon under the existing notification policy.
- **Connector management / task notifications.** Managing Arena
  connector OAuth natively would mean handling Arena account tokens
  outside the web view — **not implemented, by design**: Arena
  connectors stay in the web view where Arena's own OAuth flow runs.
  What is native: notifications when long-running *local* tasks
  (terminal commands, git push/pull, downloads, scratch comparisons)
  finish or need attention, via the notification centre (D3).

**Decided limitation — no file-system bridge into the web agent.**
Exposing local files to Arena's Agent Mode would require injecting a
bridge into the arena.ai page (script injection / host objects /
WebMessageReceived), which SECURITY.md forbids, or a protocol Arena
does not offer. ArenaDesktop therefore does not bridge local files
into the web agent; the agent keeps Arena's own remote sandbox, and
the desktop adds its value natively (git, terminal, notifications)
beside it. Recorded in DECISIONS.md.

## D1. Tabs — Status: Core model + shell state tabs (see limitation)

**Current state.** One `Browser` WebView2, referenced from ~100
places in a 5,191-line MainWindow (navigation, find, zoom, downloads,
watchdog, retry view, suspend). SecondaryWindow already detaches a
page into its own window on the same environment.

**Design.** Core `TabModel.cs`: `BrowserTab { Id, Title, Url }`,
`TabSet` (open/close/activate/reorder, closed-tab stack for
Ctrl+Shift+T, per-profile persistence as part of settings —
`OpenTabsByProfile`). Shell: a tab strip above the content area;
each tab is a session entry on the one browsing view — switching
tabs saves the current tab's URL/title and navigates to the target
tab. New tab (Ctrl+T), close tab (Ctrl+W), reopen closed
(Ctrl+Shift+T), and "open in new window" reuses SecondaryWindow for
a truly concurrent page.

**Honest limitation (as shipped in v0.20.0).** These are *state
tabs*, not concurrent live pages: Arena pages reload on tab switch
(the single-view architecture is load-bearing for
find/downloads/watchdog/retry, and a multi-view rewrite of
MainWindow was judged too risky to land blind). Concurrent live
pages remain available via SecondaryWindow. The Core model is
view-agnostic, so a future multi-view host can adopt it unchanged.
Recorded in KNOWN_LIMITATIONS.

**Follow-up (unreleased, branch `feat/multi-view-tabs`).** The
multi-view host has landed, exactly on the view-agnostic model:
each tab owns a live WebView2 view on the profile's one shared
environment, created lazily, capped at six live views with Core
LRU eviction back to state tabs. Design + audit:
`docs/MULTI_VIEW_TABS.md`. The state-tab limitation in
KNOWN_LIMITATIONS is replaced by the live-view cap and the
browser-process-crash cost. Windows runtime Unverified.

## D2. Chat export & search — Status: implemented (app's own data)

**Current state.** The app stores **no** Arena conversation content
(by design — it never reads the page). Its own stores: Battle Lab
prompts + journal, prompt pad recents/draft, downloads history,
field-pass records. Search exists per store; no cross-store search
or unified export.

**Design.** Core `LibrarySearch.cs`: a tokenized inverted index over
the app's own stores (prompt name/category/body, journal notes,
pad entries, download file names) with the house prefix-match rule;
`LibraryExport.cs`: one structured export of those stores as JSON
(versioned document) or Markdown. Shell: a Library window (search
box + results + export buttons). Arena conversations themselves are
not searchable/exportable — they live on Arena's servers; the plan
does not scrape the page (forbidden) and says so in the window.

## D3. Notification centre — Status: implemented

Core `NotificationCentre.cs`: `NotificationEntry { Id, Category
(Battle, Agent, Download, Connector, System), Title, Body, CreatedUtc,
Read }`, a capped store (`notifications.json` beside settings),
per-category enable toggles in settings, unread count, mark
read/clear. Producers: downloads (existing completion path also
records an entry), terminal/git task completion, scratch comparison
completion, update availability. Shell: a Notifications window +
tray/⋯ entry with unread count; balloons remain governed by the
existing `NotificationPolicy` (DND/quiet hours) — the centre is the
durable record balloons never were.

## D4. Accessibility & i18n groundwork — Status: groundwork

**Current state.** Accessibility is already substantial (live
regions, focus visuals, shortcut remapping, high contrast via
`ThemeManager.HighContrastActive` → SystemColors). i18n is zero:
all strings are hard-coded English; the csproj pins
`SatelliteResourceLanguages=en`.

**Design (groundwork, honestly scoped).** Core `ShellStrings.cs`: a
string table for the shell's own chrome (tab strip, new windows,
notifications, git/terminal panels) with an `en` default and a
culture selector that falls back to `en` — new surfaces in this
program take their strings from the table, proving the pipeline
without pretending the whole 25-window shell is localized. A
`UiLanguage` setting (default: follow system, options English for
now) is stored and applied to new surfaces. High contrast already
exists and is documented in README's accessibility paragraph
(previously undetailed); new windows follow the same ThemeManager
brushes and carry AutomationProperties names. Full-shell
localization remains future work and is stated as such.

## D5. Plugin surface — Status: implemented (script commands)

Core `ScriptCommands.cs`: user-configured entries
`{ Name, Command, Arguments, WorkingFolder }` stored in settings;
each becomes a command-palette command in a generated family with
id prefix `script:` (the established generated-family pattern —
`mode:`, `zoom-preset:`), executed by the shell in the project
folder through the same runner as the terminal, with output to the
terminal window. Validation: name uniqueness, non-empty command,
no shell metacharacter smuggling beyond what the user typed
themselves (these are the user's own commands on their own machine;
the palette shows the full command text before running). This is a
local automation surface, not a plugin binary API — no third-party
code is loaded into the app process, ever.

## E. Privacy & trust — Status: implemented

- **Encrypted profile backup.** Core `ProfileBackup.cs`: a versioned
  payload { settings JSON, per-profile metadata snapshot } encrypted
  with the `EncryptedExport` envelope (PBKDF2 + AES-GCM, new magic
  `ARENADESKTOP-PROFILE-BACKUP`). Restore decrypts, previews changes
  (reusing `SettingsRestorePreview`), then applies. **Cookies and
  live sign-in are not in the backup** (DPAPI machine binding — see
  A); the backup UI says so in plain words.
- **Audit log.** Core `AuditLog.cs`: append-only JSONL store
  (`audit-log.jsonl` beside settings, capped/rotated) recording
  profile switches, downloads completed, data-clearing, backup /
  restore, export events, and permission-policy denials — timestamps
  and event kinds + non-sensitive detail only (hosts, never URLs;
  file names, never contents). Viewer: Settings → Privacy section
  list + the Log Viewer gains an "Audit" source. Entries are written
  by the shell at the existing choke points (SwitchToProfile,
  DownloadTracker completion, ClearAsync aftermath, backup/restore).
- **Permissions transparency.** SECURITY.md gains the exact
  exposure table (camera: always denied by policy today; microphone:
  auto-granted to https://arena.ai only; notifications: same;
  clipboard: WebView2 default, page-driven; geolocation: denied),
  and Settings → Privacy shows the same table (from a Core model,
  `PermissionsTransparency`) with per-profile revoke instructions
  (the existing permissions manager over `WebPrivacyGateway`).

## F. Ecosystem — Status: implemented (ROADMAP + issue templates only)

**Updated direction (SEAN, during the program): no package-manager
distribution at all** — no winget (the standing rule), and no Scoop
or Chocolatey either. Distribution is GitHub Releases only, and
ROADMAP.md says so.

- `ROADMAP.md` (repo root): A–E with honest status, the decided
  limits, distribution as GitHub Releases only, and how to vote
  (GitHub issue 👍 reactions).
- `.github/ISSUE_TEMPLATE/`: bug report + feature request forms and
  a config linking the roadmap and the security policy; feature
  requests explain reaction voting, mirrored in ROADMAP.md.

## Order of work

Phase 0 (this doc) → A → B1 → B2 → B3 → C → D1 → D2 → D3 → D4 →
D5 → E → F. Full gate (Release build, zero errors/warnings + full
Core suite via the xUnit console runner) after every slice, one
atomic commit per slice. CHANGELOG `[Unreleased]`, DECISIONS.md and
KNOWN_LIMITATIONS.md move with each slice.
