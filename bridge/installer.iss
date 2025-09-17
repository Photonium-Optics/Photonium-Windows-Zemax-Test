#define AppName "Photonium Zemax Bridge"
#define AppExe "Photonium.Zemax.Bridge.exe"
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
Source: "Photonium.Zemax.Bridge\bin\x64\Release\{#AppExe}"; DestDir: "{#AppDir}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{#AppDir}\{#AppExe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{#AppDir}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{#AppDir}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[Registry]
; Register custom protocol handler: photonium-zemax://
Root: HKCU; Subkey: "Software\Classes\photonium-zemax"; ValueType: string; ValueName: ""; ValueData: "URL:Photonium Zemax Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\photonium-zemax"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\photonium-zemax\DefaultIcon"; ValueType: string; ValueData: """{#AppDir}\{#AppExe}"""
Root: HKCU; Subkey: "Software\Classes\photonium-zemax\shell\open\command"; ValueType: string; ValueData: """{#AppDir}\{#AppExe}"" ""%1"""

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
    
    // Set environment variable for CORS origin (optional - can be configured later)
    if MsgBox('Do you want to restrict the bridge to only work with the official Photonium website?' + #13#10 + 
              'This is more secure but may prevent local testing.', mbConfirmation, MB_YESNO) = IDYES then
    begin
      RegWriteStringValue(HKCU, 'Environment', 'PHOTONIUM_ORIGIN', 'https://photonium-windows-zemax-test.vercel.app');
    end;
  end;
end;

[UninstallRun]
Filename: "netsh"; Parameters: "http delete urlacl url=http://127.0.0.1:8765/"; Flags: runhidden

[Messages]
BeveledLabel=Photonium Optics