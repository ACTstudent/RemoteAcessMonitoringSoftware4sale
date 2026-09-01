; CAMS Server - Inno Setup installer definition
; Builds "CAMS-Server-Setup.exe" - installs the server, opens the firewall,
; performs clean installation, and optionally launches the server on startup.

#define MyAppName "CAMS Server"
#ifndef MyAppVersion
  #define MyAppVersion "2.9.3"
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
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; StatusMsg: "Initializing the CAMS server and configured accounts..."

[Code]
function SetProcessEnvironmentVariable(Name: string; Value: string): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

var
  AdminAccountPage: TInputQueryWizardPage;
  TeacherAccountPage: TInputQueryWizardPage;
  StudentAccountPage: TInputQueryWizardPage;
  AdminUsernameOverride: string;
  AdminPasswordOverride: string;
  TeacherUsernameOverride: string;
  TeacherPasswordOverride: string;
  TeacherFirstNameOverride: string;
  TeacherLastNameOverride: string;
  StudentUsernameOverride: string;
  StudentPasswordOverride: string;
  StudentNumberOverride: string;
  StudentFirstNameOverride: string;
  StudentLastNameOverride: string;

function IsValidAccountIdentifier(const Value: string): Boolean;
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
  TeacherUsernameOverride := Trim(ExpandConstant('{param:TeacherUsername}'));
  TeacherPasswordOverride := ExpandConstant('{param:TeacherPassword}');
  TeacherFirstNameOverride := Trim(ExpandConstant('{param:TeacherFirstName|Seeded}'));
  TeacherLastNameOverride := Trim(ExpandConstant('{param:TeacherLastName|Teacher}'));
  StudentUsernameOverride := Trim(ExpandConstant('{param:StudentUsername}'));
  StudentPasswordOverride := ExpandConstant('{param:StudentPassword}');
  StudentNumberOverride := Trim(ExpandConstant('{param:StudentNumber|STUDENT-001}'));
  StudentFirstNameOverride := Trim(ExpandConstant('{param:StudentFirstName|Seeded}'));
  StudentLastNameOverride := Trim(ExpandConstant('{param:StudentLastName|Student}'));
  Result := True;

  if (AdminUsernameOverride = '') <> (AdminPasswordOverride = '') then
  begin
    MsgBox('/AdminUsername and /AdminPassword must be supplied together.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (AdminUsernameOverride <> '') and (not IsValidAccountIdentifier(AdminUsernameOverride)) then
  begin
    MsgBox('/AdminUsername must be 3-50 letters, numbers, dots, underscores, or hyphens.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (AdminPasswordOverride <> '') and (Length(AdminPasswordOverride) < 12) then
  begin
    MsgBox('/AdminPassword must contain at least 12 characters.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (TeacherUsernameOverride = '') <> (TeacherPasswordOverride = '') then
  begin
    MsgBox('/TeacherUsername and /TeacherPassword must be supplied together.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if (TeacherUsernameOverride <> '') and (not IsValidAccountIdentifier(TeacherUsernameOverride)) then
  begin
    MsgBox('/TeacherUsername must be 3-50 letters, numbers, dots, underscores, or hyphens.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if (TeacherPasswordOverride <> '') and (Length(TeacherPasswordOverride) < 8) then
  begin
    MsgBox('/TeacherPassword must contain at least 8 characters.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (StudentUsernameOverride = '') <> (StudentPasswordOverride = '') then
  begin
    MsgBox('/StudentUsername and /StudentPassword must be supplied together.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if (StudentUsernameOverride <> '') and
    ((not IsValidAccountIdentifier(StudentUsernameOverride)) or (not IsValidAccountIdentifier(StudentNumberOverride))) then
  begin
    MsgBox('/StudentUsername and /StudentNumber must be 3-50 letters, numbers, dots, underscores, or hyphens.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if (StudentPasswordOverride <> '') and (Length(StudentPasswordOverride) < 8) then
  begin
    MsgBox('/StudentPassword must contain at least 8 characters.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure InitializeWizard;
var
  PreviousPageId: Integer;
begin
  PreviousPageId := wpSelectDir;
  if (AdminUsernameOverride = '') and (AdminPasswordOverride = '') then
  begin
    AdminAccountPage := CreateInputQueryPage(PreviousPageId,
      'Initial Administrator', 'Create the first CAMS administrator',
      'Enter credentials for the first local administrator. Existing accounts are never overwritten. On an upgrade with an existing CAMS.db, the password may be left blank.');
    AdminAccountPage.Add('Administrator username:', False);
    AdminAccountPage.Add('Password (minimum 12 characters):', True);
    AdminAccountPage.Add('Confirm password:', True);
    AdminAccountPage.Values[0] := 'admin';
    PreviousPageId := AdminAccountPage.ID;
  end;

  if (TeacherUsernameOverride = '') and (TeacherPasswordOverride = '') then
  begin
    TeacherAccountPage := CreateInputQueryPage(PreviousPageId,
      'Initial Teacher (Optional)', 'Create a CAMS teacher account',
      'Enter a teacher account for immediate classroom setup, or leave the password blank to skip it. Existing accounts are never overwritten.');
    TeacherAccountPage.Add('Teacher username:', False);
    TeacherAccountPage.Add('First name:', False);
    TeacherAccountPage.Add('Last name:', False);
    TeacherAccountPage.Add('Password (minimum 8 characters):', True);
    TeacherAccountPage.Add('Confirm password:', True);
    TeacherAccountPage.Values[0] := 'teacher';
    TeacherAccountPage.Values[1] := 'Seeded';
    TeacherAccountPage.Values[2] := 'Teacher';
    PreviousPageId := TeacherAccountPage.ID;
  end;

  if (StudentUsernameOverride = '') and (StudentPasswordOverride = '') then
  begin
    StudentAccountPage := CreateInputQueryPage(PreviousPageId,
      'Initial Student (Optional)', 'Create a CAMS student account',
      'Enter a student account for immediate client testing, or leave the password blank to skip it. The workstation profile will be created automatically on first client login.');
    StudentAccountPage.Add('Student username:', False);
    StudentAccountPage.Add('Student number:', False);
    StudentAccountPage.Add('First name:', False);
    StudentAccountPage.Add('Last name:', False);
    StudentAccountPage.Add('Password (minimum 8 characters):', True);
    StudentAccountPage.Add('Confirm password:', True);
    StudentAccountPage.Values[0] := 'student1';
    StudentAccountPage.Values[1] := 'STUDENT-001';
    StudentAccountPage.Values[2] := 'Seeded';
    StudentAccountPage.Values[3] := 'Student';
    PreviousPageId := StudentAccountPage.ID;
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
  if (AdminAccountPage <> nil) and (CurPageID = AdminAccountPage.ID) then
  begin
    Username := Trim(AdminAccountPage.Values[0]);
    Password := AdminAccountPage.Values[1];
    if not IsValidAccountIdentifier(Username) then
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
  end
  else if (TeacherAccountPage <> nil) and (CurPageID = TeacherAccountPage.ID) then
  begin
    Password := TeacherAccountPage.Values[3];
    if Password = '' then
      Result := TeacherAccountPage.Values[4] = ''
    else if not IsValidAccountIdentifier(Trim(TeacherAccountPage.Values[0])) then
    begin
      MsgBox('The teacher username must be 3-50 letters, numbers, dots, underscores, or hyphens.', mbError, MB_OK);
      Result := False;
    end
    else if (Trim(TeacherAccountPage.Values[1]) = '') or (Trim(TeacherAccountPage.Values[2]) = '') then
    begin
      MsgBox('The teacher first and last names are required.', mbError, MB_OK);
      Result := False;
    end
    else if Length(Password) < 8 then
    begin
      MsgBox('The teacher password must contain at least 8 characters.', mbError, MB_OK);
      Result := False;
    end
    else if Password <> TeacherAccountPage.Values[4] then
    begin
      MsgBox('The teacher passwords do not match.', mbError, MB_OK);
      Result := False;
    end;
  end
  else if (StudentAccountPage <> nil) and (CurPageID = StudentAccountPage.ID) then
  begin
    Password := StudentAccountPage.Values[4];
    if Password = '' then
      Result := StudentAccountPage.Values[5] = ''
    else if (not IsValidAccountIdentifier(Trim(StudentAccountPage.Values[0]))) or
      (not IsValidAccountIdentifier(Trim(StudentAccountPage.Values[1]))) then
    begin
      MsgBox('The student username and number must be 3-50 letters, numbers, dots, underscores, or hyphens.', mbError, MB_OK);
      Result := False;
    end
    else if (Trim(StudentAccountPage.Values[2]) = '') or (Trim(StudentAccountPage.Values[3]) = '') then
    begin
      MsgBox('The student first and last names are required.', mbError, MB_OK);
      Result := False;
    end
    else if Length(Password) < 8 then
    begin
      MsgBox('The student password must contain at least 8 characters.', mbError, MB_OK);
      Result := False;
    end
    else if Password <> StudentAccountPage.Values[5] then
    begin
      MsgBox('The student passwords do not match.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Username: string;
  Password: string;
  TeacherUsername: string;
  TeacherPassword: string;
  TeacherFirstName: string;
  TeacherLastName: string;
  StudentUsername: string;
  StudentPassword: string;
  StudentNumber: string;
  StudentFirstName: string;
  StudentLastName: string;
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

    if TeacherPasswordOverride <> '' then
    begin
      TeacherUsername := TeacherUsernameOverride;
      TeacherPassword := TeacherPasswordOverride;
      TeacherFirstName := TeacherFirstNameOverride;
      TeacherLastName := TeacherLastNameOverride;
    end
    else
    begin
      TeacherUsername := Trim(TeacherAccountPage.Values[0]);
      TeacherFirstName := Trim(TeacherAccountPage.Values[1]);
      TeacherLastName := Trim(TeacherAccountPage.Values[2]);
      TeacherPassword := TeacherAccountPage.Values[3];
    end;
    SetProcessEnvironmentVariable('Cams__SeededTeacherUsername', TeacherUsername);
    SetProcessEnvironmentVariable('Cams__SeededTeacherFirstName', TeacherFirstName);
    SetProcessEnvironmentVariable('Cams__SeededTeacherLastName', TeacherLastName);
    if not SetProcessEnvironmentVariable('Cams__SeededTeacherPassword', TeacherPassword) then
      RaiseException('Could not configure the initial CAMS teacher password.');

    if StudentPasswordOverride <> '' then
    begin
      StudentUsername := StudentUsernameOverride;
      StudentPassword := StudentPasswordOverride;
      StudentNumber := StudentNumberOverride;
      StudentFirstName := StudentFirstNameOverride;
      StudentLastName := StudentLastNameOverride;
    end
    else
    begin
      StudentUsername := Trim(StudentAccountPage.Values[0]);
      StudentNumber := Trim(StudentAccountPage.Values[1]);
      StudentFirstName := Trim(StudentAccountPage.Values[2]);
      StudentLastName := Trim(StudentAccountPage.Values[3]);
      StudentPassword := StudentAccountPage.Values[4];
    end;
    SetProcessEnvironmentVariable('Cams__SeededStudentUsername', StudentUsername);
    SetProcessEnvironmentVariable('Cams__SeededStudentNumber', StudentNumber);
    SetProcessEnvironmentVariable('Cams__SeededStudentFirstName', StudentFirstName);
    SetProcessEnvironmentVariable('Cams__SeededStudentLastName', StudentLastName);
    if not SetProcessEnvironmentVariable('Cams__SeededStudentPassword', StudentPassword) then
      RaiseException('Could not configure the initial CAMS student password.');
  end
  else if CurStep = ssDone then
  begin
    SetProcessEnvironmentVariable('Cams__InitialAdminUsername', '');
    SetProcessEnvironmentVariable('Cams__InitialAdminPassword', '');
    SetProcessEnvironmentVariable('Cams__SeededTeacherUsername', '');
    SetProcessEnvironmentVariable('Cams__SeededTeacherFirstName', '');
    SetProcessEnvironmentVariable('Cams__SeededTeacherLastName', '');
    SetProcessEnvironmentVariable('Cams__SeededTeacherPassword', '');
    SetProcessEnvironmentVariable('Cams__SeededStudentUsername', '');
    SetProcessEnvironmentVariable('Cams__SeededStudentNumber', '');
    SetProcessEnvironmentVariable('Cams__SeededStudentFirstName', '');
    SetProcessEnvironmentVariable('Cams__SeededStudentLastName', '');
    SetProcessEnvironmentVariable('Cams__SeededStudentPassword', '');
  end;
end;
