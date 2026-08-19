#define MyAppName "CursorPocket"
#define MyAppVersion "0.2.0-preview"
#define MyAppPublisher "Tanmay Sharma"
#define MyAppExeName "CursorPocket.exe"

[Setup]
AppId={{A77D8660-B2BC-4E7A-A639-5617FB8BDE22}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
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
Name: "startup"; Description: "Start CursorPocket when I sign in"; GroupDescription: "Everyday use:"; Flags: checkedonce

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CursorPocket"; ValueData: """{app}\{#MyAppExeName}"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Start CursorPocket"; Flags: nowait postinstall skipifsilent
