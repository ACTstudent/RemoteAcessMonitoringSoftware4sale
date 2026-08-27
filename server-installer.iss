; CAMS Server - Inno Setup installer definition
; Builds "CAMS-Server-Setup.exe" - installs the server, opens the firewall,
; performs clean installation, and optionally launches the server on startup.

#define MyAppName "CAMS Server"
#ifndef MyAppVersion
  #define MyAppVersion "2.6.0"
#endif
#define MyAppExeName "Server.exe"
#define MyAppPublisher "CAMS"

[Setup]
AppId={{CAMS-SERVER-2026}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\CAMS Server
DefaultGroupName=CAMS
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=no
OutputBaseFilename=CAMS-Server-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; CAMS deliberately keeps its writable SQLite data and per-user autostart under the
; installing teacher account. Elevation is required only for the firewall rules.
UsedUserAreasWarning=no
CloseApplications=yes
CloseApplicationsFilter=*.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "cleaninstall"; Description: "Clean installation (remove all old binary files & cached assets before installing)"; GroupDescription: "Installation Options:"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start the server automatically when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[InstallDelete]
Type: filesandordirs; Name: "{app}\runtimes"; Tasks: cleaninstall
Type: filesandordirs; Name: "{app}\wwwroot"; Tasks: cleaninstall
Type: files; Name: "{app}\*.dll"; Tasks: cleaninstall
Type: files; Name: "{app}\*.exe"; Tasks: cleaninstall
Type: files; Name: "{app}\*.json"; Tasks: cleaninstall
Type: files; Name: "{app}\*.pdb"; Tasks: cleaninstall

[Files]
Source: "server-publish\*"; DestDir: "{app}"; Excludes: "CAMS.db,CAMS.db-shm,CAMS.db-wal"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";             Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Server Dashboard (Admin)"; Filename: "https://localhost:5000/Admin"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0
Name: "{group}\Server Dashboard (Teacher)"; Filename: "https://localhost:5000/Teacher"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0
Name: "{autodesktop}\{#MyAppName}";       Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CAMS Server"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""CAMS Server"""; Flags: runhidden; StatusMsg: "Refreshing the CAMS server firewall rule..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""CAMS Server"" dir=in action=allow protocol=TCP localport=5000 profile=any"; Flags: runhidden; StatusMsg: "Opening firewall port 5000..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""CAMS Discovery"""; Flags: runhidden; StatusMsg: "Refreshing the CAMS discovery firewall rule..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""CAMS Discovery"" dir=in action=allow protocol=UDP localport=5001 profile=any"; Flags: runhidden; StatusMsg: "Opening firewall port 5001 for auto-discovery..."
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
