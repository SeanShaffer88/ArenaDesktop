; ArenaDesktop installer (NSIS) — per-user install, no admin required.
;
; Build (from the repo root, with the verified release publish staged):
;   makensis /DVERSION=0.1.0 /DARCH=x64 /DSTAGE=<dir containing the publish output> installer\ArenaDesktop.nsi
; Produces ArenaDesktop-Setup-v<VERSION>-<ARCH>.exe next to this script.
; ARCH is x64 or arm64 and must match the staged publish (v0.4.0+).
;
; Design notes (see DECISIONS.md, 2026-10-23):
; - Per-user install to %LOCALAPPDATA%\Programs\ArenaDesktop, HKCU uninstall
;   registration: matches the app's per-user data model and needs no UAC.
; - Uninstall removes the program and shortcuts but KEEPS the user's data
;   (%APPDATA%\ArenaDesktop settings/log and every profile folder under
;   %LOCALAPPDATA%\ArenaDesktop) unless the user answers Yes to the
;   "also delete" prompt — the same folders a zip user would delete by hand.
; - arenadesktop:// links (v1.0.0, S11 #99): the install registers the
;   per-user URL protocol under HKCU Software\Classes\arenadesktop —
;   no admin, and uninstall removes it ONLY when its command still
;   points at this install (a user who moved the app or registered
;   another copy keeps what the key says). The app also keeps the
;   registration in line with its Settings toggle at runtime, which
;   is how portable (zip) installs register: the toggle, not this
;   script, owns that case.
;
; Seamless update handshake (ported from MuseDesktop's R7D/R7D1 —
; see DECISIONS.md, 2026-11-02):
; - An update must be hands-free after the user accepts it: when an
;   ArenaDesktop instance is running, the installer closes it and
;   installs over it — no prompts anywhere in that logic, in the
;   wizard and in silent (/S) runs alike.
; - Detection is PROCESS-based (AD_CHECK_RUNNING): PowerShell's
;   Get-Process answers the literal question "is ArenaDesktop.exe
;   running", and the answer rides the exit code — no object names,
;   no handles, no plugin call shapes to get silently wrong. This
;   design is ported from MuseDesktop, whose first version of the
;   handshake detected the running app by opening its
;   single-instance mutex (System plugin, OpenMutexW) and NEVER
;   fired in the field (v1.8.4, 2026-10-27): the looked-up name
;   never matched a live instance, so no close was ever attempted
;   and the failure left no evidence behind. ArenaDesktop starts
;   with the process-based design that replaced it. Every step of
;   the close chain also prints a DetailPrint line, so a future
;   field failure leaves its story in the wizard's details view
;   instead of silence.
; - The close is a handshake first: the installer runs the INSTALLED
;   ArenaDesktop.exe --exit-for-update, which forwards an exit intent
;   to the running instance (Core: LaunchIntentKind.ExitForUpdate —
;   it applies immediately, even behind the app lock, and works with
;   the window hidden in the tray) and exits. Builds older than this
;   one don't know the flag: they tolerate it (unknown arguments are
;   never fatal) and merely wake their window, so the handshake
;   politely does nothing there and the fallbacks below do the close.
; - Fallbacks, in order, each followed by a wait: ask every
;   ArenaDesktop process to close its main window (PowerShell
;   CloseMainWindow — note a close-to-tray build hides instead of
;   exiting, which is why this is only a fallback), then force-kill
;   the process (taskkill /F). After the last wait the install
;   proceeds regardless: a file still locked surfaces as the ordinary
;   file error it always was.
; - Relaunch: the finish page's "Run ArenaDesktop" checkbox is the
;   ONLY interactive relaunch — MUI fires it as the wizard closes, so
;   the app starts after the installer is gone, and only when the box
;   is ticked. When the app WAS running at install start,
;   AdFinishPageShow pre-ticks the box (bring back what was open);
;   the user's untick always wins, and when nothing was running the
;   box is left exactly as MUI ships it. Silent (/S) runs have no
;   finish page, so the one exception: a hidden last section
;   relaunches the app when it was running AND the run is silent —
;   in-app installs have no checkbox, and the click on Install inside
;   the app is the consent.
; - The uninstaller runs the same close chain before removing files:
;   a running instance would otherwise lock the program folder (and,
;   when the user asks to delete data, the data folders too).
;
; Installer audit (v0.13.0, S9 #86):
; - Install folder: the directory page (MUI_PAGE_DIRECTORY) offers a
;   choice, still per-user with no admin, and InstallDirRegKey reads
;   the recorded folder back on upgrade, so an upgrade installs into
;   the SAME folder the user picked before — both predate this audit.
; - Desktop shortcut: an optional Components-page section, OFF by
;   default (unchanged). The audit's one fix: the choice is recorded
;   in HKCU "Software\ArenaDesktop" DesktopShortcut and pre-selected
;   on the next install/upgrade (.onInit), so upgrading no longer
;   silently drops a desktop shortcut the user once asked for.
;   Uninstall removes the registry key with the rest.

!ifndef VERSION
  !define VERSION "0.1.0"
!endif
!ifndef ARCH
  !define ARCH "x64"
!endif
!ifndef STAGE
  !error "Pass /DSTAGE=<publish output dir>"
!endif

Unicode true
Name "ArenaDesktop"
OutFile "ArenaDesktop-Setup-v${VERSION}-${ARCH}.exe"
InstallDir "$LOCALAPPDATA\Programs\ArenaDesktop"
InstallDirRegKey HKCU "Software\ArenaDesktop" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "ArenaDesktop"
VIAddVersionKey "FileDescription" "ArenaDesktop Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "MIT License"

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\ArenaDesktop.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Run ArenaDesktop"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
; Seamless update: pre-tick the finish page's Run box when the app
; was running (see AdFinishPageShow). The define must sit immediately
; before the FINISH insert — MUI consumes (and undefines) it in that
; page's SHOW hook, after the checkbox exists and before it is
; displayed.
!define MUI_PAGE_CUSTOMFUNCTION_SHOW AdFinishPageShow
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ── Seamless update state (see the design note) ──
Var AdWasRunning  ; 1 when an ArenaDesktop instance was running at install start
Var AdRunning     ; scratch: result of the last process check (1 = still running)
Var AdCheckCode   ; scratch: exit-code text of the last process check
Var AdTries       ; scratch: poll-loop counter

; Is ArenaDesktop.exe running? Sets $AdRunning. The answer rides
; PowerShell's EXIT CODE — 0 = an ArenaDesktop process exists, 3 =
; none does — because an exit code cannot be misread the way the
; mutex lookup this design replaced was (see the design note). Any
; other outcome (PowerShell missing, an "error" return) reads as
; "not running". ExecToStack pushes the exit code on top of the
; output: the first Pop takes the code, the second discards the
; (empty) output.
!macro AD_CHECK_RUNNING
  nsExec::ExecToStack 'powershell -NoProfile -WindowStyle Hidden -Command "if (Get-Process ArenaDesktop -ErrorAction SilentlyContinue) { exit 0 } else { exit 3 }"'
  Pop $AdCheckCode
  Pop $0
  ${If} $AdCheckCode == 0
    StrCpy $AdRunning 1
  ${Else}
    StrCpy $AdRunning 0
  ${EndIf}
!macroend

; Poll every 500 ms until no ArenaDesktop process remains, at most
; TRIES times (30 ≈ 15 s, 20 ≈ 10 s, 10 ≈ 5 s — each cycle also
; pays a PowerShell start, so real waits run a little longer).
; Leaves $AdRunning current.
!macro AD_WAIT_UNTIL_GONE TRIES
  StrCpy $AdTries ${TRIES}
  ${Do}
    !insertmacro AD_CHECK_RUNNING
    ${If} $AdRunning == 0
      ${ExitDo}
    ${EndIf}
    IntOp $AdTries $AdTries - 1
    ${If} $AdTries <= 0
      ${ExitDo}
    ${EndIf}
    Sleep 500
  ${Loop}
!macroend

; The close chain (see the design note), shared by the installer and
; the uninstaller; call only when a process check just said the app
; is running. PREFIX names the caller in the DetailPrint lines
; ("Seamless update" / "Uninstall"). No prompts anywhere in it: the
; handshake first, then ask Windows to close the main window, then
; force it — and proceed regardless of the outcome.
!macro AD_CLOSE_RUNNING PREFIX
  DetailPrint "${PREFIX}: ArenaDesktop is running — asking it to close."
  ; The handshake: the installed build forwards the exit intent and
  ; its second process exits at once; ExecWait returns when it has.
  ; (A missing exe just sets the error flag, and the waits/fallbacks
  ; below still run.)
  ExecWait '"$INSTDIR\ArenaDesktop.exe" --exit-for-update'
  !insertmacro AD_WAIT_UNTIL_GONE 30
  ${If} $AdRunning == 0
    DetailPrint "${PREFIX}: ArenaDesktop closed."
  ${EndIf}
  ${If} $AdRunning == 1
    DetailPrint "${PREFIX}: asking Windows to close ArenaDesktop."
    ; Fallback 1: ask every ArenaDesktop process to close its main
    ; window. ($$ is NSIS's literal-$ escape: PowerShell's $_ must
    ; reach PowerShell, not NSIS's variable expansion.)
    nsExec::ExecToStack 'powershell -NoProfile -WindowStyle Hidden -Command "Get-Process ArenaDesktop -ErrorAction SilentlyContinue | ForEach-Object { $$_.CloseMainWindow() | Out-Null }"'
    Pop $0
    Pop $0
    !insertmacro AD_WAIT_UNTIL_GONE 20
    ${If} $AdRunning == 0
      DetailPrint "${PREFIX}: ArenaDesktop closed."
    ${EndIf}
  ${EndIf}
  ${If} $AdRunning == 1
    DetailPrint "${PREFIX}: forcing ArenaDesktop closed."
    ; Fallback 2: force it. The user's data is never in the program
    ; folder, and the app autosaves as it goes; a forced close here
    ; loses nothing the graceful paths would have kept.
    nsExec::ExecToStack 'taskkill /F /IM ArenaDesktop.exe'
    Pop $0
    Pop $0
    !insertmacro AD_WAIT_UNTIL_GONE 10
    ${If} $AdRunning == 0
      DetailPrint "${PREFIX}: ArenaDesktop closed."
    ${Else}
      DetailPrint "${PREFIX}: ArenaDesktop is still running — continuing anyway."
    ${EndIf}
  ${EndIf}
!macroend

Section "ArenaDesktop" SecMain
  SectionIn RO

  ; ── Seamless update: close the running app BEFORE any file is
  ; copied ── (design note at the top). No prompts: this is invisible
  ; in the wizard and in silent runs alike. The DetailPrint lines are
  ; the evidence trail: open the install details view and the close
  ; tells its own story.
  !insertmacro AD_CHECK_RUNNING
  StrCpy $AdWasRunning $AdRunning
  ${If} $AdWasRunning == 1
    !insertmacro AD_CLOSE_RUNNING "Seamless update"
  ${EndIf}

  SetOutPath "$INSTDIR"
  File /r "${STAGE}\*.*"

  WriteRegStr HKCU "Software\ArenaDesktop" "InstallDir" "$INSTDIR"

  ; Start Menu shortcut
  CreateDirectory "$SMPROGRAMS\ArenaDesktop"
  CreateShortcut "$SMPROGRAMS\ArenaDesktop\ArenaDesktop.lnk" "$INSTDIR\ArenaDesktop.exe"

  ; arenadesktop:// protocol registration (S11 #99) — per-user, the
  ; standard URL-protocol key shape. The in-app toggle syncs the same
  ; key at runtime; the two writers always write the same values.
  WriteRegStr HKCU "Software\Classes\arenadesktop" "" "URL:ArenaDesktop Link"
  WriteRegStr HKCU "Software\Classes\arenadesktop" "URL Protocol" ""
  WriteRegStr HKCU "Software\Classes\arenadesktop\shell\open\command" "" '"$INSTDIR\ArenaDesktop.exe" "%1"'

  ; Uninstaller + Apps & Features registration (per-user)
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ArenaDesktop" "DisplayName" "ArenaDesktop"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ArenaDesktop" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ArenaDesktop" "Publisher" "Sean Shaffer"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ArenaDesktop" "DisplayIcon" "$INSTDIR\ArenaDesktop.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ArenaDesktop" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ArenaDesktop" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ArenaDesktop" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ArenaDesktop" "NoRepair" 1
SectionEnd

Section /o "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\ArenaDesktop.lnk" "$INSTDIR\ArenaDesktop.exe"
SectionEnd

; S9 #86: record the desktop-shortcut choice for the next
; install/upgrade (read back by .onInit). Always runs.
Section "-RecordChoices" SecRecord
  SectionIn RO
  SectionGetFlags ${SecDesktop} $0
  IntOp $0 $0 & 1
  WriteRegDWORD HKCU "Software\ArenaDesktop" "DesktopShortcut" $0
SectionEnd

; Seamless update: the silent-mode relaunch exception (see the
; design note). Interactively the finish page's Run checkbox is the
; ONLY relaunch and this section must not fire — a /S run has no
; finish page, so a hidden LAST section reopens the app when (and
; only when) it was running AND the run is silent. In-app installs
; are that silent path, and their consent is the Install click inside
; the app.
Section "-AdSilentRelaunch" SecSilentRelaunch
  SectionIn RO
  ${If} $AdWasRunning == 1
    ${If} ${Silent}
      Exec '"$INSTDIR\ArenaDesktop.exe"'
    ${EndIf}
  ${EndIf}
SectionEnd

; Seamless update: finish-page SHOW hook (wired by the define before
; MUI_PAGE_FINISH). MUI calls it after the finish page's controls
; exist and before the dialog shows: when the app was running at
; install start, pre-tick "Run ArenaDesktop" — the very SendMessage
; MUI itself uses to check the box by default ($mui.FinishPage.Run
; is the checkbox's handle, MUI_FinishPage state in Finish.nsh;
; BM_SETCHECK/BST_CHECKED come from WinMessages.nsh via MUI2). The
; user's untick afterwards always wins; when nothing was running,
; the box keeps its stock state and this does nothing.
Function AdFinishPageShow
  ${If} $AdWasRunning == 1
    SendMessage $mui.FinishPage.Run ${BM_SETCHECK} ${BST_CHECKED} 0
  ${EndIf}
FunctionEnd

; S9 #86: restore the recorded desktop-shortcut choice (see the audit
; note at the top) so the Components page reflects what this user
; chose last time. First install: no value recorded, the section
; keeps its default (off). Defined after the sections on purpose —
; section constants only exist once their Section command is read.
Function .onInit
  ReadRegDWORD $0 HKCU "Software\ArenaDesktop" "DesktopShortcut"
  ${If} $0 == 1
    SectionSetFlags ${SecDesktop} 1
  ${EndIf}
FunctionEnd

LangString DESC_SecMain ${LANG_ENGLISH} "ArenaDesktop program files (required)."
LangString DESC_SecDesktop ${LANG_ENGLISH} "Adds a ArenaDesktop shortcut to your desktop."
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} $(DESC_SecMain)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_SecDesktop)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  ; Seamless update: close the running app BEFORE anything is
  ; removed — a live instance locks the program files (and, when the
  ; user asks below to delete the data, the profile folders the web
  ; view holds open). Same chain as the installer's, same no-prompt
  ; shape; the DetailPrint lines tell the story in the details view.
  !insertmacro AD_CHECK_RUNNING
  ${If} $AdRunning == 1
    !insertmacro AD_CLOSE_RUNNING "Uninstall"
  ${EndIf}

  ; arenadesktop:// registration (S11 #99): remove it only when it is
  ; still THIS install's command — never another copy's.
  ReadRegStr $0 HKCU "Software\Classes\arenadesktop\shell\open\command" ""
  ${If} $0 == '"$INSTDIR\ArenaDesktop.exe" "%1"'
    DeleteRegKey HKCU "Software\Classes\arenadesktop"
  ${EndIf}

  ; Program files and shortcuts
  Delete "$SMPROGRAMS\ArenaDesktop\ArenaDesktop.lnk"
  RMDir "$SMPROGRAMS\ArenaDesktop"
  Delete "$DESKTOP\ArenaDesktop.lnk"
  RMDir /r "$INSTDIR"

  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ArenaDesktop"
  DeleteRegKey HKCU "Software\ArenaDesktop"

  ; User data is kept by default (settings, log, and every sign-in profile).
  MessageBox MB_YESNO|MB_ICONQUESTION \
    "Also delete ArenaDesktop's settings and sign-in data?$\n$\nThis removes %APPDATA%\ArenaDesktop and the profile folders under %LOCALAPPDATA%\ArenaDesktop, signing you out of Arena on this PC. Your chats are in your Arena account and are not affected." \
    IDNO keepData
    RMDir /r "$APPDATA\ArenaDesktop"
    ; Profile folders: WebView2, WebView2-<id>, ... under %LOCALAPPDATA%\ArenaDesktop
    RMDir /r "$LOCALAPPDATA\ArenaDesktop"
  keepData:
SectionEnd
