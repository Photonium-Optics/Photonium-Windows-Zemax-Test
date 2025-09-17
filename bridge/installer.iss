#define AppName "Photonium Zemax Bridge"
#define AppExe "PhotoniumZemaxBridge.exe"
#define AppVersion "1.0.0"
#define AppPublisher "Photonium Optics"
#define AppDir "{pf}\Photonium\ZemaxBridge"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={#AppDir}
DefaultGroupName={#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=Photonium-Zemax-Bridge-Setup
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
Compression=lzma
SolidCompression=yes
UninstallDisplayName={#AppName}

[Files]
Source: "SimpleBridge\bin\Release\{#AppExe}"; DestDir: "{#AppDir}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{#AppDir}\{#AppExe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{#AppDir}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{#AppDir}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[Registry]
; Autostart entry
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PhotoniumZemaxBridge"; ValueData: """{#AppDir}\{#AppExe}"""; Flags: uninsdeletevalue

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // Reserve URL for HttpListener so normal users can listen on 127.0.0.1:8765
    ShellExec('', 'netsh', 'http delete urlacl url=http://127.0.0.1:8765/', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    ShellExec('', 'netsh', 'http add urlacl url=http://127.0.0.1:8765/ user=Everyone', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

[UninstallRun]
Filename: "netsh"; Parameters: "http delete urlacl url=http://127.0.0.1:8765/"; Flags: runhidden

[Messages]
BeveledLabel=Photonium Optics