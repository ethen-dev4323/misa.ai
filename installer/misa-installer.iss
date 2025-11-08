; MISA AI Installer Script
; Inno Setup Configuration for Zero-Setup Installation

#define MyAppName "MISA AI"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MISA AI Technologies"
#define MyAppURL "https://misa.ai"
#define MyAppExeName "MISA.exe"
#define MyAppAssocName "MISA AI Assistant"
#define MyAppAssocExt ".misa"
#define MyAppAssocKey "MISAProject"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
AppId={{A5B6C7D8-E9F0-1234-5678-90ABCDEF1234}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Uncomment the following line to run in non administrative install mode (install for current user only.)
;PrivilegesRequired=lowest
PrivilegesRequired=admin
OutputDir=build
OutputBaseFilename=misa-ai-installer
SetupIconFile=resources\misa-icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardImageFile=resources\wizard-image.bmp
WizardSmallImageFile=resources\wizard-small.bmp
LicenseFile=resources\license.txt
InfoBeforeFile=resources\info-before.txt
InfoAfterFile=resources\info-after.txt
MinVersion=10.0
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
UsePreviousUserInfo=yes

; Windows 10/11 detection and requirements
[Code]
function IsWindows10OrLater(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major > 10) or (Version.Major = 10) and (Version.Minor >= 0);
end;

function Is64BitWindows: Boolean;
begin
  Result := Is64BitInstallMode;
end;

function CheckSystemRequirements(): Boolean;
var
  Memory: DWORD;
  DiskSpace: Int64;
  ResultCode: Integer;
begin
  Result := True;

  // Check Windows version
  if not IsWindows10OrLater then
  begin
    MsgBox('MISA AI requires Windows 10 or later. Please upgrade your operating system.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Check 64-bit architecture
  if not Is64BitWindows then
  begin
    MsgBox('MISA AI requires a 64-bit version of Windows.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Check RAM (minimum 8GB)
  Memory := GetSystemMemory div (1024 * 1024); // Convert to MB
  if Memory < 8192 then
  begin
    if MsgBox('MISA AI requires at least 8GB of RAM. Your system has ' + IntToStr(Memory div 1024) + 'GB. Continue anyway?', mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;

  // Check disk space (minimum 50GB)
  DiskSpace := GetDiskSpace(ExpandConstant('{app}')) div (1024 * 1024 * 1024); // Convert to GB
  if DiskSpace < 50 then
  begin
    MsgBox('MISA AI requires at least 50GB of free disk space. Your system has ' + IntToStr(DiskSpace) + 'GB available.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Check for admin privileges
  if not IsAdminLoggedOn then
  begin
    MsgBox('MISA AI requires administrator privileges for installation.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := CheckSystemRequirements();

  if Result then
  begin
    Log('System requirements check passed');

    // Create installation log
    Log('Starting MISA AI installation');
    Log('Windows version: ' + GetWindowsVersionString);
    Log('64-bit Windows: ' + BoolToStr(Is64BitWindows));
    Log('RAM: ' + IntToStr(GetSystemMemory div (1024 * 1024)) + 'MB');
    Log('Disk space: ' + IntToStr(GetDiskSpace(ExpandConstant('{app}')) div (1024 * 1024 * 1024)) + 'GB');
  end;
end;

[Types]
Name: "full"; Description: "Full installation"; Flags: iscustom
Name: "compact"; Description: "Compact installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "core"; Description: "MISA AI Core Engine"; Types: full compact custom; Flags: fixed
Name: "models"; Description: "AI Models (Large Download)"; Types: full
Name: "development"; Description: "Development Tools"; Types: full
Name: "documentation"; Description: "Documentation"; Types: full

[Dirs]
Name: "{app}\data"
Name: "{app}\models"
Name: "{app}\logs"
Name: "{app}\config"
Name: "{app}\temp"

[Files]
; Core Application Files
Source: "src\MISA.Core\bin\Release\net8.0-windows\*"; DestDir: "{app}\core"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core
Source: "src\MISA.Personality\bin\Release\net8.0\*"; DestDir: "{app}\personality"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core
Source: "src\MISA.OCR\bin\Release\net8.0\*"; DestDir: "{app}\ocr"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core
Source: "src\MISA.Remote\bin\Release\net8.0\*"; DestDir: "{app}\remote"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core
Source: "src\MISA.Updater\bin\Release\net8.0\*"; DestDir: "{app}\updater"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core
Source: "src\MISA.Ollama\bin\Release\net8.0\*"; DestDir: "{app}\ollama"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core
Source: "src\MISA.Memory\bin\Release\net8.0\*"; DestDir: "{app}\memory"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core
Source: "src\MISA.Builder\bin\Release\net8.0\*"; DestDir: "{app}\builder"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: development
Source: "src\MISA.Background\bin\Release\net8.0\*"; DestDir: "{app}\background"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core

; Configuration Files
Source: "config\*"; DestDir: "{app}\config"; Flags: ignoreversion recursesubdirs; Components: core

; Prerequisites
Source: "prerequisites\dotnet-runtime-8.0.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: core
Source: "prerequisites\vcredist2022.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: core
Source: "prerequisites\ollama-setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: core

; Essential Models (downloaded during installation)
Source: "models\README.md"; DestDir: "{app}\models"; Flags: ignoreversion; Components: models

; Documentation
Source: "docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs; Components: documentation

; Start Menu and Desktop Files
Source: "resources\misa-icon.ico"; DestDir: "{app}"; Flags: ignoreversion; Components: core

; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\core\{#MyAppExeName}"; IconFilename: "{app}\misa-icon.ico"; Comment: "MISA AI Assistant"; Components: core
Name: "{group}\{cm:ProgramOnTheWeb,{#MyAppName}}"; Filename: "{#MyAppURL}"; Components: core
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Components: core
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\core\{#MyAppExeName}"; IconFilename: "{app}\misa-icon.ico"; Tasks: desktopicon; Components: core
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\core\{#MyAppExeName}"; Tasks: startupicon; Components: core

[Run]
; Install prerequisites silently
Filename: "{tmp}\dotnet-runtime-8.0.exe"; Parameters: "/quiet"; StatusMsg: "Installing .NET 8 Runtime..."; Components: core; Flags: runhidden waituntilterminated
Filename: "{tmp}\vcredist2022.exe"; Parameters: "/quiet"; StatusMsg: "Installing Visual C++ Redistributable..."; Components: core; Flags: runhidden waituntilterminated
Filename: "{tmp}\ollama-setup.exe"; Parameters: "/S"; StatusMsg: "Installing Ollama..."; Components: core; Flags: runhidden waituntilterminated

; Configure Windows Firewall
Filename: "{cmd}"; Parameters: "/c netsh advfirewall firewall add rule name=""MISA AI Core"" dir=in action=allow program=""{app}\core\{#MyAppExeName}"" enable=yes"; StatusMsg: "Configuring firewall..."; Components: core; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c netsh advfirewall firewall add rule name=""MISA AI WebSocket"" dir=in action=allow protocol=TCP localport=8080 enable=yes"; StatusMsg: "Configuring firewall for WebSocket..."; Components: core; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c netsh advfirewall firewall add rule name=""MISA AI HTTP API"" dir=in action=allow protocol=TCP localport=8081 enable=yes"; StatusMsg: "Configuring firewall for HTTP API..."; Components: core; Flags: runhidden waituntilterminated

; Install Windows Service
Filename: "{app}\core\{#MyAppExeName}"; Parameters: "--install-service"; StatusMsg: "Installing MISA AI Windows Service..."; Components: core; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "start ""MISA AI Service"""; StatusMsg: "Starting MISA AI Service..."; Components: core; Flags: runhidden waituntilterminated

; Launch application
Filename: "{app}\core\{#MyAppExeName}"; Description: "Launch MISA AI"; Flags: nowait postinstall skipifsilent; Components: core

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Components: core
Name: "startupicon"; Description: "Start MISA AI with Windows"; GroupDescription: "Startup Options"; Components: core
Name: "firstrun"; Description: "Run first-time configuration wizard"; GroupDescription: "Initial Setup"; Components: core

[Registry]
; File associations
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocExt}\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletekey; Components: core
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocName}"; Flags: uninsdeletekey; Components: core
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\misa-icon.ico,0"; Components: core
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\core\{#MyAppExeName}"" ""%1"""; Components: core

; Auto-start registry entries
Root: HKLM; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey; Components: core
Root: HKLM; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey; Components: core

[UninstallDelete]
Type: filesandordirs; Name: "{app}\data"
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\temp"
Type: filesandordirs; Name: "{app}\models"

[UninstallRun]
; Stop and remove Windows Service
Filename: "sc.exe"; Parameters: "stop ""MISA AI Service"""; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "delete ""MISA AI Service"""; Flags: runhidden waituntilterminated

; Remove firewall rules
Filename: "{cmd}"; Parameters: "/c netsh advfirewall firewall delete rule name=""MISA AI Core"""; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c netsh advfirewall firewall delete rule name=""MISA AI WebSocket"""; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c netsh advfirewall firewall delete rule name=""MISA AI HTTP API"""; Flags: runhidden waituntilterminated

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  ErrorCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // Create initial configuration
    Log('Creating initial configuration...');

    // Initialize data directories
    CreateDir(ExpandConstant('{app}\data'));
    CreateDir(ExpandConstant('{app}\logs'));
    CreateDir(ExpandConstant('{app}\temp'));

    // Set permissions
    if FileExists(ExpandConstant('{app}\data')) then
    begin
      Log('Setting permissions for data directory');
      Exec(ExpandConstant('{cmd}'), '/c icacls "' + ExpandConstant('{app}\data') + '" /grant Users:F /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
    end;

    // Generate unique device ID
    Log('Generating unique device ID');
    if RegWriteStringValue(HKEY_LOCAL_MACHINE, 'Software\{#MyAppName}', 'DeviceID', CreateGuidAsString) then
      Log('Device ID created successfully')
    else
      Log('Failed to create device ID');

    Log('Installation completed successfully');
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  // Skip the "Ready to Install" page if in silent mode
  Result := (PageID = wpReady) and WizardSilent;
end;

function GetCustomSetupExitCode: Integer;
begin
  if (GetWindowLongPtr(WizardForm.Handle, GWL_STYLE) and WS_VISIBLE) = 0 then
    Result := 0
  else
    Result := 1;
end;

procedure DeinitializeSetup();
begin
  Log('MISA AI installer deinitializing...');

  // Clean up temporary files
  DeleteFile(ExpandConstant('{tmp}\dotnet-runtime-8.0.exe'));
  DeleteFile(ExpandConstant('{tmp}\vcredist2022.exe'));
  DeleteFile(ExpandConstant('{tmp}\ollama-setup.exe'));

  Log('Installer cleanup completed');
end;