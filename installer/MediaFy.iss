; Instalador de MediaFy by CG — CG LABS
#define MyAppName "MediaFy"
#define MyAppVersion "2.0.1"
#define MyAppPublisher "CG LABS"
#define MyAppURL "https://github.com/cgus392-cmd"
#define MyAppExeName "MediaFy.exe"
#define SourceDir "C:\Users\camil\Documents\CG LABS Projects\MediaFy\YTDownloader\YTDownloaderWinUI\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"

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
OutputDir=C:\Users\camil\Documents\CG LABS Projects\MediaFy\YTDownloader\installer\dist
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
; Instalación normal (con asistente): checkbox "ejecutar MediaFy" al final.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,MediaFy}"; Flags: nowait postinstall skipifsilent
; Actualización silenciosa (desde el auto-updater): relanza la app automáticamente al terminar,
; como el usuario original (sin heredar elevación si el instalador se elevó).
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifnotsilent runasoriginaluser

[Code]
// Cierra cualquier instancia previa de MediaFy y sus procesos auxiliares antes de copiar
// archivos, para que la actualización no falle con "deshaciendo cambios".
//
// IMPORTANTE: se mata POR NOMBRE (/IM), nunca con /T (árbol). Si usáramos /T sobre
// MediaFy.exe y este instalador fue lanzado como proceso hijo de la app (como hacían
// las versiones <= 1.8.1), el /T mataría también a este instalador → crash de ambos.
// Matando por nombre, el instalador (nombre distinto) nunca cae en la redada.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exes: array[0..3] of String;
  I: Integer;
begin
  Exes[0] := '{#MyAppExeName}';  // MediaFy.exe (bloquea el ejecutable principal)
  Exes[1] := 'yt-dlp.exe';       // auxiliares que pueden bloquear archivos en Assets
  Exes[2] := 'ffmpeg.exe';
  Exes[3] := 'deno.exe';
  for I := 0 to 3 do
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ' + Exes[I], '',
         SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
  Result := '';
end;
