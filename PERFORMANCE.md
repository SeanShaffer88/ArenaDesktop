# Performance

Baseline measurements for ArenaDesktop, how they were taken, and how
to reproduce them. Figures are recorded only from real runs on a
Windows machine — nothing in the table below is estimated, and a
metric nobody has measured yet says so rather than carrying a
plausible-looking number.

## Baseline

**Status: not yet measured.** The first field pass on a Windows
machine (the same pass `RELEASE_CHECKLIST.md` already calls for) has
not run, so no baseline exists yet. When it runs, record the results
in this table — machine, build and date included, because a number
without its machine is not reproducible.

| Metric | Result | Machine / build | Date |
| --- | --- | --- | --- |
| Cold startup time (launch → interactive window) | *Not yet measured* | — | — |
| Working set after 5 minutes idle | *Not yet measured* | — | — |
| CPU during a prompt-copying workflow | *Not yet measured* | — | — |

Targets the baseline will be judged against (from the project rule
sheet: budgets are declared at the first field pass, measured, not
guessed — these are the ceilings that pass sets):

- Cold startup should feel instant on a mid-range PC; a baseline
  above ~3 seconds to an interactive window is a problem to fix, not
  a number to accept.
- Idle working set is dominated by the WebView2 renderer; the app's
  own contribution is what the tray-suspend feature exists to cut
  (Settings → Behavior → "Save memory while hidden in the tray").

## Methodology

Everything below runs on the Windows machine under test, against a
Release build installed from the release assets (or a local
`dotnet build ArenaDesktop.sln -c Release` output — say which in the
record). Close other ArenaDesktop instances first, and note the
machine (CPU, RAM, Windows build), the app version, and whether the
run is a first-ever launch (profile creation makes it untypical) or
a normal one.

### 1. Cold startup time

1. Reboot, sign in, and wait for the desktop to settle (a minute is
   enough for sign-in background work to stop dominating).
2. Start a stopwatch — or better, use the app's own instrumentation:
   ArenaDesktop times its startup milestones internally, and
   **⋯ → Help → Diagnostics → Copy report** includes the startup
   timings for the current launch. Launch the app, open Diagnostics,
   and record the reported time to the interactive window.
3. Repeat three times, quitting fully between runs (tray → Exit —
   closing the window only hides it). Record all three and the
   median.

Cross-check with Windows Performance Recorder if the numbers look
wrong: `wpr -start CPU -start DiskIO`, launch the app, wait until
the window is interactive, `wpr -stop startup.etl`, and read the
process's CPU-by-process and first-window times in Windows
Performance Analyzer.

### 2. Working set after 5 minutes idle

1. Launch the app, sign in if needed, and leave the window open on
   the Arena home page, untouched, for 5 minutes.
2. In Task Manager's Details tab, sum the Working set of
   `ArenaDesktop.exe` and its `msedgewebview2.exe` children (the
   renderer's memory is part of the app's real footprint; reporting
   only the shell process would flatter the result).
3. Or, from a terminal:
   `dotnet-counters monitor --process-id <ArenaDesktop pid> System.Runtime`
   and record `Working Set` alongside the process tree total.
4. Repeat with the window hidden in the tray for 5 minutes, and note
   the difference — that second figure is what the tray-suspend
   setting is for, and both belong in the record.

### 3. CPU during a prompt-copying workflow

The typical workflow this app exists for: copy prompts from Battle
Lab / the prompt pad and paste them into a Battle.

1. Open Battle Lab with a library of at least a few prompts. Start
   `dotnet-counters monitor --process-id <pid> System.Runtime` (its
   `CPU Usage (%)` line), or record a WPR CPU trace as above.
2. Over about two minutes: copy the next queued prompt, paste it
   into the Battle page, run it, copy the following prompt, and
   repeat — roughly one copy/paste cycle every 15–20 seconds, at a
   normal working pace.
3. Record the average and peak CPU across the whole tree
   (`ArenaDesktop.exe` plus its WebView2 children) over the run,
   excluding the initial page load.

### Recording a result

Add a row per metric to the table above (or a dated table below it
when re-measuring a later version), with: app version, machine,
Windows build, install type (installer / portable / local build),
and the raw repeats, not only the summary. A later version whose
numbers move more than ~10% against the recorded baseline is a
regression to explain in the changelog, not a number to quietly
replace.
