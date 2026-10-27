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
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "ArenaDesktop" SecMain
  SectionIn RO
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
; install/upgrade (read back by .onInit). Always runs; last section.
Section "-RecordChoices" SecRecord
  SectionIn RO
  SectionGetFlags ${SecDesktop} $0
  IntOp $0 $0 & 1
  WriteRegDWORD HKCU "Software\ArenaDesktop" "DesktopShortcut" $0
SectionEnd

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
