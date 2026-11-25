; Lumo – instalátor (NSIS)
Unicode true
SetCompressor /SOLID lzma

!define APPNAME "Lumo"
!ifndef VERSION
  !define VERSION "4.0.0"
!endif
!define PUBLISHER "Bolek98"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Lumo"

Name "${APPNAME} ${VERSION}"
OutFile "..\dist\Lumo-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\Lumo"
InstallDirRegKey HKCU "Software\Lumo" "InstallDir"
RequestExecutionLevel user
BrandingText "Lumo ${VERSION} · Bolek98"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "FileDescription" "Instalátor Lumo"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "© Bolek98"

!include "MUI2.nsh"
!define MUI_ICON "..\lumo.ico"
!define MUI_UNICON "..\lumo.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Vítej v instalaci Lumo"
!define MUI_WELCOMEPAGE_TEXT "Lumo je malý ostrůvek nahoře nebo dole na obrazovce – hudba, počasí, teploty PC, směny, časovač, poznámky a další.$\r$\n$\r$\nInstalace nepotřebuje práva správce.$\r$\n$\r$\nKlikni na Další."
!define MUI_FINISHPAGE_RUN "$INSTDIR\Lumo.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Spustit Lumo"
!define MUI_COMPONENTSPAGE_SMALLDESC

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Czech"

Section "Lumo (povinné)" SecMain
  SectionIn RO
  ; zavřít běžící Lumo, aby šlo přepsat
  nsExec::Exec 'taskkill /IM Lumo.exe /F'
  ; starý Ostrůvek (předchůdce Lumo) – zavřít a vypnout jeho autostart
  nsExec::Exec 'taskkill /IM Ostrovek.exe /F'
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Ostrovek"
  Sleep 600

  SetOutPath "$INSTDIR"
  File "..\dist\Lumo.exe"
  File "..\lumo.ico"

  WriteRegStr HKCU "Software\Lumo" "InstallDir" "$INSTDIR"
  WriteUninstaller "$INSTDIR\Odinstalovat.exe"

  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\lumo.ico"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "URLInfoAbout" "https://bolek98.github.io"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Odinstalovat.exe"'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" 80000

  CreateDirectory "$SMPROGRAMS\Lumo"
  CreateShortcut "$SMPROGRAMS\Lumo\Lumo.lnk" "$INSTDIR\Lumo.exe" "" "$INSTDIR\lumo.ico"
  CreateShortcut "$SMPROGRAMS\Lumo\Odinstalovat Lumo.lnk" "$INSTDIR\Odinstalovat.exe"
SectionEnd

Section "Zástupce na ploše" SecDesktop
  CreateShortcut "$DESKTOP\Lumo.lnk" "$INSTDIR\Lumo.exe" "" "$INSTDIR\lumo.ico"
SectionEnd

Section "Spouštět s Windows" SecAutostart
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Ostrovek"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Lumo" '"$INSTDIR\Lumo.exe"'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} "Samotný program Lumo."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Ikona Lumo na ploše."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAutostart} "Lumo se spustí samo po zapnutí počítače."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  nsExec::Exec 'taskkill /IM Lumo.exe /F'
  Sleep 600
  Delete "$INSTDIR\Lumo.exe"
  Delete "$INSTDIR\lumo.ico"
  Delete "$INSTDIR\Odinstalovat.exe"
  RMDir "$INSTDIR"
  Delete "$DESKTOP\Lumo.lnk"
  Delete "$SMPROGRAMS\Lumo\Lumo.lnk"
  Delete "$SMPROGRAMS\Lumo\Odinstalovat Lumo.lnk"
  RMDir "$SMPROGRAMS\Lumo"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Lumo"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\Lumo"

  MessageBox MB_YESNO|MB_ICONQUESTION "Smazat i nastavení, poznámky a přihrádku (složka %APPDATA%\Lumo)?" IDNO keep
    RMDir /r "$APPDATA\Lumo"
  keep:
SectionEnd
