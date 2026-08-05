; CAMS Server - Inno Setup installer definition
; Builds "CAMS-Server-Setup.exe" - installs the server, opens the firewall,
; and optionally launches the server on startup.
; Requires Inno Setup 6: https://jrsoftware.org/isdl.php
; Compile via publish.ps1 (automated) or manually:
;   iscc /oserver-dist server-installer.iss

#define MyAppName "CAMS Server"
#define MyAppVersion "2.0.0"
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
OutputBaseFilename=CAMS-Server-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart";     Description: "Start the server automatically when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "server-publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";             Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Server Dashboard (Admin)"; Filename: "http://localhost:5000/Admin"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0
Name: "{group}\Server Dashboard (Teacher)"; Filename: "http://localhost:5000/Teacher"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0
Name: "{autodesktop}\{#MyAppName}";       Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CAMS Server"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: runstarts

[Run]
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""CAMS Server"" dir=in action=allow protocol=TCP localport=5000"; Flags: runhidden; StatusMsg: "Opening firewall port 5000..."
Filename: "netsh.exe"; Parameters: "http add urlacl url=http://+:5000/ user=""Users"""; Flags: runhidden; StatusMsg: "Reserving HTTP namespace for non-admin listeners..."
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent