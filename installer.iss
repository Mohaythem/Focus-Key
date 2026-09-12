[Setup]
AppId={{A1B2C3D4-FOCUS-KEY1-0000-000000000001}
AppName=Focus Key
AppVersion=1.1.0
VersionInfoVersion=1.1.0.0
AppPublisher=Focus Key
AppSupportURL=https://github.com/Mohaythem/Focus-Key
DefaultDirName={userpf}\Focus Key
DefaultGroupName=Focus Key
DisableProgramGroupPage=yes
OutputBaseFilename=FocusKeySetup
OutputDir=release
SetupIconFile=src\FocusKey.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\FocusKey.exe
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Focus Key"; Filename: "{app}\FocusKey.exe"
Name: "{autoprograms}\Focus Key"; Filename: "{app}\FocusKey.exe"
Name: "{autodesktop}\Focus Key"; Filename: "{app}\FocusKey.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Focus Key"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\FocusKey.exe"; Description: "{cm:LaunchProgram,Focus Key}"; Flags: nowait postinstall skipifsilent
