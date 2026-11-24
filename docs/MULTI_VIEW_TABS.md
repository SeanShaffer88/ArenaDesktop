# Multi-view tabs — design (D1 follow-up)

Status: implemented in the shell on branch `feat/multi-view-tabs`.
Windows runtime: **Unverified** (built and Core-tested on the
maintainer's VM; no on-machine pass has run).

## Why

v0.20.0 shipped *state tabs*: the Core `TabSet` is durable and
per-profile, but the main window hosted every tab on the one
`Browser` WebView2, so switching tabs navigated that view and the
page reloaded. A Battle, an Agent task and the Leaderboard could
not stay live at once. This slice gives each tab its own live
WebView2 view.

## Audit — where the single view was assumed

`MainWindow.xaml.cs` referenced the XAML `Browser` control from
~100 places. Grouped by concern:

- **Chrome (active page only):** address bar (`OnSourceChanged`),
  window title (`OnTitleChanged` → `_pageTitle`, attention signal),
  nav buttons, zoom label + zoom steps/presets, find bar
  (`core.Find`), print, fullscreen element, arrival notices,
  session page counts, the overlay, the hung-page watchdog ping.
- **Per-page facts kept globally:** `_lastGoodUrl` (crash restore),
  `_micGrantedThisSession` (suspend/watchdog heuristic, S4 #35),
  the navigation `_history` (a session-wide record — stays global
  on purpose), `_pageTitle`.
- **Policy handlers (must be identical on every view):**
  `OnNavigationStarting` (UrlPolicy block), `OnNewWindowRequested`
  (classify → in-app navigate / external / block),
  `OnPermissionRequested` (notifications + microphone from exactly
  https://arena.ai), `OnDownloadStarting` (destination policy +
  tracker), `OnServerCertificateErrorDetected` (per-host,
  session-only decisions), `OnContextMenuRequested` (link actions),
  `OnProcessFailed` (recovery coordinator).
- **Shared services keyed off the view:** the privacy gateway
  (operates on the profile through the running view — the profile
  is shared, so the active view is the right handle), the hidden
  `RetryBrowser` (built from the main view's environment), tray
  suspend/resume (`TrySuspendAsync` on the one view), memory
  snapshots (taken from the view's environment), settings live-apply
  (autofill/password — profile-wide values), theme background.
- **Tab plumbing:** `SyncActiveTabFromPage` (copies the view's
  Source/title into the active tab), `NavigateToTab` (navigates the
  one view on switch), startup restore (`InitWebViewAsync` resolves
  the start page once), `RecreateWebViewAsync` (S8 #71: replaces the
  `Browser`/`RetryBrowser` controls — the rest of the window only
  ever talks to the fields, which this design keeps true).

## Architecture

- **One environment per profile, always.** `CoreWebView2Environment`
  is created once (`_env`) over the profile's user-data folder.
  Every tab view and the retry view are created from it, so sign-in
  is shared across tabs exactly as it was across windows. Never one
  environment per tab.
- **`TabView` (shell, `TabViews.cs`)** owns one tab's live view: the
  WPF `WebView2` control, a `Ready` flag, and the per-view facts
  that used to be global — `LastGoodUrl`, `MicGrantedThisSession`,
  `Suspended`. MainWindow keeps `Dictionary<string, TabView>` keyed
  by tab id.
- **`Browser` stays the name of the active view.** The field now
  points at the active tab's control; switching tabs swaps the
  control inside `WebFocusRing` and repoints the field, so the
  ~100 existing call sites (find, zoom, print, mode jumps, palette,
  CLI/protocol launches, Simple Mode paths) keep meaning "the page
  the user is looking at" unchanged.
- **Lazy creation.** A tab's view is created on first activation
  and navigates to the tab's persisted URL. Startup creates only
  the active tab's view (with the existing start-page resolution);
  restored background tabs cost nothing until visited.
- **Live-view cap with LRU eviction (Core).** `TabSet` tracks live
  views in activation order (`LiveViewIds`, most-recent last) and
  caps them at `TabSet.MaxLiveViews` (6 of at most 12 tabs).
  `RegisterLiveView` returns the least-recently-used *background*
  tab whose view the shell must dispose; the evicted tab falls back
  to a state tab — its URL/title persist and its page reloads on
  next activation, exactly the v0.20.0 behaviour. The active tab's
  view is never evicted. The bookkeeping is Core logic with tests;
  the shell only obeys it.
- **Event routing.** Every per-view handler resolves its `TabView`
  from `sender` (the `CoreWebView2`, or the control for
  `ZoomFactorChanged`). Durable tab state (URL/title sync) updates
  for *every* view; window chrome updates only when the sender is
  the active view. Downloads from a background tab go through the
  same destination policy and tracker, and the notification centre
  records which tab they came from.
- **No suspension of background views.** Background tabs are never
  `TrySuspendAsync`'d on switch — an Agent task or a streaming chat
  must not be frozen for being backgrounded, and WebView2 gives no
  reliable "this page is streaming" signal to suspend selectively
  (the watchdog's audio/mic heuristics cover the visible page
  only). The memory cost of a live view is bounded instead by the
  6-view cap + eviction. Tray hide keeps its existing behaviour:
  every live view is offered `TrySuspendAsync` (WebView2 declines
  for active media/calls) and resumed on show.
- **Recovery.** A renderer failure reloads the *failing* view at
  its own last good URL. A browser-process exit kills the shared
  environment, so every view is dead: the recreate path disposes
  all views + the retry view, builds a new environment, recreates
  the active tab's view at its last page, and leaves the other
  tabs uncreated — they rebuild lazily at their persisted URLs on
  next activation (their in-page state is lost; that is recorded
  in KNOWN_LIMITATIONS).
- **Unchanged:** per-profile tab persistence and sanitization
  (additive Core fields only — settings `SchemaVersion` stays 1),
  profile switch restarts the app, single-instance and
  protocol/CLI launches act on the active tab as today, Simple
  Mode gating is untouched (it never referenced views), and
  `SecondaryWindow` ("Open in new window") still gets the shared
  environment.

## Security invariants

No script injection, no host objects, no `WebMessageReceived`
bridge — nothing about the page-facing surface changes. Every new
view is wired through the one `WireView` path, so the settings
(DevTools off in Release, no status bar, no built-in error page)
and all policy handlers are identical on every tab by construction.
