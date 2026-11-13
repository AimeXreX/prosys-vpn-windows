; ProSyS VPN for Windows: Inno Setup 6 script.
; Built by prosys/package-windows.sh (or manually):
;   ISCC.exe /DAppVersion=1.0.1 /DArch=x64 /DSourceDir=<publish dir> /DOutputDir=<dist> ProSySVPN.iss
;
; Per-user install: the app keeps its config, logs and cores next to the exe, so it lives in a folder the user
; can write to. Since 1.2.0 ProSySVPN.exe itself always runs as Administrator (app.manifest requireAdministrator),
; so TUN mode and the system proxy work reliably. Because of that:
;   - the app is launched with ShellExecute (UAC prompt), never CreateProcess (error 740);
;   - start-with-Windows is a "highest privileges" scheduled task created by the app, not a HKCU Run value
;     (Windows silently skips elevated programs listed in Run);
;   - this non-elevated installer cannot close an elevated copy, so the app's own --prosys-stop and
;     --prosys-uninstall switches do that (one UAC prompt).
; The free-service device token is stored in %LOCALAPPDATA%\ProSySVPN and survives uninstall/reinstall on purpose.

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
; No ArchitecturesAllowed and a low MinVersion on purpose: Inno's own checks only say "This program does not
; support the version of Windows your computer is running" (what users saw when they ran the arm64 Setup on a
; normal x64 PC). InitializeSetup below explains what is wrong and offers the right download instead.
#if Arch == "arm64"
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=6.1

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
; 1.1.0 used a HKCU Run value; Windows never starts an elevated app from there, so remove it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "ProSySVPN"; Flags: deletevalue dontcreatekey

[INI]
; Start with Windows: the elevated app turns this into a scheduled task on its next start, then deletes the file.
Filename: "{app}\guiConfigs\prosys-setup.ini"; Section: "setup"; Key: "autorun"; String: "1"; Tasks: autostart
Filename: "{app}\guiConfigs\prosys-setup.ini"; Section: "setup"; Key: "autorun"; String: "0"; Tasks: not autostart

[Run]
; shellexec: the exe requires Administrator, so CreateProcess would fail with error 740.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; The elevated app stops itself and its cores and removes its start-with-Windows task (one UAC prompt).
Filename: "{app}\{#AppExe}"; Parameters: "--prosys-uninstall"; Flags: shellexec waituntilterminated skipifdoesntexist; RunOnceId: "AppCleanup"
; Fallback for copies that were not elevated, and their cores.
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
const
  ReleasesUrl = 'https://github.com/AimeXreX/prosys-vpn-windows/releases/latest';

function IsArm64Setup: Boolean;
begin
#if Arch == "arm64"
  Result := True;
#else
  Result := False;
#endif
end;

// Bilingual on purpose: this can run before the user has picked a language.
function InitializeSetup: Boolean;
var
  Ver: TWindowsVersion;
  Msg: String;
  ErrorCode: Integer;
begin
  Result := True;
  GetWindowsVersionEx(Ver);
  Msg := '';

  // .NET 10 (the app runtime) needs Windows 10 version 1607 (build 14393) or newer.
  if (Ver.Major < 10) or ((Ver.Major = 10) and (Ver.Build < 14393)) then
    Msg := 'ProSyS VPN به ویندوز ۱۰ نسخهٔ 1607 یا جدیدتر (یا ویندوز ۱۱) نیاز دارد.' + #13#10 +
           'ProSyS VPN needs Windows 10 version 1607 or newer, or Windows 11.'
  else if IsArm64Setup and not IsArm64 then
    Msg := 'این فایل نصب مخصوص کامپیوترهای ARM (مثل Snapdragon) است و روی این کامپیوتر نصب نمی‌شود.' + #13#10 +
           'لطفاً فایل ProSySVPN-{#AppVersion}-windows-x64-setup.exe را دانلود کنید.' + #13#10#13#10 +
           'This installer is for ARM64 PCs only. Please download ProSySVPN-{#AppVersion}-windows-x64-setup.exe.'
  else if (not IsArm64Setup) and (not IsX64Compatible) then
    Msg := 'ProSyS VPN فقط روی ویندوز ۶۴ بیتی اجرا می‌شود.' + #13#10 +
           'ProSyS VPN needs 64-bit Windows.';

  if Msg <> '' then
  begin
    if IsArm64Setup and (not IsArm64) and (not WizardSilent) then
    begin
      if MsgBox(Msg + #13#10#13#10 + 'صفحهٔ دانلود باز شود؟ / Open the download page?', mbError, MB_YESNO) = IDYES then
        ShellExec('open', ReleasesUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end
    else
      SuppressibleMsgBox(Msg, mbCriticalError, MB_OK, IDOK);
    Result := False;
  end;
end;

function IsAppRunning: Boolean;
var
  ResultCode: Integer;
  Output: TExecOutput;
  I: Integer;
begin
  Result := False;
  if ExecAndCaptureOutput(ExpandConstant('{sys}\tasklist.exe'), '/NH /FO CSV /FI "IMAGENAME eq {#AppExe}"', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode, Output) then
    for I := 0 to GetArrayLength(Output.StdOut) - 1 do
      if Pos(Lowercase('{#AppExe}'), Lowercase(Output.StdOut[I])) > 0 then
        Result := True;
end;

// Close a running copy before its files are replaced. 1.1.0 ran unelevated (taskkill works); 1.2.0+ runs
// elevated, so the installed app is asked to stop itself, which brings one UAC prompt.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exe: String;
begin
  Result := '';
  if not IsAppRunning then
    Exit;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800);
  Exe := ExpandConstant('{app}\{#AppExe}');
  if IsAppRunning and FileExists(Exe) then
  begin
    ShellExec('', Exe, '--prosys-stop', ExtractFileDir(Exe), SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(800);
  end;
end;

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
