; CAMS Student Client - Inno Setup installer definition
; Builds "CAMS-Client-Setup.exe" - a clean installation wizard.

#define MyAppName "CAMS Student Client"
#ifndef MyAppVersion
  #define MyAppVersion "2.9.8"
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
Source: "client-publish\*"; DestDir: "{app}"; Excludes: "client-settings.json"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "client-publish\client-settings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ServerUrlPage: TInputQueryWizardPage;
  ServerTrustPage: TInputFileWizardPage;
  ServerUrlOverride: string;
  ServerRootCertificateOverride: string;

function IsValidServerUrl(const Value: string): Boolean;
var
  Authority: string;
  HubPath: string;
  Normalized: string;
begin
  HubPath := '/remoteMonitoringHub';
  Normalized := Trim(Value);
  if (Normalized <> Value) or
     (Copy(LowerCase(Normalized), 1, 8) <> 'https://') or
     (Length(Normalized) <= 8 + Length(HubPath)) or
     (Copy(Normalized, Length(Normalized) - Length(HubPath) + 1, Length(HubPath)) <> HubPath) then
  begin
    Result := False;
    Exit;
  end;

  Authority := Copy(Normalized, 9, Length(Normalized) - 8 - Length(HubPath));
  Result := (Authority <> '') and
    (Pos('/', Authority) = 0) and (Pos('\', Authority) = 0) and
    (Pos('@', Authority) = 0) and (Pos(' ', Authority) = 0) and
    (Pos('?', Authority) = 0) and (Pos('#', Authority) = 0) and
    (Authority[1] <> ':') and (Authority[Length(Authority)] <> ':');
end;

function InitializeSetup: Boolean;
var
  PreferredServerUrl: string;
  LegacyServerUrl: string;
begin
  PreferredServerUrl := ExpandConstant('{param:ServerUrl|}');
  LegacyServerUrl := ExpandConstant('{param:ServerIP|}');
  if (PreferredServerUrl <> '') and (LegacyServerUrl <> '') and
     (CompareText(Trim(PreferredServerUrl), Trim(LegacyServerUrl)) <> 0) then
  begin
    MsgBox('/ServerUrl and /ServerIP specify different values. Use only /ServerUrl, or provide the same URL to both.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if PreferredServerUrl <> '' then
    ServerUrlOverride := PreferredServerUrl
  else
    ServerUrlOverride := LegacyServerUrl;

  ServerRootCertificateOverride := ExpandConstant('{param:ServerRootCert|}');
  if (ServerRootCertificateOverride = '') and
     FileExists(ExpandConstant('{localappdata}\CAMS Server\CAMS-Server-Root.cer')) then
    ServerRootCertificateOverride := ExpandConstant('{localappdata}\CAMS Server\CAMS-Server-Root.cer');
  if (ServerUrlOverride <> '') and (not IsValidServerUrl(ServerUrlOverride)) then
  begin
    MsgBox('/ServerUrl must be the exact HTTPS CAMS hub URL, for example https://192.168.1.100:5000/remoteMonitoringHub. /ServerIP is retained as an alias.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if (ServerRootCertificateOverride <> '') and
     ((not FileExists(ServerRootCertificateOverride)) or
      (CompareText(ExtractFileExt(ServerRootCertificateOverride), '.cer') <> 0)) then
  begin
    MsgBox('/ServerRootCert must name an existing .cer certificate file.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
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
    ServerTrustPage := CreateInputFilePage(ServerUrlPage.ID,
      'Current-User Server Trust', 'Trust the CAMS server for the current Windows user',
      'Select CAMS-Server-Root.cer from the teacher PC. Leave this blank only when the server uses a publicly trusted certificate.')
  else
    ServerTrustPage := CreateInputFilePage(wpSelectDir,
      'Current-User Server Trust', 'Trust the CAMS server for the current Windows user',
      'Select CAMS-Server-Root.cer from the teacher PC. Leave this blank only when the server uses a publicly trusted certificate.');

  ServerTrustPage.Add('Current-user trusted root certificate (optional):',
    'Certificate files (*.cer)|*.cer|All files (*.*)|*.*', '.cer');
  ServerTrustPage.Values[0] := ServerRootCertificateOverride;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  certificatePath: string;
begin
  Result := True;
  if (ServerUrlPage <> nil) and (CurPageID = ServerUrlPage.ID) and
     (not IsValidServerUrl(ServerUrlPage.Values[0])) then
  begin
    MsgBox('Enter an HTTPS CAMS server URL, for example https://192.168.1.100:5000/remoteMonitoringHub.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (ServerTrustPage <> nil) and (CurPageID = ServerTrustPage.ID) then
  begin
    certificatePath := Trim(ServerTrustPage.Values[0]);
    if (certificatePath <> '') and
       ((not FileExists(certificatePath)) or
        (CompareText(ExtractFileExt(certificatePath), '.cer') <> 0)) then
    begin
      MsgBox('Select an existing CAMS root certificate with a .cer extension.', mbError, MB_OK);
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
  ServerUrl: string;
  RootCertificatePath: string;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    ResultCode := -1;
    if ServerUrlOverride <> '' then
      ServerUrl := ServerUrlOverride
    else
      ServerUrl := ServerUrlPage.Values[0];

    RootCertificatePath := Trim(ServerTrustPage.Values[0]);
    if RootCertificatePath <> '' then
    begin
      if not InstallRootCertificate(RootCertificatePath) then
        RaiseException('Windows could not add the selected certificate to the current user trusted root store.');
    end;

    if (not Exec(ExpandConstant('{app}\{#MyAppExeName}'),
         '--configure-server "' + ServerUrl + '"', ExpandConstant('{app}'),
         SW_HIDE, ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
      RaiseException(Format('CAMS client server configuration failed (exit code %d).', [ResultCode]));
  end;
end;
