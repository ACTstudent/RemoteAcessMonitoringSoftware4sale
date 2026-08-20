; CAMS Student Client - Inno Setup installer definition
; Builds "CAMS-Client-Setup.exe" - a download-and-run installation wizard.
; Requires Inno Setup 6: https://jrsoftware.org/isdl.php
;
; Silent deploy:  CAMS-Client-Setup.exe /VERYSILENT /ServerIP="http://192.168.1.100:5000/remoteMonitoringHub"

#define MyAppName "CAMS Student Client"
#define MyAppVersion "2.5.0"
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
DefaultDirName={localappdata}\CAMS Student Client
DefaultGroupName=CAMS
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=no
OutputBaseFilename=CAMS-Client-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "client-publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ServerUrlPage: TInputQueryWizardPage;
  ServerUrlOverride: string;

function InitializeSetup: Boolean;
begin
  ServerUrlOverride := ExpandConstant('{param:ServerIP}');
  Result := True;
end;

procedure InitializeWizard;
begin
  if ServerUrlOverride = '' then
  begin
    ServerUrlPage := CreateInputQueryPage(wpSelectDir,
      'Server Address', 'Enter the CAMS server URL',
      'Your teacher will give you this address, e.g. http://192.168.1.100:5000/remoteMonitoringHub');
    ServerUrlPage.Add('Server URL:', False);
    ServerUrlPage.Values[0] := 'http://localhost:5000/remoteMonitoringHub';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  SettingsPath: string;
  ServerUrl: string;
begin
  if CurStep = ssPostInstall then
  begin
    if ServerUrlOverride <> '' then
      ServerUrl := ServerUrlOverride
    else
      ServerUrl := ServerUrlPage.Values[0];

    SettingsPath := ExpandConstant('{app}\client-settings.json');
    if not FileExists(SettingsPath) then
    begin
      StringChangeEx(ServerUrl, '\', '\\', True);
      StringChangeEx(ServerUrl, '"', '\"', True);
      SaveStringToFile(SettingsPath,
        '{' + #13#10 +
        '  "ServerUrl": "' + ServerUrl + '"' + #13#10 +
        '}', False);
    end;
  end;
end;