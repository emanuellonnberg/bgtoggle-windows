; BgToggle installer. Build after publishing both apps into ..\publish:
;   iscc /DAppVersion=1.2.3 installer\BgToggle.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; Never change AppId: upgrades and uninstall are keyed on it.
AppId={{8C7A3F52-6E1B-4C2D-9A57-3B1E0D4F6A21}
AppName=BgToggle
AppVersion={#AppVersion}
AppVerName=BgToggle {#AppVersion}
AppPublisher=emanuellonnberg
AppPublisherURL=https://github.com/emanuellonnberg/bgtoggle-windows
AppSupportURL=https://github.com/emanuellonnberg/bgtoggle-windows/issues
AppUpdatesURL=https://github.com/emanuellonnberg/bgtoggle-windows/releases
DefaultDirName={autopf}\BgToggle
; The LocalSystem helper runs from {app}, so it must be the admin-only
; Program Files folder: no directory page, and PrepareToInstall rejects /DIR=.
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The tray is stopped explicitly in PrepareToInstall; Restart Manager can't
; reliably close a window-less tray app.
CloseApplications=no
SourceDir=..
OutputDir=installer\Output
OutputBaseFilename=BgToggle-Setup-{#AppVersion}
UninstallDisplayIcon={app}\BgToggle.exe
LicenseFile=LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
; Upgrades keep whatever the user chose later in the tray menu.
Name: "startatlogon"; Description: "Start BgToggle when I sign in"; GroupDescription: "Startup:"; Check: not IsUpgrade

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\BgToggle"; Filename: "{app}\BgToggle.exe"

[Run]
; As the user who started Setup (not the elevated admin), so the logon
; autostart lands in their HKCU.
Filename: "{app}\BgToggle.exe"; Parameters: "--first-run --start-at-logon={code:LogonFlag} --exit-after-setup"; Flags: runasoriginaluser runhidden waituntilterminated; StatusMsg: "Configuring BgToggle..."
Filename: "{app}\BgToggle.exe"; Description: "Launch BgToggle"; Flags: runasoriginaluser nowait postinstall skipifsilent

[UninstallDelete]
; Written by the helper service at runtime, so Setup doesn't track it.
Type: filesandordirs; Name: "{app}\logs"

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM BgToggle.exe /F"; Flags: runhidden waituntilterminated; RunOnceId: "StopTray"
Filename: "{app}\BgToggle.exe"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreAutostart"
; net stop waits until the service has stopped, so its exe can be deleted.
Filename: "{sys}\net.exe"; Parameters: "stop BgToggleHelper"; Flags: runhidden waituntilterminated; RunOnceId: "StopHelper"
Filename: "{sys}\sc.exe"; Parameters: "delete BgToggleHelper"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteHelper"

[Code]
var
  WasInstalled: Boolean;

function InitializeSetup(): Boolean;
begin
  // Captured up front: by the time [Run] executes, this install has
  // created the uninstall key itself.
  WasInstalled := RegKeyExists(HKLM,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C7A3F52-6E1B-4C2D-9A57-3B1E0D4F6A21}_is1');
  Result := True;
end;

function IsUpgrade(): Boolean;
begin
  Result := WasInstalled;
end;

function LogonFlag(Param: String): String;
begin
  // 'keep' is neither on nor off, so the tray leaves the current setting alone.
  if IsUpgrade() then
    Result := 'keep'
  else if WizardIsTaskSelected('startatlogon') then
    Result := 'on'
  else
    Result := 'off';
end;

function RunHidden(const FileName, Params: String): Integer;
var
  Code: Integer;
begin
  if not Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Log(Format('%s %s -> %d', [FileName, Params, Code]));
  Result := Code;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if CompareText(RemoveBackslash(WizardDirValue), ExpandConstant('{autopf}\BgToggle')) <> 0 then
  begin
    Result := 'BgToggle must be installed in ' + ExpandConstant('{autopf}\BgToggle') +
              ' so its helper service runs from a folder only administrators can change.';
    Exit;
  end;

  // Upgrade: free the files the running tray and helper hold.
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/IM BgToggle.exe /F');
  RunHidden(ExpandConstant('{sys}\net.exe'), 'stop BgToggleHelper');
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Sc, Args: String;
begin
  if CurStep <> ssPostInstall then Exit;

  Sc := ExpandConstant('{sys}\sc.exe');
  Args := 'BgToggleHelper binPath= "\"' + ExpandConstant('{app}') + '\BgToggle.Helper.exe\"" ' +
          'start= delayed-auto obj= LocalSystem DisplayName= "BgToggle Helper"';
  if RunHidden(Sc, 'create ' + Args) <> 0 then
    RunHidden(Sc, 'config ' + Args);
  RunHidden(Sc, 'description BgToggleHelper "Lets BgToggle stop and start vendor services listed in its installed recipes."');
  RunHidden(Sc, 'failure BgToggleHelper reset= 86400 actions= restart/5000/restart/5000/restart/60000');
  RunHidden(Sc, 'start BgToggleHelper');

  // The pre-installer "Install autostart (admin)" menu registered this task.
  RunHidden(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN BgToggle /F');
end;
