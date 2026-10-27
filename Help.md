# Help & troubleshooting

Plain-language fixes for the problems people actually hit with ArenaDesktop. Everything on these pages works offline — the only thing that needs the internet is reporting a problem on GitHub (the link at the bottom of the Help window, which opens in your browser).

## Arena keeps asking me to sign in

What you see: Arena asks you to sign in again every time you open ArenaDesktop, or signs you out in the middle of using it.

Why it happens: your sign-in lives in a cookie stored in your profile's data folder on this PC. If that cookie is cleared, expires, or can't be saved, Arena has no way to know it's you — so it asks again. Arena itself also signs sessions out from time to time; that part is normal and not something ArenaDesktop controls.

Try this:
1. Make sure you're signing in on the profile you expect — different profiles (Settings → Account profiles) have separate sign-ins and don't share cookies.
2. Sign in once more, then quit ArenaDesktop completely (tray → Exit) and start it again. A clean quit is when the session cookie gets saved.
3. If it keeps happening, clear this profile's site data and sign in fresh: use the button below to open Settings → Privacy & security, choose "All site data" for "This profile only", and Clear. Your chats are in your Arena account, not on this PC, so nothing is lost.
4. Check that nothing else is wiping the data: a very aggressive "PC cleaner" that empties app data folders will sign you out of everything, ArenaDesktop included.

## Voice dictation doesn't hear me

First, the plain fact: Arena has no voice or dictation button of its own (checked on the live site, October 2026) — dictation is not built into Arena. The way to speak a prompt is Windows' own dictation: click in Arena's prompt box and press Win+H, and Windows turns your speech into text right there.

If Win+H dictation isn't hearing you, the usual causes are a muted or wrong microphone, or Windows' permission for apps to use it.

Try this:
1. Check the microphone itself: the button below opens Settings → Privacy & security — choose "Test microphone…", allow a moment, and say something. The result tells you plainly whether the microphone was granted and whether sound actually arrived.
2. If the test says the request was denied, open Windows Settings → Privacy & security → Microphone and turn on both the master switch and "Let desktop apps access your microphone".
3. If the test says no sound was picked up, check Windows Settings → Sound → Input: the right microphone selected, not muted, and its volume up. Headsets with a hardware mute switch catch everyone once.
4. Click into Arena's prompt box before pressing Win+H — dictation types wherever the cursor is.

## I'm not getting notifications

What you see: Arena finishes a task (or a download completes) and nothing tells you — no pop-up, no sound, no tray change.

Why it happens: there are two notification systems. Arena's own alerts depend on arena.ai deciding to send one and on Windows allowing it. ArenaDesktop's own balloons (for example, "download finished") additionally respect Windows Do Not Disturb and the quiet-hours schedule you can set in Settings — and when they keep quiet, the log says why.

Try this:
1. Send yourself one: in Settings → Privacy & security, choose "Send test notification" (it's in the "Microphone & notification tests" group). The result line tells you if it was sent, and whether quiet hours or Windows Do Not Disturb is currently holding ArenaDesktop's automatic notifications back (a test you ask for is sent anyway).
2. Check Windows Settings → Notifications: ArenaDesktop (for its balloons) and your browser-style notifications from arena.ai must be allowed, and Focus Assist / Do Not Disturb schedules can hold everything until later.
3. The button below opens Settings → Tray & notifications: check quiet hours isn't covering the whole day by accident, and that "Tell me when a download finishes while the window is hidden" is on if it's download alerts you're missing.
4. Remember the app can't manufacture Arena alerts: if a task finishes without arena.ai sending a notification, there's nothing for Windows to show.

## The WebView2 Runtime is missing or too old

What you see: ArenaDesktop says the WebView2 Runtime is missing, shows a blank window, or Diagnostics says the runtime is far out of date.

Why it happens: ArenaDesktop draws Arena using Microsoft's WebView2 Runtime — the same engine Edge uses — instead of bundling a whole browser. It's already on Windows 11 and nearly all Windows 10 PCs, and Microsoft updates it with Edge, but a minority of PCs don't have it (or have a very old one).

Try this:
1. If ArenaDesktop showed the "runtime missing" message, use its button — it opens Microsoft's own installer page for the free Evergreen WebView2 Runtime. Install it (no restart needed) and start ArenaDesktop again.
2. If the runtime is merely old, let Edge update: open Edge → Settings → About, or run Windows Update — the runtime updates itself alongside.
3. The button below opens Diagnostics, which shows the exact runtime version found on this PC and whether it's missing, old, or healthy — the first thing worth checking before anything else.
4. Don't copy a "fixed version" runtime folder next to ArenaDesktop.exe to work around this: ArenaDesktop uses the Evergreen runtime on purpose (so security fixes arrive from Microsoft), and a fixed folder is ignored — Diagnostics will point it out if one's there.

## Downloads fail or never finish

What you see: a file Arena made starts downloading and then fails, stalls at a percentage, or vanishes without landing in your folder.

Why it happens: usually the connection dropped mid-download, the folder it was headed for isn't writable (a removed USB drive, a network folder that's offline), or the disk filled up. The Downloads window records failures with a reason whenever the web engine reports one.

