; BluntyBuds Windows installer (Inno Setup 6)
; Build:  iscc /DAppVersion=1.4.6 /DSourceExe=dist\BluntyBuds.exe BluntyBuds.iss
; Paths are relative to this script's folder (windows\installer).

#ifndef AppVersion
  #define AppVersion "1.4.6"
#endif
#ifndef SourceExe
  #define SourceExe "dist\BluntyBuds.exe"
#endif

[Setup]
AppId={{4EC3F009-22EE-4C05-B6E8-24FBC2465002}
AppName=BluntyBuds
AppVersion={#AppVersion}
AppVerName=BluntyBuds {#AppVersion}
AppPublisher=arkivx0
AppPublisherURL=https://github.com/arkivx0/BluntyBuds
AppSupportURL=https://github.com/arkivx0/BluntyBuds/issues
DefaultDirName={autopf}\BluntyBuds
DefaultGroupName=BluntyBuds
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=BluntyBuds-{#AppVersion}-setup
SetupIconFile=..\..\art\bluntybuds.ico
UninstallDisplayIcon={app}\BluntyBuds.exe
UninstallDisplayName=BluntyBuds
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
; Restart Manager only considers files being replaced in {app}
CloseApplicationsFilter=BluntyBuds.exe
RestartApplications=no
LicenseFile=..\..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "BluntyBuds.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\BluntyBuds"; Filename: "{app}\BluntyBuds.exe"
Name: "{autodesktop}\BluntyBuds"; Filename: "{app}\BluntyBuds.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BluntyBuds.exe"; Description: "{cm:LaunchProgram,BluntyBuds}"; Flags: nowait postinstall skipifsilent

[Code]
{ Only BluntyBuds.exe processes whose image path is inside the install folder are closed. A portable copy running from
  another folder is never touched (no blanket "taskkill /IM"). Uses WMI Win32_Process.ExecutablePath; if WMI
  is unavailable nothing is killed and Inno Setup's normal in-use handling (Restart Manager on install,
  delete-on-reboot on uninstall) applies. }
function IsInDir(const Path, Dir: String): Boolean;
var D: String;
begin
  D := Lowercase(AddBackslash(Dir));
  Result := (Path <> '') and (Pos(D, Lowercase(Path)) = 1);
end;

procedure CloseAppInDir(const Dir: String);
var Locator, Wmi, Procs, P: Variant; I, Count, Rc: Integer; ExePath: String;
begin
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Wmi.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''BluntyBuds.exe''');
    Count := Procs.Count;
    for I := 0 to Count - 1 do
    begin
      P := Procs.ItemIndex(I);
      ExePath := '';
      if not VarIsNull(P.ExecutablePath) then ExePath := P.ExecutablePath;
      if IsInDir(ExePath, Dir) then
      begin
        Log('Closing ' + ExePath + ' (PID ' + IntToStr(P.ProcessId) + ')');
        Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /PID ' + IntToStr(P.ProcessId), '', SW_HIDE, ewWaitUntilTerminated, Rc);
      end
      else
        Log('Leaving BluntyBuds.exe outside the install folder alone: ' + ExePath);
    end;
  except
    Log('WMI process check unavailable: ' + GetExceptionMessage);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Upgrade: close only the copy installed in the target folder }
  CloseAppInDir(ExpandConstant('{app}'));
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var LocalDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseAppInDir(ExpandConstant('{app}'));
    { Remove the Start-with-Windows entry the app may have created }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'BluntyBuds');
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    { %LOCALAPPDATA%\BluntyBuds only holds crash.log: delete that file, and the folder only if it is then empty }
    LocalDir := ExpandConstant('{localappdata}\BluntyBuds');
    DeleteFile(LocalDir + '\crash.log');
    RemoveDir(LocalDir);
    { Settings and presets are kept unless the user opts in (never in silent mode) }
    if (not UninstallSilent) and DirExists(ExpandConstant('{userappdata}\BluntyBuds')) then
      if MsgBox('Also delete your BluntyBuds settings and presets?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
        exit
      else
        DelTree(ExpandConstant('{userappdata}\BluntyBuds'), True, True, True);
  end;
end;
