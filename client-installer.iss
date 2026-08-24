; CAMS Student Client - Inno Setup installer definition
; Builds "CAMS-Client-Setup.exe" - a clean installation wizard.

#define MyAppName "CAMS Student Client"
#ifndef MyAppVersion
  #define MyAppVersion "2.5.6"
#endif
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
PrivilegesRequired=lowest
CloseApplications=yes
CloseApplicationsFilter=*.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "cleaninstall"; Description: "Clean installation (remove all old binary files before installing)"; GroupDescription: "Installation Options:"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
Type: filesandordirs; Name: "{app}\runtimes"; Tasks: cleaninstall
Type: files; Name: "{app}\*.dll"; Tasks: cleaninstall
Type: files; Name: "{app}\*.exe"; Tasks: cleaninstall
Type: files; Name: "{app}\*.json"; Tasks: cleaninstall
Type: files; Name: "{app}\*.pdb"; Tasks: cleaninstall

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
      'Your teacher will give you this address, e.g. https://lab-server.example:5000/remoteMonitoringHub');
    ServerUrlPage.Add('Server URL:', False);
    ServerUrlPage.Values[0] := 'https://localhost:5000/remoteMonitoringHub';
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
