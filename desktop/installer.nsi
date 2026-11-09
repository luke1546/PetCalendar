Unicode true
!include MUI2.nsh
!include FileFunc.nsh
!define VERSION "3.0.2"
!ifndef APP_DIRECTORY
  !define APP_DIRECTORY "..\dist\app"
!endif
!ifdef QA_BUILD
  !define PRODUCT "PetCalendarDesktopQA"
  !define LINK "펫 캘린더 QA"
  !define OLD_LINK "업무 캘린더 QA"
  !ifndef OUTPUT_FILE
    !define OUTPUT_FILE "..\dist\PetCalendar-Setup-QA-Final.exe"
  !endif
!else
  !define PRODUCT "PetCalendarDesktop"
  !define LINK "펫 캘린더"
  !define OLD_LINK "업무 캘린더"
  !ifndef OUTPUT_FILE
    !define OUTPUT_FILE "..\dist\PetCalendar-Setup.exe"
  !endif
!endif
OutFile "${OUTPUT_FILE}"
Name "펫 캘린더"
InstallDir "$LOCALAPPDATA\Programs\PetCalendar"
InstallDirRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "펫 캘린더"
Icon "..\calendar.ico"
VIProductVersion "3.0.2.0"
VIAddVersionKey /LANG=1042 "ProductName" "펫 캘린더"
VIAddVersionKey /LANG=1042 "FileDescription" "펫 캘린더 설치"
VIAddVersionKey /LANG=1042 "FileVersion" "3.0.2.0"
VIAddVersionKey /LANG=1042 "LegalCopyright" "PetCalendar"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\PetCalendar.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "Korean"
Var DataDir
Var Staged
Var Previous
Var AutoStart
!macro CleanPayload Directory
  Delete "${Directory}\PetCalendar.exe"
  Delete "${Directory}\PetCalendar.exe.config"
  Delete "${Directory}\RemovePetCalendar.exe"
  Delete "${Directory}\calendar.ico"
  Delete "${Directory}\notification.wav"
  Delete "${Directory}\public\index.html"
  Delete "${Directory}\public\app.mjs"
  Delete "${Directory}\public\model.mjs"
  Delete "${Directory}\public\styles.css"
  Delete "${Directory}\public\icon.svg"
  Delete "${Directory}\public\holidays.json"
  Delete "${Directory}\public\tour-cat.png"
  Delete "${Directory}\EMOJI-LICENSE.md"
  Delete "${Directory}\webview2\Microsoft.Web.WebView2.Core.dll"
  Delete "${Directory}\webview2\Microsoft.Web.WebView2.WinForms.dll"
  Delete "${Directory}\webview2\WebView2Loader.dll"
  Delete "${Directory}\webview2\LICENSE.txt"
  Delete "${Directory}\webview2\source.json"
  RMDir "${Directory}\public"
  RMDir "${Directory}\webview2"
  RMDir "${Directory}"
!macroend
Function .onInit
  SetShellVarContext current
  StrCpy $AutoStart 1
  ReadRegStr $R0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "InstallLocation"
!ifndef QA_BUILD
  StrCmp $R0 "" 0 known_install
  ReadRegStr $R0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WorkCalendarDesktop" "InstallLocation"
!endif
  StrCmp $R0 "" check_desktop known_install
check_desktop:
  IfFileExists "$DESKTOP\${LINK}.lnk" known_install 0
  IfFileExists "$DESKTOP\${OLD_LINK}.lnk" known_install startup_done
known_install:
  IfFileExists "$SMSTARTUP\${LINK}.lnk" startup_done 0
  IfFileExists "$SMSTARTUP\${OLD_LINK}.lnk" startup_done startup_disabled
startup_disabled:
  StrCpy $AutoStart 0
startup_done:
  StrCpy $DataDir "$LOCALAPPDATA\PetCalendar\data"
  ReadRegStr $R1 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "DataDir"
!ifndef QA_BUILD
  StrCmp $R1 "" 0 use_registered_data
  ReadRegStr $R1 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\WorkCalendarDesktop" "DataDir"
  StrCmp $R1 "$LOCALAPPDATA\WorkCalendar\data" data_ready
!endif
!ifndef QA_BUILD
use_registered_data:
!endif
  StrCmp $R1 "" data_ready
  StrCpy $DataDir $R1
