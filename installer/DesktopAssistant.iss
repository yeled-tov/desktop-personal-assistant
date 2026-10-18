#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\portable"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{2FA45C1D-228B-4681-865D-2143CE984C1C}
AppName=העוזר האישי
AppVersion={#AppVersion}
AppVerName=העוזר האישי {#AppVersion}
AppPublisher=yeled-tov
DefaultDirName={localappdata}\Programs\DesktopAssistant
DefaultGroupName=העוזר האישי
OutputDir={#OutputDir}
OutputBaseFilename=DesktopAssistant-Setup
SetupIconFile=..\src\DesktopAssistant.App\app.ico
UninstallDisplayIcon={app}\DesktopAssistant.exe
UninstallDisplayName=העוזר האישי
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=lowest
WizardStyle=modern
DisableProgramGroupPage=yes
DisableDirPage=auto
CloseApplications=force
RestartApplications=no
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "hebrew"; MessagesFile: "compiler:Languages\Hebrew.isl"

[Tasks]
Name: "desktopicon"; Description: "צור קיצור דרך בשולחן העבודה"; GroupDescription: "קיצורי דרך:"
Name: "startup"; Description: "הפעל את העוזר אוטומטית עם Windows (מומלץ — כדי שהקיצורים יעבדו תמיד)"; GroupDescription: "הפעלה:"

[Files]
Source: "{#SourceDir}\DesktopAssistant.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\app.ico"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\העוזר האישי"; Filename: "{app}\DesktopAssistant.exe"
Name: "{autodesktop}\העוזר האישי"; Filename: "{app}\DesktopAssistant.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DesktopAssistant"; ValueData: """{app}\DesktopAssistant.exe"" --minimized"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\DesktopAssistant.exe"; Description: "הפעל את העוזר עכשיו"; Flags: postinstall nowait skipifsilent

[Code]
// The assistant lives in the tray, so close a running copy before replacing the EXE.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM DesktopAssistant.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM DesktopAssistant.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DesktopAssistant');
end;
