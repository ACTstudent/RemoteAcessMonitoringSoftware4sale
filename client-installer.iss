; CAMS Student Client - Inno Setup installer definition
; Builds "CAMS-Client-Setup.exe" - a download-and-run installation wizard.
; Requires Inno Setup 6: https://jrsoftware.org/isdl.php

#define MyAppName "CAMS Student Client"
#define MyAppVersion "1.0.0"
#define MyAppExeName "Client.exe"
#define MyAppPublisher "CAMS"
#define MyAppURL "https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale"

[Setup]
AppId={{CAMS-STUDENT-CLIENT-2026}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\CAMS Student Client
DefaultGroupName=CAMS
DisableProgramGroupPage=yes
OutputBaseFilename=CAMS-Client-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Change this to a hosted URL if you want the installer to be downloadable
; from a web server instead of copied over the LAN.
; AppVerName={#MyAppName} {#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The published client output is copied here by publish-client.ps1.
Source: "client-publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
