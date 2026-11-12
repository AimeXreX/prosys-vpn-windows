; ProSyS VPN for Windows: Inno Setup 6 script.
; Built by prosys/package-windows.sh (or manually):
;   ISCC.exe /DAppVersion=1.0.1 /DArch=x64 /DSourceDir=<publish dir> /DOutputDir=<dist> ProSySVPN.iss
;
; Per-user install (no administrator prompt): the app keeps its config, logs and cores next to the exe,
; so it must live in a folder the user can write to. The free-service device token is stored in
; %LOCALAPPDATA%\ProSySVPN and survives uninstall/reinstall on purpose.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #error SourceDir must point at the published ProSyS VPN folder
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define AppName "ProSyS VPN"
#define AppExe "ProSySVPN.exe"
#define AppPublisher "ProSyS"
#define AppUrl "https://vpn.prosysvpn.top/"

[Setup]
AppId={{6E7C2B1A-4F3D-4C5B-9A8E-2D1F0B7C9E41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL=https://t.me/ProSySsellbot
AppUpdatesURL=https://github.com/AimeXreX/prosys-vpn-windows/releases
DefaultDirName={localappdata}\Programs\ProSySVPN
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=ProSySVPN-{#AppVersion}-windows-{#Arch}-setup
SetupIconFile=..\..\v2rayN\v2rayN\Resources\v2rayN.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
LicenseFile=..\..\LICENSE
CloseApplications=force
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0

[Languages]
Name: "fa"; MessagesFile: "Farsi.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
fa.DesktopIcon=ساخت میانبر روی دسکتاپ
en.DesktopIcon=Create a desktop shortcut
fa.AutoStart=اجرای خودکار هنگام ورود به ویندوز
en.AutoStart=Start with Windows
fa.LaunchApp=اجرای ProSyS VPN
en.LaunchApp=Launch ProSyS VPN
fa.Shortcuts=میانبرها:
en.Shortcuts=Shortcuts:

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Shortcuts}"
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked

[Files]
; Never overwrite the user's own settings, logs or server database on upgrade.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "guiConfigs\*,guiLogs\*,guiTemps\*,binConfigs\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ProSySVPN"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the app and its cores first, otherwise their files stay locked.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM xray.exe /FI ""MODULES eq wintun.dll"""; Flags: runhidden; RunOnceId: "KillXray"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\guiLogs"
Type: filesandordirs; Name: "{app}\guiTemps"
Type: filesandordirs; Name: "{app}\binConfigs"
Type: filesandordirs; Name: "{app}\guiConfigs"
; Rule sets and geo files the app downloads at runtime.
Type: filesandordirs; Name: "{app}\bin"
Type: dirifempty; Name: "{app}"

[Code]
// Clear the Windows system proxy on uninstall when it still points at this app's local port,
// so removing ProSyS never leaves the PC without internet.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Server: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Internet Settings', 'ProxyServer', Server) then
      if Pos('127.0.0.1:208', Server) > 0 then
        RegWriteDWordValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Internet Settings', 'ProxyEnable', 0);
  end;
end;
