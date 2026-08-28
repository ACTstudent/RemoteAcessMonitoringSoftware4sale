; CAMS Student Client - Inno Setup installer definition
; Builds "CAMS-Client-Setup.exe" - a clean installation wizard.

#define MyAppName "CAMS Student Client"
#ifndef MyAppVersion
  #define MyAppVersion "2.8.0"
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
  ServerTrustPage: TInputQueryWizardPage;
  ServerUrlOverride: string;
  ServerRootCertificateOverride: string;

function InitializeSetup: Boolean;
begin
  ServerUrlOverride := ExpandConstant('{param:ServerIP}');
  ServerRootCertificateOverride := ExpandConstant('{param:ServerRootCert}');
  if (ServerRootCertificateOverride = '') and
     FileExists(ExpandConstant('{localappdata}\CAMS Server\CAMS-Server-Root.cer')) then
    ServerRootCertificateOverride := ExpandConstant('{localappdata}\CAMS Server\CAMS-Server-Root.cer');
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

  if ServerUrlOverride = '' then
    ServerTrustPage := CreateInputQueryPage(ServerUrlPage.ID,
      'Server Trust', 'Trust the CAMS server certificate',
      'Copy CAMS-Server-Root.cer from the teacher PC to this workstation. Leave this blank only when the server uses a publicly trusted certificate.')
  else
    ServerTrustPage := CreateInputQueryPage(wpSelectDir,
      'Server Trust', 'Trust the CAMS server certificate',
      'Copy CAMS-Server-Root.cer from the teacher PC to this workstation. Leave this blank only when the server uses a publicly trusted certificate.');

  ServerTrustPage.Add('Public root certificate path (optional):', False);
  ServerTrustPage.Values[0] := ServerRootCertificateOverride;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  certificatePath: string;
begin
  Result := True;
  if (ServerUrlPage <> nil) and (CurPageID = ServerUrlPage.ID) and
     (Pos('https://', LowerCase(Trim(ServerUrlPage.Values[0]))) <> 1) then
  begin
    MsgBox('Enter an HTTPS CAMS server URL, for example https://192.168.1.100:5000/remoteMonitoringHub.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (ServerTrustPage <> nil) and (CurPageID = ServerTrustPage.ID) then
  begin
    certificatePath := Trim(ServerTrustPage.Values[0]);
    if (certificatePath <> '') and (not FileExists(certificatePath)) then
    begin
      MsgBox('The selected CAMS root certificate was not found.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function InstallRootCertificate(const certificatePath: string): Boolean;
var
  resultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\certutil.exe'),
    '-user -addstore -f Root "' + certificatePath + '"',
    '', SW_HIDE, ewWaitUntilTerminated, resultCode) and (resultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  SettingsPath: string;
  ServerUrl: string;
  RootCertificatePath: string;
begin
  if CurStep = ssPostInstall then
  begin
    if ServerUrlOverride <> '' then
      ServerUrl := ServerUrlOverride
    else
      ServerUrl := ServerUrlPage.Values[0];

    RootCertificatePath := Trim(ServerTrustPage.Values[0]);
    if RootCertificatePath <> '' then
    begin
      if not InstallRootCertificate(RootCertificatePath) then
        MsgBox('CAMS was installed, but Windows could not trust the selected root certificate. ' +
          'Run the installer again as the current student user or install the certificate manually.',
          mbError, MB_OK);
    end;

    SettingsPath := ExpandConstant('{app}\client-settings.json');
    StringChangeEx(ServerUrl, '\', '\\', True);
    StringChangeEx(ServerUrl, '"', '\"', True);
    SaveStringToFile(SettingsPath,
      '{' + #13#10 +
      '  "ServerUrl": "' + ServerUrl + '"' + #13#10 +
      '}', False);
  end;
end;
