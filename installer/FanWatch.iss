; FanWatch installer (Inno Setup 6). Build with build-installer.ps1, which passes AppVersion and SourceDir.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\app"
#endif

[Setup]
AppId={{6F1D2C4B-8E2A-4C7D-9B1F-3A5E7D9C0B42}
AppName=FanWatch
AppVersion={#AppVersion}
AppVerName=FanWatch {#AppVersion}
AppPublisher=Ahmad Farhan
VersionInfoVersion={#AppVersion}
VersionInfoDescription=FanWatch installer
; Per-user install: no admin prompt; goes to %LOCALAPPDATA%\Programs\FanWatch.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\FanWatch
DefaultGroupName=FanWatch
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\FanWatch.exe
UninstallDisplayName=FanWatch
SetupIconFile=..\src\FanWatch\fan.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=FanWatch-Setup-{#AppVersion}
; FanWatch is closed by [Code]/[UninstallRun] below, not by Restart Manager: Restart Manager also lists Windows'
; compatibility-appraisal service (which scans new exes), can't stop it from a per-user setup, and then closes nothing.
; No AppMutex either: with it, a silent uninstall aborts when FanWatch is open instead of closing it.
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &Desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\FanWatch"; Filename: "{app}\FanWatch.exe"; Comment: "See what is making your fan loud"
Name: "{group}\FanWatch (Ports)"; Filename: "{app}\FanWatch.exe"; Parameters: "--ports"; Comment: "Open FanWatch on the Ports tab"
Name: "{group}\Uninstall FanWatch"; Filename: "{uninstallexe}"
Name: "{autodesktop}\FanWatch"; Filename: "{app}\FanWatch.exe"; Tasks: desktopicon; Comment: "See what is making your fan loud"

[Run]
Filename: "{app}\FanWatch.exe"; Description: "Open FanWatch now"; Flags: nowait postinstall skipifsilent

[Code]
// Close a running FanWatch before its files are replaced: ask nicely (WM_CLOSE), then force after 2 s.
procedure CloseFanWatch();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM FanWatch.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(2000);
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM FanWatch.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseFanWatch();
  Result := '';
end;

[UninstallRun]
; Make sure FanWatch isn't running while its files are removed.
Filename: "{cmd}"; Parameters: "/C taskkill /IM FanWatch.exe /F"; Flags: runhidden; RunOnceId: "StopFanWatch"
