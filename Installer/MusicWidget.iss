; ============================================================================
; Beats — Inno Setup installer script
; ============================================================================
;
; Builds a single .exe installer that drops the published, self-contained build
; into C:\Program Files\Beats, adds Start Menu + Desktop shortcuts, and
; uses the app's icon throughout the UI.
;
; Build steps (run from the repo root):
;
;   1. dotnet publish MusicWidget\MusicWidget.csproj -c Release ^
;        -p:PublishProfile=Properties\PublishProfiles\WinX64SelfContained.pubxml
;   2. iscc Installer\MusicWidget.iss
;
; The output installer lands at Installer\Output\Beats-Setup-x64.exe.
; ============================================================================

#define MyAppName "Beats"
#define MyAppPublisher "Delexo"
#define MyAppURL "https://delexo.store"
#define MyAppExeName "Beats.exe"
#define MyAppVersion "2.2.0"

[Setup]
; A pinned GUID identifies this product across upgrades — never change it,
; otherwise existing installs won't be replaced and the user gets two copies.
AppId={{8E3B4F0A-6B5A-4D6E-9C3F-7F1E1A2D9C4A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (C) {#MyAppPublisher} 2026
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
OutputDir=Output
OutputBaseFilename=Beats-Setup-x64
SetupIconFile=..\MusicWidget\Assets\AppIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
; Adding a per-user Startup shortcut from an admin installer is intentional —
; the autostart entry must follow the user (not the machine) so multi-user PCs
; don't autostart for everyone. Inno warns at compile time; silence it here.
UsedUserAreasWarning=no
DisableDirPage=no
DisableReadyPage=no
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Launch {#MyAppName} when Windows starts"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Entire self-contained publish output (apphost + Beats.dll + runtime + libvlc\…).
Source: "..\MusicWidget\bin\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Drop the PNG asset into the install dir so anything else (a website, an
; uninstaller, the user, ...) can find it under Program Files\Beats\Assets.
Source: "..\MusicWidget\Assets\AppIcon.png"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "..\MusicWidget\Assets\AppIcon.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon
; Optional autostart entry — Inno's Startup folder shortcut is the least
; invasive way to do this; uninstalling removes it cleanly.
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The app writes settings/cache under %APPDATA%\Beats; uninstall
; should leave that alone (user data) unless the user opts in via a prompt.
; If you want a full wipe, uncomment the next line.
; Type: filesandordirs; Name: "{userappdata}\Beats"
