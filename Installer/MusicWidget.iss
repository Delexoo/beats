; ============================================================================
; Beats — Inno Setup installer script
; ============================================================================
;
; Builds a single .exe installer that drops the published, self-contained build
; into C:\Program Files\Beats, adds Start Menu + Desktop shortcuts, and
; uses the app's icon throughout the UI.
;
; Build (from repo root):
;   powershell -ExecutionPolicy Bypass -File Installer\Build-Installer.ps1
;
; Output: Installer\Output\Beats-Setup-x64.exe
; ============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "2.2.0"
#endif

#define MyAppName "Beats"
#define MyAppPublisher "Delexo"
#define MyAppURL "https://delexoo.github.io/beats/"
#define MyAppSupportURL "https://delexoo.github.io/beats/help.html"
#define MyAppUpdatesURL "https://github.com/Delexoo/beats/releases"
#define MyAppExeName "Beats.exe"

[Setup]
AppId={{8E3B4F0A-6B5A-4D6E-9C3F-7F1E1A2D9C4A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (C) {#MyAppPublisher} 2026
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppSupportURL}
AppUpdatesURL={#MyAppUpdatesURL}
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
UsedUserAreasWarning=no
DisableDirPage=no
DisableReadyPage=no
ShowLanguageDialog=no
LicenseFile=..\LICENSE
InfoBeforeFile=welcome.txt
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer for Windows (64-bit)
VersionInfoProductName={#MyAppName}
VersionInfoCopyright=Copyright (C) {#MyAppPublisher} 2026

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%nBeats is a free floating desktop music player for Windows. Play local playlists, download from the web, and control everything from a minimal widget on your screen.%n%nNo account required.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Launch {#MyAppName} when Windows starts"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\MusicWidget\bin\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\MusicWidget\Assets\AppIcon.png"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "..\MusicWidget\Assets\AppIcon.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{group}\Help && manual"; Filename: "{#MyAppSupportURL}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
