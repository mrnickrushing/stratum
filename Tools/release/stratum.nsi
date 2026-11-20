; Stratum for Windows: a per-user installer (no administrator needed), with Start menu and desktop shortcuts
; and an entry in Settings > Apps to uninstall it. Built by Tools/release/package.sh:
;   makensis -DVERSION=0.2.0 -DSRC=Builds/Windows -DOUT=Builds/Stratum-0.2.0-Setup.exe Tools/release/stratum.nsi

Target amd64-unicode          ; the game is 64-bit only, so the installer is too
SetCompressor /SOLID lzma
RequestExecutionLevel user

!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!define NAME "Stratum"
!define PUBLISHER "Rushing Technologies"
!define UNINST "Software\Microsoft\Windows\CurrentVersion\Uninstall\Stratum"

Name "${NAME}"
OutFile "${OUT}"
InstallDir "$LOCALAPPDATA\Programs\Stratum"
InstallDirRegKey HKCU "Software\Stratum" "InstallDir"
BrandingText "${PUBLISHER}"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${NAME}"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "FileDescription" "${NAME} installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "${PUBLISHER}"

!define MUI_ICON "stratum.ico"
!define MUI_UNICON "stratum.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Welcome to Stratum ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "This puts Stratum on this computer, with a shortcut on the desktop and in the Start menu.$\r$\n$\r$\nYour worlds are kept when you update.$\r$\n$\r$\nClick Install to carry on."
!define MUI_FINISHPAGE_RUN "$INSTDIR\Stratum.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Play Stratum now"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Stratum" Main
  SectionIn RO
  ; An update replaces the game's files (worlds live in AppData\LocalLow, not here).
  RMDir /r "$INSTDIR\Stratum_Data"
  SetOutPath "$INSTDIR"
  File /r /x "*_DoNotShip" "${SRC}\*.*"
  File "stratum.ico"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateShortcut "$DESKTOP\Stratum.lnk" "$INSTDIR\Stratum.exe" "" "$INSTDIR\stratum.ico"
  CreateDirectory "$SMPROGRAMS\Stratum"
  CreateShortcut "$SMPROGRAMS\Stratum\Stratum.lnk" "$INSTDIR\Stratum.exe" "" "$INSTDIR\stratum.ico"
  CreateShortcut "$SMPROGRAMS\Stratum\Uninstall Stratum.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "Software\Stratum" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINST}" "DisplayName" "${NAME}"
  WriteRegStr HKCU "${UNINST}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINST}" "DisplayIcon" "$INSTDIR\stratum.ico"
  WriteRegStr HKCU "${UNINST}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINST}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  ; Takes the game away; the saved worlds stay unless the player deletes them.
  Delete "$DESKTOP\Stratum.lnk"
  RMDir /r "$SMPROGRAMS\Stratum"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNINST}"
  DeleteRegKey HKCU "Software\Stratum"
SectionEnd
