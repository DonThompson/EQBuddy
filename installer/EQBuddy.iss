; EQBuddy installer — EverQuest Legends session tracker widget
#define AppName "EQBuddy"
#define AppVersion "1.0.0"
#define AppPublisher "David Edwards"
#define AppExe "EQBuddy.exe"

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

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\dist\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\EQBuddy"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\EQBuddy"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch EQBuddy now"; Flags: nowait postinstall skipifsilent
