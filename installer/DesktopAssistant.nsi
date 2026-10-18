; Installer for "שיר – עוזרת AI למחשב" (Desktop Personal Assistant)
; NOTE: this file must stay UTF-8 *with BOM* — without it makensis on Windows reads the Hebrew as ANSI and shows gibberish.
; Build:  makensis -DVERSION=2.0.0 -DSOURCE_DIR=..\artifacts\portable -DOUT_FILE=..\artifacts\installer\DesktopAssistant-Setup.exe installer\DesktopAssistant.nsi
; Works with makensis on Windows and on Linux.

Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user        ; per-user install: no admin prompt
ManifestDPIAware true

!ifndef VERSION
  !define VERSION "2.0.0"
!endif
!ifndef SOURCE_DIR
  !define SOURCE_DIR "..\artifacts\portable"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "..\artifacts\installer\DesktopAssistant-Setup.exe"
!endif

!define APP_NAME "שיר – עוזרת AI למחשב"
!define SHORT_NAME "שיר"
!define APP_EXE "DesktopAssistant.exe"
!define APP_ID "DesktopAssistant"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"

Name "${APP_NAME}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\DesktopAssistant"
InstallDirRegKey HKCU "Software\${APP_ID}" "InstallDir"
BrandingText "${APP_NAME} ${VERSION}"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1037 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1037 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1037 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1037 "FileDescription" "התקנת ${APP_NAME}"
VIAddVersionKey /LANG=1037 "LegalCopyright" "yeled-tov"

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define MUI_ICON "..\src\DesktopAssistant.App\app.ico"
!define MUI_UNICON "..\src\DesktopAssistant.App\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "התקנת ${APP_NAME}"
!define MUI_WELCOMEPAGE_TEXT "מה זה?$\r$\nעוזרת AI למחשב — כמו Gemini בטלפון. אפשר לדבר איתה או לכתוב לה, והיא פותחת תוכנות, כותבת ב-Word, מקלידה מה שאומרים, מחפשת, מתרגמת את המסך ועונה על שאלות.$\r$\n$\r$\nכמה מקום?$\r$\nכ-200MB. לא צריך הרשאות מנהל ולא צריך להתקין שום דבר נוסף.$\r$\n$\r$\nאחרי ההתקנה:$\r$\n• Ctrl+Alt+Space — לפתוח את שיר$\r$\n• Ctrl+Alt+V — לדבר · Ctrl+Alt+D — הכתבה$\r$\n• Ctrl+Alt+S — הקף וחפש$\r$\n$\r$\nבפתיחה הראשונה מחברים מפתח AI (ל-Gemini יש חינמי), או מורידים מצב אופליין מתוך ההגדרות."
!define MUI_FINISHPAGE_TITLE "${SHORT_NAME} מותקנת!"
!define MUI_FINISHPAGE_TEXT "שיר רצה ברקע — הסמל שלה ליד השעון.$\r$\n$\r$\nלפתוח: Ctrl+Alt+Space$\r$\nלשאול מה היא יודעת: לחצו ? בחלון שלה.$\r$\nלשנות קיצורים, ספק AI או להוריד מצב אופליין: ⚙ הגדרות."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "הפעל את שיר עכשיו"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Hebrew"

Function .onInit
  ; The assistant runs in the tray — close it so its files can be replaced.
  nsExec::Exec 'taskkill /F /IM ${APP_EXE}'
  Sleep 400
FunctionEnd

Section "${SHORT_NAME} — התוכנה עצמה (חובה)" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  File "${SOURCE_DIR}\${APP_EXE}"
  File "/oname=app.ico" "..\src\DesktopAssistant.App\app.ico"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; remove shortcuts left by older versions
  Delete "$SMPROGRAMS\העוזר האישי.lnk"
  Delete "$DESKTOP\העוזר האישי.lnk"
  CreateShortCut "$SMPROGRAMS\${SHORT_NAME} – עוזרת AI.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\app.ico" 0

  WriteRegStr HKCU "Software\${APP_ID}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "yeled-tov"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\app.ico"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "קיצור דרך בשולחן העבודה" SecDesktop
  CreateShortCut "$DESKTOP\${SHORT_NAME} – עוזרת AI.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\app.ico" 0
SectionEnd

Section "הפעלה אוטומטית עם Windows (מומלץ)" SecStartup
  WriteRegStr HKCU "${RUN_KEY}" "${APP_ID}" '"$INSTDIR\${APP_EXE}" --minimized'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} "התוכנה (כ-200MB, כולל כל מה שצריך). מצב אופליין לא כלול — מורידים אותו מההגדרות רק אם רוצים."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "אייקון על שולחן העבודה."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} "העוזר יעלה ברקע עם הדלקת המחשב, כדי שהקיצורים והלחיצה הארוכה יעבדו תמיד."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  nsExec::Exec 'taskkill /F /IM ${APP_EXE}'
  Sleep 400
  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\app.ico"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${SHORT_NAME} – עוזרת AI.lnk"
  Delete "$DESKTOP\${SHORT_NAME} – עוזרת AI.lnk"
  DeleteRegValue HKCU "${RUN_KEY}" "${APP_ID}"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "Software\${APP_ID}"
  ; Settings and logs in %LOCALAPPDATA%\DesktopAssistant are kept on purpose (API keys live in Windows Credential Manager).
SectionEnd
