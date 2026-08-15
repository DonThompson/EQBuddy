; EQBuddy installer — EverQuest Legends session tracker widget
#define AppName "EQBuddy"
; Overridden by scripts\release.ps1 via /DAppVersion=<csproj Version>
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#define AppPublisher "David Edwards"
#define AppExe "EQBuddy.exe"
; The build being replaced, kept beside the new one so a bad update is recoverable
; without a reinstall (see [Icons] and [Code], discussion #158).
#define PrevExe "EQBuddy.previous.exe"

[Setup]
AppId={{7E1B6A94-3C2D-4B77-9F41-EQBUDDY10000}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\EQBuddy
DefaultGroupName=EQBuddy
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=EQBuddySetup
SetupIconFile=..\src\EQBuddy\Assets\EQBuddy.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
; Stamp the setup exe with the app version so the in-app updater can read it.
VersionInfoVersion={#AppVersion}
; Let silent self-updates close the running widget and relaunch it after.
CloseApplications=force
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\dist\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; MIT requires the copyright and permission notice to travel with copies.
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\EQBuddy"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\EQBuddy"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
; The escape hatch. EQBuddy's updater lives inside the widget, so a build that will
; not open takes the only route to the fix with it — v1.84.0 did exactly that and left
; people reinstalling from GitHub by hand, which a casual player will not find
; (discussion #158, n3cr0nk1tt3n). The previous build is kept beside the new one and
; gets its own shortcut: start it, and you have a working widget whose own updater can
; pull the fix. Only appears once there IS a previous build.
Name: "{group}\EQBuddy (previous version)"; Filename: "{app}\{#PrevExe}"; \
    Comment: "Starts the build you had before the last update - use this if the current one will not open, then update from inside it."; \
    Check: PreviousBuildKept

[UninstallDelete]
Type: files; Name: "{app}\{#PrevExe}"

[Code]
var
  KeptPrevious: Boolean;

// Runs BEFORE the new files are copied, which is the only moment the outgoing exe
// still exists. A failure here must never block the install: not having a rollback
// shortcut is a far smaller problem than not being able to update at all.
procedure CurStepChanged(CurStep: TSetupStep);
var
  Current: String;
begin
  if CurStep = ssInstall then
  begin
    Current := ExpandConstant('{app}\{#AppExe}');
    if FileExists(Current) then
      KeptPrevious := CopyFile(Current, ExpandConstant('{app}\{#PrevExe}'), False);
  end;
end;

function PreviousBuildKept: Boolean;
begin
  Result := KeptPrevious;
end;

[Run]
; No skipifsilent: silent self-updates must relaunch the widget when done.
Filename: "{app}\{#AppExe}"; Description: "Launch EQBuddy now"; Flags: nowait postinstall
