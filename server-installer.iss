; CAMS Server - Inno Setup installer definition
; Builds "CAMS-Server-Setup.exe" - installs the server, opens the firewall,
; performs clean installation, and optionally launches the server on startup.

#define MyAppName "CAMS Server"
#ifndef MyAppVersion
  #define MyAppVersion "2.9.1"
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
Type: filesandordirs; Name: "{app}\DeploymentAssets"; Tasks: cleaninstall
Type: files; Name: "{app}\*.dll"; Tasks: cleaninstall
Type: files; Name: "{app}\*.exe"; Tasks: cleaninstall
Type: files; Name: "{app}\*.pdb"; Tasks: cleaninstall

[Files]
Source: "server-publish\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "server-publish\DeploymentAssets\CAMS-Client-Setup.exe"; DestDir: "{app}\DeploymentAssets"; Flags: ignoreversion
Source: "server-publish\DeploymentAssets\CAMS-Client-Setup.exe.sha256"; DestDir: "{app}\DeploymentAssets"; Flags: ignoreversion
Source: "server-publish\DeploymentAssets\deployment-manifest.json"; DestDir: "{app}\DeploymentAssets"; Flags: ignoreversion
; Package safe defaults and binaries only. Runtime certificates, databases, and environment settings stay on the target machine.
Source: "server-publish\*"; DestDir: "{app}"; Excludes: "appsettings*.json,DeploymentAssets\*,*.pfx,CAMS.db,CAMS.db-shm,CAMS.db-wal"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";             Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Server Dashboard (Admin)"; Filename: "https://localhost:5000/Admin"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0
Name: "{group}\Server Dashboard (Teacher)"; Filename: "https://localhost:5000/Teacher"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0
Name: "{autodesktop}\{#MyAppName}";       Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CAMS Server"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""CAMS Server"""; Flags: runhidden; StatusMsg: "Refreshing the CAMS server firewall rule..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""CAMS Server"" dir=in action=allow protocol=TCP localport=5000 profile=private"; Flags: runhidden; StatusMsg: "Opening firewall port 5000 on private networks..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""CAMS Discovery"""; Flags: runhidden; StatusMsg: "Refreshing the CAMS discovery firewall rule..."
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; StatusMsg: "Initializing the CAMS server and administrator account..."

[Code]
function SetProcessEnvironmentVariable(Name: string; Value: string): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

var
  AdminAccountPage: TInputQueryWizardPage;
  AdminUsernameOverride: string;
  AdminPasswordOverride: string;

function IsValidAdminUsername(const Value: string): Boolean;
var
  I: Integer;
  C: Char;
begin
  Result := (Length(Value) >= 3) and (Length(Value) <= 50);
  if not Result then Exit;
  for I := 1 to Length(Value) do
  begin
    C := Value[I];
    if not (((C >= 'a') and (C <= 'z')) or ((C >= 'A') and (C <= 'Z')) or
      ((C >= '0') and (C <= '9')) or (C = '.') or (C = '_') or (C = '-')) then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function HasExistingDatabase: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\CAMS.db'));
end;

function InitializeSetup: Boolean;
begin
  AdminUsernameOverride := Trim(ExpandConstant('{param:AdminUsername}'));
  AdminPasswordOverride := ExpandConstant('{param:AdminPassword}');
  Result := True;

  if (AdminUsernameOverride = '') <> (AdminPasswordOverride = '') then
  begin
    MsgBox('/AdminUsername and /AdminPassword must be supplied together.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (AdminUsernameOverride <> '') and (not IsValidAdminUsername(AdminUsernameOverride)) then
  begin
    MsgBox('/AdminUsername must be 3-50 letters, numbers, dots, underscores, or hyphens.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (AdminPasswordOverride <> '') and (Length(AdminPasswordOverride) < 12) then
  begin
    MsgBox('/AdminPassword must contain at least 12 characters.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  if (AdminUsernameOverride = '') and (AdminPasswordOverride = '') then
  begin
    AdminAccountPage := CreateInputQueryPage(wpSelectDir,
      'Initial Administrator', 'Create the first CAMS administrator',
      'Enter credentials for the first local administrator. Existing accounts are never overwritten. On an upgrade with an existing CAMS.db, the password may be left blank.');
    AdminAccountPage.Add('Administrator username:', False);
    AdminAccountPage.Add('Password (minimum 12 characters):', True);
    AdminAccountPage.Add('Confirm password:', True);
    AdminAccountPage.Values[0] := 'admin';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
begin
  Result := '';
  if (AdminPasswordOverride = '') and (AdminAccountPage <> nil) and
    (AdminAccountPage.Values[1] = '') and (not HasExistingDatabase) then
    Result := 'A first-time silent CAMS installation requires /AdminUsername and /AdminPassword. The password must contain at least 12 characters.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Username: string;
  Password: string;
begin
  Result := True;
  if (AdminAccountPage = nil) or (CurPageID <> AdminAccountPage.ID) then Exit;

  Username := Trim(AdminAccountPage.Values[0]);
  Password := AdminAccountPage.Values[1];
  if not IsValidAdminUsername(Username) then
  begin
    MsgBox('The administrator username must be 3-50 letters, numbers, dots, underscores, or hyphens.', mbError, MB_OK);
    Result := False;
  end
  else if (Password = '') and (not HasExistingDatabase) then
  begin
    MsgBox('A password is required when installing a new CAMS database.', mbError, MB_OK);
    Result := False;
  end
  else if (Password <> '') and (Length(Password) < 12) then
  begin
    MsgBox('The administrator password must contain at least 12 characters.', mbError, MB_OK);
    Result := False;
  end
  else if Password <> AdminAccountPage.Values[2] then
  begin
    MsgBox('The administrator passwords do not match.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Username: string;
  Password: string;
begin
  if CurStep = ssPostInstall then
  begin
    if AdminUsernameOverride <> '' then
      Username := AdminUsernameOverride
    else
      Username := Trim(AdminAccountPage.Values[0]);

    if AdminPasswordOverride <> '' then
      Password := AdminPasswordOverride
    else
      Password := AdminAccountPage.Values[1];

    if not SetProcessEnvironmentVariable('Cams__InitialAdminUsername', Username) then
      RaiseException('Could not configure the initial CAMS administrator username.');
    if not SetProcessEnvironmentVariable('Cams__InitialAdminPassword', Password) then
      RaiseException('Could not configure the initial CAMS administrator password.');
  end
  else if CurStep = ssDone then
  begin
    SetProcessEnvironmentVariable('Cams__InitialAdminUsername', '');
    SetProcessEnvironmentVariable('Cams__InitialAdminPassword', '');
  end;
end;
