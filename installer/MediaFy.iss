; Instalador de MediaFy by CG — CG LABS
#define MyAppName "MediaFy"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "CG LABS"
#define MyAppURL "https://github.com/cgus392-cmd"
#define MyAppExeName "MediaFy.exe"
#define SourceDir "C:\Users\camil\Documents\New project\YTDownloader\YTDownloaderWinUI\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"

[Setup]
AppId={{8F3A2C10-7B4E-4D9A-9C2E-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\MediaFy
DefaultGroupName=MediaFy
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=C:\Users\camil\Documents\New project\YTDownloader\installer\dist
OutputBaseFilename=MediaFy-Setup-{#MyAppVersion}
SetupIconFile={#SourceDir}\Assets\logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\MediaFy"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,MediaFy}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MediaFy"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,MediaFy}"; Flags: nowait postinstall skipifsilent
