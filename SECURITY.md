# Security

This document describes how ArenaDesktop is built to keep a signed-in
Arena (arena.ai) session safe inside a desktop window, what it does
not protect against, and how to report a problem. It describes the
code as shipped — every claim below names the place that enforces it,
so it can be checked rather than trusted.

## Threat model

ArenaDesktop is a shell around untrusted web content. The threats it
is designed against are:

- **Untrusted web content.** Pages, redirects, pop-ups and downloads
  served to the WebView2 control may be hostile or compromised, even
  when they arrive over HTTPS from a legitimate flow (sign-in and
  connector authorisation redirect through several providers).
- **Malicious URIs.** Links, command lines, Jump List tasks and
  `arenadesktop://` links can be supplied by other programs, web
  pages, documents or messages. Any of them may be crafted to try to
  make the app open an arbitrary address, run an unintended action,
  or crash the parser.
- **Tampered updates.** An installer downloaded from a release page
  could be corrupted in transit or replaced by an attacker who
  controls a mirror or a network path. An unverified installer must
  never run.
- **A hostile local process with the user's own rights** is out of
  scope: like any per-user Windows app, ArenaDesktop cannot defend
  the session against code already running as the same user. The app
  lock (below) raises the bar for a person with momentary physical
  access; it is not a boundary against malware.

## No script injection

**Policy: ArenaDesktop never injects script into the Arena page, and
never executes code that arrived as web content.**

How it is enforced:

- The shell contains no call to
  `AddScriptToExecuteOnDocumentCreatedAsync` anywhere, and no
  `ExecuteScriptAsync` call that runs page- or user-supplied text.
  The single `ExecuteScriptAsync` call in the main window runs the
  constant `"1"` as a liveness ping for the renderer.
- No WebView2 host objects are exposed to page content in the main
  window (`AreHostObjectsAllowed` is never enabled and no
  `AddHostObjectToScript` call exists), and the main window subscribes
  to no `WebMessageReceived` handler: page content has no channel
  into the shell other than the WebView2 events the shell explicitly
  handles (navigation, new-window, permission, download, certificate
  error, context menu).
- The one component that renders streamed content,
  `ArenaRenderView`, displays only a document the app itself
  generates locally (`NavigateToString`): all streamed text is
  HTML-escaped before rendering, the page carries a restrictive
  Content-Security-Policy, HTML widget previews run in a sandboxed
  frame with scripts disabled, and its `WebMessageReceived` handler
  parses messages defensively — malformed messages are ignored,
  never acted on beyond reporting a named block action (Copy / Run /
  Expand) to the host. The rendering engine itself never executes
  streamed code; what "Run" means is the host window's decision.
- The microphone test window follows the same pattern: a locally
  generated page, a defensive message handler, nothing remote.

## WebView2 security settings

The main window's WebView2 is configured in `InitWebViewAsync`
(`src/ArenaDesktop/MainWindow.xaml.cs`); the second window applies
the same restrictions. The values that matter:

| Setting | Main window | Why |
| --- | --- | --- |
| `AreDevToolsEnabled` | `false` in Release builds (`true` only in Debug builds) | DevTools would let anyone script the live, signed-in session from the app's own window. |
| `IsBuiltInErrorPageEnabled` | `false` | Errors surface through the app's own plain-language UI, not a browser error page a hostile navigation could imitate. |
| `IsStatusBarEnabled` | `false` | No browser status bar to spoof link targets with. |
| `IsZoomControlEnabled` | `true` | Page zoom is a user accessibility feature; it grants the page nothing. |
| `AreBrowserAcceleratorKeysEnabled` | `true` | Standard browser keys (find, print, zoom) work; app shortcuts are handled by the shell first. |
| `IsScriptEnabled` | left at the WebView2 default (enabled) | Arena's own pages cannot run without script. The protection is not disabling Arena's script — it is that the shell adds none of its own and exposes no host bridge (see above). |
| `IsWebMessageEnabled` | default, but unused in the main window | No handler is subscribed and no host object is registered, so page messages have no effect. The render view and microphone test, which do subscribe, treat every message as untrusted input. |
| `IsPasswordAutosaveEnabled` / `IsGeneralAutofillEnabled` | the user's Settings choices (Privacy & security) | Autofill is the user's decision, applied per profile. |

