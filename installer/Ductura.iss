; Inno Setup skripta za Windows instalacijski paket.
; Izrada: iscc /DAppVersion=0.1.0 /DSourceDir=..\publish installer\Ductura.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{7B0E3D52-5C1A-4A55-9C8B-1D3C0D6F2A11}
AppName=Ductura
AppVersion={#AppVersion}
AppPublisher=Ductura
AppPublisherURL=https://ductura.org
DefaultDirName={autopf}\Ductura
DefaultGroupName=Ductura
DisableProgramGroupPage=yes
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=Ductura-Setup-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\Ductura"; Filename: "{app}\Ductura.exe"
Name: "{group}\Deinstaliraj Ducturu"; Filename: "{uninstallexe}"

[Registry]
Root: HKA; Subkey: "Software\Classes\ductura"; ValueType: string; ValueData: "URL:Ductura"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\ductura"; ValueName: "URL Protocol"; ValueType: string; ValueData: ""
Root: HKA; Subkey: "Software\Classes\ductura\shell\open\command"; ValueType: string; ValueData: """{app}\Ductura.exe"" ""%1"""

[Run]
Filename: "{app}\Ductura.exe"; Description: "Pokreni Ducturu"; Flags: nowait postinstall skipifsilent
