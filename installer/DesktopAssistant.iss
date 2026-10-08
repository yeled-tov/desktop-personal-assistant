#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\portable"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{2FA45C1D-228B-4681-865D-2143CE984C1C}
AppName=עוזר אישי למחשב
AppVersion={#AppVersion}
AppPublisher=Desktop Personal Assistant
DefaultDirName={autopf}\DesktopAssistant
DefaultGroupName=עוזר אישי למחשב
OutputDir={#OutputDir}
OutputBaseFilename=DesktopAssistant-Setup
SetupIconFile={#SourceDir}\app.ico
UninstallDisplayIcon={app}\DesktopAssistant.exe
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
WizardStyle=modern
DisableProgramGroupPage=yes
Uninstallable=yes

[Languages]
Name: "hebrew"; MessagesFile: "compiler:Languages\Hebrew.isl"

[Tasks]
Name: "desktopicon"; Description: "צור קיצור דרך בשולחן העבודה"; GroupDescription: "קיצורי דרך:"; Flags: unchecked
Name: "startup"; Description: "הפעל עם Windows"; GroupDescription: "העדפות:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\DesktopAssistant.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\עוזר אישי למחשב"; Filename: "{app}\DesktopAssistant.exe"
Name: "{autodesktop}\עוזר אישי למחשב"; Filename: "{app}\DesktopAssistant.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DesktopAssistant"; ValueData: """{app}\DesktopAssistant.exe"" --minimized"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\DesktopAssistant.exe"; Description: "הפעל את עוזר אישי למחשב"; Flags: postinstall nowait skipifsilent