Navigation is policy-checked, not assumed (`UrlPolicy` in
`ArenaDesktop.Core`, fully unit-tested):

- **Top-level navigation** loads only `https:` pages (plus `blob:`
  download URLs and `about:blank`). Plain HTTP and every non-web
  scheme (`file:`, `javascript:`, `data:`, …) are refused.
- **New-window requests** are stricter: only Arena and its known
  sign-in hosts (exact host or true subdomain — look-alikes such as
  `arena.ai.evil.com` never match) stay in the app; other http(s)
  links open in the user's default browser; everything else is
  refused.
- **Permissions** are granted automatically only for notifications
  and the microphone, and only when the requesting origin is exactly
  `https://arena.ai`. Everything else is refused rather than silently
  granted.
- **Certificate errors** get the app's own dialog, every time;
  "continue" is per-host and lasts only for the session.
- **Logs** record scheme, host and path only (`RedactForLog`) — never
  a query string or fragment, which can carry tokens or chat content.

### `arenadesktop://` links

Links are an allowlist, not a router (`LaunchIntent.ParseProtocolUri`
in `ArenaDesktop.Core`): a link can name only a fixed set of places
(the window, Home, Settings, Downloads, Battle Lab, the prompt pad,
the command palette, Help, a mode, or `profile/<id>` for a profile
that exists on this PC). A link can never carry a web address — any
query string, fragment, user-info or port is a refusal — and while
the app lock is engaged, links wait behind the lock like every other
intent. The parser is fuzz-tested with thousands of generated
malformed URIs (`UriFuzzingTests`): invalid input is refused, never
an unhandled exception.

### The app lock

The optional app lock stores only a PBKDF2-HMAC-SHA256 hash of the
credential (random salt, per-record iteration count) — never the
credential itself. A damaged lock record fails open with a logged
repair rather than locking the user out of their own app.

## Update verification

Updates are only ever offered from this project's GitHub Releases,
and only ever run after verification (`UpdateService` in the shell,
`Updates` / `SumsFile` in Core):

1. The checker reads the release list and picks the installer for
   this PC's architecture. A release with no `SHA256SUMS.txt`, or no
   entry in it for that installer, is **refused** — nothing is
   downloaded.
2. The installer downloads to a `.partial` file, never directly to a
   launchable name.
3. Its SHA-256 is computed and compared against the release's
   `SHA256SUMS.txt`. On mismatch the file is deleted, the user is
   told in plain words, and the installer is never launched. Only a
   verified file is renamed into place and offered to run, and only
   on the user's click.

**Code signatures:** ArenaDesktop's binaries are currently unsigned
(the project ships without a code-signing certificate), so Windows
SmartScreen may warn on first run and there is no publisher
signature to check. The SHA-256 verification above is therefore the
integrity guarantee for in-app updates; for a manual download,
compare the file's SHA-256 against the release's `SHA256SUMS.txt`
yourself (for example `Get-FileHash <file> -Algorithm SHA256` in
PowerShell) before running it, and only download from this
repository's Releases page. If signed builds are introduced, this
section will name the publisher and signature checks will be added
to the update path before the verified installer is launched.

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use
GitHub's private vulnerability reporting for this repository
(**Security → Report a vulnerability** on
`github.com/SeanShaffer88/ArenaDesktop`), or, if that is unavailable,
contact the maintainer through their GitHub profile. Include the
version, what you did, what happened, and what you expected — a
redacted log excerpt (⋯ → Help → View log) helps, and the in-app log
viewer runs its own redaction check before anything is copied.
You will get an acknowledgement, and a fix will ship in a normal
release with the changelog noting the security fix once it is safe
to describe publicly.
