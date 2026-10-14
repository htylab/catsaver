; Inno Setup script – builds CatSaver-Setup.exe
; Compile with:  iscc installer\CatSaver.iss   (after running build.ps1)

#define AppName "CatSaver"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6B0E7C1E-3C55-4C8E-9C61-CA75A7E50001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CatSaver
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install: no admin rights needed.
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=CatSaver-Setup
SetupIconFile=..\src\CatSaver\Assets\catsaver.ico
UninstallDisplayIcon={app}\CatSaver.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "開機時自動啟動 CatSaver (Start with Windows)"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\CatSaver.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\CatSaver.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\CatSaver.exe"; Tasks: desktopicon

[Registry]
; Same value the app's tray menu toggles.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CatSaver"; \
  ValueData: """{app}\CatSaver.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\CatSaver.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM CatSaver.exe /F"; Flags: runhidden; RunOnceId: "KillCatSaver"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\CatSaver"