Try this:
1. Open the Downloads window (⋯ menu → Downloads…): failed downloads from THIS session can be retried with the Retry button. Retry re-downloads into the folder your settings name now, so fix the folder first (next step) if that was the problem.
2. The button below opens Settings → Downloads: check the downloads folder points somewhere that exists and has space, and — if a Save-As dialog kept cancelling things — whether "Ask where to save each download" is on by accident.
3. Reconnect and retry: most "network failed" downloads succeed on a second try once Wi-Fi is back. If a download stalls at the same percentage every time, ask Arena to produce the file again — a fresh link restarts the transfer cleanly.
4. If downloads from one profile fail but another works, that profile may have its own downloads-folder override (Settings → Account profiles) pointing somewhere unusable.

## I forgot my app password

What you see: ArenaDesktop opens to the lock screen and the app password or PIN you set isn't accepted — or you've plain forgotten it.

Why it happens: the app lock guards the open app on this PC. Only a salted one-way hash of your password is stored — the password itself is never written anywhere, so nobody (including ArenaDesktop) can show it to you or recover it. What DOES exist is a tested way to remove the lock: it also resets your settings to defaults, but it does not sign you out, because sign-ins live in separate profile folders, not in the settings file.

Try this:
1. First, retry slowly — after 5 wrong tries in a row the lock pauses for about 30 seconds, which can look like a correct password being refused.
2. To remove the lock: quit ArenaDesktop completely (close the lock screen, and if a tray icon remains, right-click it → Exit).
3. Open the data folder by hand: press Windows+R, type %APPDATA%\ArenaDesktop and press Enter. (In portable mode the folder is the "data" folder beside ArenaDesktop.exe.) Once you're back in, the button below opens Settings → Data, which has an "Open data folder" button for next time.
4. Delete the file settings.json in that folder. Don't touch anything else — especially not the WebView2 profile folders under %LOCALAPPDATA%\ArenaDesktop, which hold your sign-ins.
5. Start ArenaDesktop: no lock, default settings, and your default profile is still signed in. Re-apply any settings you cared about (exporting them first is the habit that makes this painless next time), and set a new app password in Settings → Privacy & security if you still want one.

## The page won't load

What you see: a blank page, a spinner that never ends, or the failure screen saying you're offline, the name couldn't be found, or the connection failed — maybe right after the PC woke from sleep.

Why it happens: Arena is a website, so ArenaDesktop needs the internet to show it. A dropped connection, waking from sleep before Wi-Fi is back, a DNS hiccup, or (rarely) arena.ai itself being down all look similar from inside the app. When your connection comes back, ArenaDesktop retries the failed page once on its own.

Try this:
1. Wait a few seconds after waking the PC or reconnecting — Wi-Fi often needs a moment, and the automatic one-time retry fires when the connection returns.
2. Press the toolbar Reload button. If the failure screen is showing, its Retry button does the same thing.
3. Check another site in your regular browser: if nothing loads anywhere, it's the connection, not the app. If only Arena fails, it may be arena.ai itself — waiting is the fix.
4. If a proxy is involved (work/school networks), the button below opens Diagnostics, which shows the proxy Windows is configured with — ArenaDesktop never overrides it; WebView2 follows Windows.
5. Still stuck every launch? Start ArenaDesktop with --safe-mode (it opens at the home page and skips the update check), or use the guided reset in Settings → Data — it backs your settings up before changing anything.
