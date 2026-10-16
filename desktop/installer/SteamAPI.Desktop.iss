; SteamAPI Desktop - Inno Setup 6 Professional Windows Installer Script
; Produces: SteamAPI-Desktop-Setup-v0.1.0-win-x64.exe
; Free, open-source, non-administrative (per-user) setup.

#define MyAppName "SteamAPI Desktop"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Fatih Emre Sarman"
#define MyAppURL "https://github.com/fatihemres"
#define MyAppExeName "SteamAPI.Desktop.exe"
#define MyAppId "{{B42B8D30-80E5-46D9-8E92-E23B86BC7B62}"

#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\SteamAPI.Desktop-v0.1.0-win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts\releases"
#endif

[Setup]
; Application Identity & Metadata
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/SteamAPI
AppUpdatesURL={#MyAppURL}/SteamAPI/releases

; Architecture (64-bit Windows)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Non-Administrative / Per-User Installation
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Packaging & Compression
OutputDir={#OutputDir}
OutputBaseFilename=SteamAPI-Desktop-Setup-v0.1.0-win-x64
Compression=lzma2/max
SolidCompression=yes
SetupIconFile=..\SteamAPI.Desktop\Assets\avalonia-logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; UI & Lifecycle Safety
WizardStyle=modern
DisableDirPage=no
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName},SteamAPI.exe
RestartApplications=no

; Version Resource Info
VersionInfoVersion=0.1.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion=0.1.0.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Install full self-contained application including api/ companion backend
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu Application Shortcut
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
; Optional Desktop Shortcut
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Optional Post-Install Launch Action
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
