; Instalador de DeskIsleBar (Inno Setup 6). Se arma con Instalador\armar.ps1, que primero publica la app.
; Se instala para el usuario actual (sin permisos de administrador) en %LOCALAPPDATA%\Programs\DeskIsleBar,
; aparece en Configuracion > Aplicaciones instaladas y al desinstalar ofrece borrar los datos del usuario.

#ifndef Version
  #define Version "1.0.0"
#endif
#define Publicado "..\bin\publicar"

[Setup]
; AppId identifica la app entre versiones: NO cambiarlo, o una version nueva no reemplaza a la anterior
AppId={{4D728B0F-6322-43B0-AF50-CE1E8AB909CB}
AppName=DeskIsleBar
AppVersion={#Version}
AppVerName=DeskIsleBar {#Version}
AppPublisher=FacuGhub
AppPublisherURL=https://github.com/FacuGhub/DeskIsleBar
AppSupportURL=https://github.com/FacuGhub/DeskIsleBar/issues
AppUpdatesURL=https://github.com/FacuGhub/DeskIsleBar/releases
DefaultDirName={autopf}\DeskIsleBar
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\bin\instalador
OutputBaseFilename=DeskIsleBar-Setup-{#Version}
SetupIconFile=..\Recursos\DeskIsleBar.ico
UninstallDisplayIcon={app}\DeskIsleBar.exe
UninstallDisplayName=DeskIsleBar
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Si la isla esta abierta (su mutex es Local\DeskIsleBar) hay que cerrarla antes de reemplazar archivos
AppMutex=DeskIsleBar
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
spanish.Arranque=Arrancar con Windows
english.Arranque=Start with Windows
spanish.Abrir=Abrir DeskIsleBar
english.Abrir=Open DeskIsleBar
spanish.BorrarDatos=¿Borrar también tu configuración, notas y calendarios vinculados?%n%nSi pensás volver a instalar DeskIsleBar, elegí No y se van a conservar.
english.BorrarDatos=Also delete your settings, notes and linked calendars?%n%nIf you plan to install DeskIsleBar again, choose No and they will be kept.

[Tasks]
Name: "arranque"; Description: "{cm:Arranque}"
Name: "escritorio"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Publicado}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\DeskIsleBar"; Filename: "{app}\DeskIsleBar.exe"
Name: "{autodesktop}\DeskIsleBar"; Filename: "{app}\DeskIsleBar.exe"; Tasks: escritorio

[Registry]
; La misma entrada que escribe la app desde Opciones ("Arrancar con Windows"). Al desinstalar se borra en [Code],
; y solo si apunta a esta copia: si apunta a otra (ej. una version portable o de desarrollo) se deja como esta
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeskIsleBar"; ValueData: """{app}\DeskIsleBar.exe"""; Tasks: arranque

[Run]
Filename: "{app}\DeskIsleBar.exe"; Description: "{cm:Abrir}"; Flags: nowait postinstall skipifsilent

[Code]
const
  ClaveRun = 'Software\Microsoft\Windows\CurrentVersion\Run';

// "Arrancar con Windows": se borra solo si apunta a esta instalacion (la pudo prender el instalador o la app)
procedure BorrarArranqueSiEsNuestro;
var
  Valor: String;
begin
  if RegQueryStringValue(HKCU, ClaveRun, 'DeskIsleBar', Valor) and
     (Pos(Lowercase(ExpandConstant('{app}\DeskIsleBar.exe')), Lowercase(Valor)) > 0) then
    RegDeleteValue(HKCU, ClaveRun, 'DeskIsleBar');
end;

// Al terminar de desinstalar: ofrecer borrar %LOCALAPPDATA%\DeskIsleBar (config, notas, calendarios cifrados, log)
procedure CurUninstallStepChanged(Paso: TUninstallStep);
var
  Datos: String;
begin
  if Paso = usUninstall then BorrarArranqueSiEsNuestro;
  if Paso <> usPostUninstall then Exit;
  Datos := ExpandConstant('{localappdata}\DeskIsleBar');
  if not DirExists(Datos) then Exit;
  if UninstallSilent then Exit;   // desinstalacion silenciosa: no se pregunta y los datos quedan
  if MsgBox(CustomMessage('BorrarDatos'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(Datos, True, True, True);
end;
