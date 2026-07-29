; NetSearch universal installer (Inno Setup 6.3+). Built by publish.ps1.
#define AppName "NetSearch"
#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif
#define AppPublisher "denfry"
#define AppExe "NetSearch.exe"

[Setup]
AppId={{8E6F2C2A-7B4D-4C1E-9E2A-NETSEARCH0002}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=NetSearch-Setup-{#AppVersion}
SetupIconFile=..\NetSearch.ico
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x86compatible
ArchitecturesInstallIn64BitMode=x64os or arm64
MinVersion=10.0.17763
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"

[Files]
Source: "..\publish\win-arm64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: IsArm64
Source: "..\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: IsX64OS
Source: "..\publish\win-x86\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: not IsArm64 and not IsX64OS

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent
