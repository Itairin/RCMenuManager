; Inno Setup script for RCMenuManager
; Builds a Win10/Win11 x64 installer for the self-contained publish output.

#define AppName        "RCMenuManager"
#define AppPublisher   "RCMenuManager Contributors"
#define AppURL         "https://github.com/your-name/RCMenuManager"
#define AppExe         "RCMenuManager.exe"
#ifndef AppVersion
  #define AppVersion "0.6.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\selfcontained"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish\installer"
#endif

[Setup]
AppId={{D8B5A0A4-3C7E-4F12-9B49-3F2D2A1B6E55}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}.0
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}-x64
SetupIconFile=
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
CloseApplications=force
MinVersion=10.0.17763
LicenseFile=..\LICENSE

[Languages]
Name: "english";      MessagesFile: "compiler:Default.isl"
Name: "chinesesimp";  MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Resources\*"; DestDir: "{app}\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

; Intentionally keep %LocalAppData%\RCMenuManager (backups, crash.log) so users
; can re-install without losing recovery data. Uncomment if you really want
; uninstall to wipe everything.
; [UninstallDelete]
; Type: filesandordirs; Name: "{localappdata}\{#AppName}"