data_ready:
  ${GetParameters} $R0
  StrCpy $R1 ""
  ${GetOptions} $R0 "/DATA=" $R1
  StrCmp $R1 "" +2
    StrCpy $DataDir $R1
FunctionEnd
Section "펫 캘린더" Main
  InitPluginsDir
  StrCpy $Staged "$INSTDIR\.install-staging"
  SetOutPath "$Staged"
  File /r "${APP_DIRECTORY}\*.*"
  ExecWait '"$Staged\PetCalendar.exe" --data-dir "$DataDir" --validate-install --silent' $0
  StrCmp $0 0 +3
    MessageBox MB_OK|MB_ICONSTOP "필수 실행 구성 요소를 확인하지 못했습니다. .NET Framework 4.8 설치 상태를 확인해 주세요." /SD IDOK
    Abort
  ExecWait '"$Staged\PetCalendar.exe" --data-dir "$DataDir" --prepare-install --silent' $0
  StrCmp $0 0 +3
    MessageBox MB_OK|MB_ICONSTOP "기존 캘린더의 저장·종료를 완료하지 못했습니다. 기존 기록은 보존했습니다." /SD IDOK
    Abort
  StrCpy $Previous "$PLUGINSDIR\previous"
  CreateDirectory "$Previous"
  IfFileExists "$INSTDIR\PetCalendar.exe" 0 +2
    CopyFiles /SILENT "$INSTDIR\*.*" "$Previous"
  CreateDirectory "$INSTDIR"
  SetOutPath "$INSTDIR"
  ClearErrors
  File /r "${APP_DIRECTORY}\*.*"
  IfErrors install_failed
  ExecWait '"$INSTDIR\PetCalendar.exe" --data-dir "$DataDir" --validate-install --silent' $0
  StrCmp $0 0 install_ok
install_failed:
  CopyFiles /SILENT "$Previous\*.*" "$INSTDIR"
  ExecWait '"$Staged\PetCalendar.exe" --data-dir "$DataDir" --rollback-install --silent'
  MessageBox MB_OK|MB_ICONSTOP "설치하지 못했습니다. 기존 기록과 바로가기를 복원했습니다." /SD IDOK
  Abort
install_ok:
  !insertmacro CleanPayload "$Staged"
  CreateDirectory "$SMPROGRAMS\${LINK}"
  CreateShortcut "$DESKTOP\${LINK}.lnk" "$INSTDIR\PetCalendar.exe" '--data-dir "$DataDir"' "$INSTDIR\PetCalendar.exe" 0
  CreateShortcut "$SMPROGRAMS\${LINK}\${LINK}.lnk" "$INSTDIR\PetCalendar.exe" '--data-dir "$DataDir"' "$INSTDIR\PetCalendar.exe" 0
  StrCmp $AutoStart 1 0 +2
    CreateShortcut "$SMSTARTUP\${LINK}.lnk" "$INSTDIR\PetCalendar.exe" '--data-dir "$DataDir"' "$INSTDIR\PetCalendar.exe" 0
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "DisplayName" "펫 캘린더"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "Publisher" "PetCalendar"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "DisplayIcon" "$INSTDIR\PetCalendar.exe,0"
!ifdef QA_BUILD
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "UninstallString" '$\"$INSTDIR\RemovePetCalendar.exe$\" --qa'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "QuietUninstallString" '$\"$INSTDIR\RemovePetCalendar.exe$\" --qa /S'
!else
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "UninstallString" '$\"$INSTDIR\RemovePetCalendar.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "QuietUninstallString" '$\"$INSTDIR\RemovePetCalendar.exe$\" /S'
!endif
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "DataDir" "$DataDir"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "NoRepair" 1
  ExecWait '"$INSTDIR\PetCalendar.exe" --data-dir "$DataDir" --finish-display-rename "${OLD_LINK}" --silent' $0
!ifndef QA_BUILD
  ExecWait '"$INSTDIR\PetCalendar.exe" --data-dir "$DataDir" --finish-brand-upgrade --silent' $0
  StrCmp $0 0 +2
    MessageBox MB_OK|MB_ICONEXCLAMATION "새 캘린더는 설치했지만 이전 설치 파일을 정리하지 못했습니다. 사용자 기록은 보존했습니다." /SD IDOK
!endif
SectionEnd
