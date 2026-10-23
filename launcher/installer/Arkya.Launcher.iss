#ifndef PayloadDirectory
  #error PayloadDirectory is required.
#endif
#ifndef LauncherVersion
  #error LauncherVersion is required.
#endif

[Setup]
AppId={{9E8A9F67-445E-4876-B986-1C6D1E4ED1A0}
AppName=Arkya Launcher
AppVersion={#LauncherVersion}
AppPublisher=Arkya
AppPublisherURL=https://arkya.online/
AppSupportURL=https://arkya.online/
AppUpdatesURL=https://arkya.online/play.html
DefaultDirName={localappdata}\Programs\Arkya Launcher
DisableDirPage=no
DefaultGroupName=Arkya
DisableProgramGroupPage=no
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern dynamic
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=Arkya-Launcher-Setup
SetupIconFile=..\src\Arkya.Launcher\Assets\Arkya.ico
UninstallDisplayName=Arkya Launcher
UninstallDisplayIcon={app}\Arkya.Launcher.exe
AppMutex=Local\Arkya.Launcher
CloseApplications=no
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Arkya Launcher"; Filename: "{app}\Arkya.Launcher.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Arkya Launcher"; Filename: "{app}\Arkya.Launcher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Arkya.Launcher.exe"; Description: "{cm:LaunchProgram,Arkya Launcher}"; Flags: nowait postinstall skipifsilent unchecked

; Only the files above belong to the launcher. Game installs and preferences
; are intentionally absent from uninstall deletion rules.
