; Inno Setup 7 script for the wgetui installer (`make installer`, after `make publish`).
;
; Installs the three files of `make publish` (the single-file exe plus the winget COM activation DLL and .winmd,
; which must sit next to it). Per-user only: %LOCALAPPDATA%\Programs\wgetui, no UAC, no "all users" choice
; (the app runs non-elevated anyway, and one scope keeps upgrades and the future self-update simple).
;
; The version is read from publish\wgetui.exe (built from <Version> in Directory.Build.props), so the props
; file stays the only place it is written.

#define AppExeSource "..\publish\wgetui.exe"

#if !FileExists(AppExeSource)
  #error publish\wgetui.exe not found: run `make publish` first (or use `make installer`)
#endif

#define VerMajor
#define VerMinor
#define VerRevision
#define VerBuild
#if GetVersionComponents(AppExeSource, VerMajor, VerMinor, VerRevision, VerBuild) == ""
  #error Could not read the file version of publish\wgetui.exe
#endif
#define AppVersion Str(VerMajor) + "." + Str(VerMinor) + "." + Str(VerRevision)

[Setup]
; Never change AppId: it identifies the installation, so a newer setup upgrades it in place.
AppId={{45F298C5-1686-4641-BB2D-6146D39E7853}
AppName=wgetui
AppVersion={#AppVersion}
AppVerName=wgetui {#AppVersion}
AppPublisher=Jacopo Filonzi
AppPublisherURL=https://github.com/jacopofilonzi/wgetui
AppSupportURL=https://github.com/jacopofilonzi/wgetui
AppUpdatesURL=https://github.com/jacopofilonzi/wgetui
VersionInfoVersion={#AppVersion}

; x64 only, Windows 10 1809 or later (the app's own targets).
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; Per-user only ({autopf} = %LOCALAPPDATA%\Programs): never elevated, no install-mode dialog.
PrivilegesRequired=lowest
DefaultDirName={autopf}\wgetui
DisableProgramGroupPage=yes

; Setup and uninstall stop while wgetui runs. Must match InstallerMutex in src/WgetUi.App (never rename).
AppMutex=wgetui-running

SetupIconFile=..\src\WgetUi.App\Assets\wgetui.ico
UninstallDisplayIcon={app}\wgetui.exe
UninstallDisplayName=wgetui
WizardStyle=modern dynamic

OutputDir=..\artifacts
OutputBaseFilename=wgetui-setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"

[Files]
Source: "..\publish\wgetui.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\Microsoft.Management.Deployment.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\Microsoft.Management.Deployment.winmd"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Same AppUserModelID as the process and the toast registration, so toasts and the taskbar group with it.
Name: "{autoprograms}\wgetui"; Filename: "{app}\wgetui.exe"; AppUserModelID: "wgetui"

[Run]
; runasoriginaluser: winget refuses COM activation from an elevated process; keeps wgetui non-elevated even if
; the setup itself was started from an elevated prompt.
Filename: "{app}\wgetui.exe"; Description: "{cm:LaunchProgram,wgetui}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
// Uninstall cleanup of what the app writes outside its install folder, for the user who installed it
// (the install is per-user). Settings (%APPDATA%\wgetui) and logs
// (%LOCALAPPDATA%\wgetui\logs) are kept on purpose: a reinstall picks them up again.
//
// - HKCU Run value "wgetui" ("Start with Windows"): deleted only when it starts this install's exe, so a copy
//   run from elsewhere (a portable build, a dev build) keeps its own entry.
// - HKCU\Software\Classes\AppUserModelId\wgetui and %LOCALAPPDATA%\wgetui\notification-icon.png: the toast
//   registration, rewritten by the app at every start; useless once it is gone.

const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'wgetui';
  AumidKey = 'Software\Classes\AppUserModelId\wgetui';

procedure RemoveRunValueIfOurs();
var
  Command: String;
begin
  if RegQueryStringValue(HKCU, RunKey, RunValue, Command) then
    if Pos(Lowercase(ExpandConstant('{app}\wgetui.exe')), Lowercase(Command)) > 0 then
      RegDeleteValue(HKCU, RunKey, RunValue);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveRunValueIfOurs();
    RegDeleteKeyIncludingSubkeys(HKCU, AumidKey);
    DeleteFile(ExpandConstant('{localappdata}\wgetui\notification-icon.png'));
  end;
end;
