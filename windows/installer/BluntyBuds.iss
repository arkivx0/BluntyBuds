; BluntyBuds Windows installer (Inno Setup 6)
; Build:  iscc /DAppVersion=1.4.0 /DSourceExe=dist\BluntyBuds.exe BluntyBuds.iss
; Paths are relative to this script's folder (windows\installer).

#ifndef AppVersion
  #define AppVersion "1.4.0"
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

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM BluntyBuds.exe"; Flags: runhidden; RunOnceId: "KillBluntyBuds"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\BluntyBuds"

[Code]
procedure KillRunningApp();
var Rc: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM BluntyBuds.exe', '', SW_HIDE, ewWaitUntilTerminated, Rc);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillRunningApp();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Remove the Start-with-Windows entry the app may have created }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'BluntyBuds');
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    { Settings and presets are kept unless the user opts in (never in silent mode) }
    if (not UninstallSilent) and DirExists(ExpandConstant('{userappdata}\BluntyBuds')) then
      if MsgBox('Also delete your BluntyBuds settings and presets?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
        exit
      else
        DelTree(ExpandConstant('{userappdata}\BluntyBuds'), True, True, True);
  end;
end;
