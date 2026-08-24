#define MyAppName "CursorPocket"
#ifndef MyAppVersion
  #define MyAppVersion "0.4.0-preview"
#endif
#ifndef MyAppFileVersion
  #define MyAppFileVersion "0.4.0.0"
#endif
#define MyAppPublisher "Tanmay Sharma"
#define MyAppExeName "CursorPocket.exe"

[Setup]
AppId={{A77D8660-B2BC-4E7A-A639-5617FB8BDE22}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppFileVersion}
VersionInfoProductVersion={#MyAppFileVersion}
DefaultDirName={localappdata}\Programs\CursorPocket
DefaultGroupName=CursorPocket
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\artifacts
OutputBaseFilename=CursorPocket-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\CursorPocket.App\Assets\AppIcon.ico

[Files]
Source: "..\..\artifacts\CursorPocket-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CursorPocket"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\CursorPocket"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Start CursorPocket"; Flags: nowait postinstall skipifsilent; Check: not RelaunchAfterUpdate
Filename: "{app}\{#MyAppExeName}"; Parameters: "--updated"; Flags: nowait skipifdoesntexist; Check: RelaunchAfterUpdate

[Code]
function RelaunchAfterUpdate(): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), '/RELAUNCH') = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;
