; Gravitone installer (Inno Setup 6.3 or later).
; Build:  iscc /DAppVersion=0.2.0 /DSourceDir=..\publish installer\Gravitone.iss
; Per-user install: no administrator rights, nothing outside the user's profile (the .NET runtime aside).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#define AppName "Gravitone"
#define AppExe "Gravitone.exe"
#define AppUrl "https://github.com/lupanostefano/Gravitone"

[Setup]
AppId={{6F1C3E2A-9B47-4C1D-A8E5-3D7B2F90C4A1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Stefano Lupano
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=© 2026 Stefano Lupano
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\dist
OutputBaseFilename=Gravitone-{#AppVersion}-setup
SetupIconFile=..\Gravitone.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
WizardSizePercent=110
WizardImageFile=wizard-large-100.bmp,wizard-large-125.bmp,wizard-large-150.bmp,wizard-large-200.bmp,wizard-large-250.bmp
WizardSmallImageFile=wizard-small-100.bmp,wizard-small-125.bmp,wizard-small-150.bmp,wizard-small-200.bmp,wizard-small-250.bmp
WizardImageStretch=no
WizardImageAlphaFormat=defined
CloseApplications=no
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Messages]
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%nGravitone replaces the Windows taskbar with a Dock and a menu bar. The taskbar always comes back: when you quit Gravitone, if it crashes, and when you uninstall it.%n%nTip: Ctrl + Alt + Shift + B shows the Windows taskbar at any time.
italian.WelcomeLabel2=Verrà installato [name/ver] sul computer.%n%nGravitone sostituisce la barra delle applicazioni di Windows con un Dock e una barra dei menu. La barra torna sempre: quando esci da Gravitone, se si chiude per un errore e quando lo disinstalli.%n%nSuggerimento: Ctrl + Alt + Maiusc + B mostra la barra di Windows in qualsiasi momento.

[CustomMessages]
english.AutoStart=Start Gravitone when I sign in to Windows
italian.AutoStart=Avvia Gravitone all'accesso a Windows
english.DesktopIcon=Create a desktop shortcut
italian.DesktopIcon=Crea un collegamento sul desktop
english.Launch=Start Gravitone now
italian.Launch=Avvia Gravitone ora
english.RuntimeTitle=.NET 9 Desktop Runtime
italian.RuntimeTitle=.NET 9 Desktop Runtime
english.RuntimeText=Gravitone needs the Microsoft .NET 9 Desktop Runtime, which is not installed. Setup will download it from Microsoft (about 60 MB) and install it; Windows may ask for permission.
italian.RuntimeText=Gravitone richiede Microsoft .NET 9 Desktop Runtime, che non è installato. L'installazione lo scaricherà da Microsoft (circa 60 MB) e lo installerà; Windows potrebbe chiedere il permesso.
english.RuntimeFailed=The .NET 9 Desktop Runtime could not be installed. You can install it from https://dotnet.microsoft.com/download/dotnet/9.0 and then start Gravitone.
italian.RuntimeFailed=Non è stato possibile installare .NET 9 Desktop Runtime. Puoi installarlo da https://dotnet.microsoft.com/download/dotnet/9.0 e poi avviare Gravitone.
english.Emergency=Restore the Windows taskbar
italian.Emergency=Ripristina la barra di Windows

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\{#AppName} - {cm:Emergency}"; Filename: "{app}\{#AppExe}"; Parameters: "--restore-taskbar"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--set-autostart on"; Tasks: autostart; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Parameters: "--set-autostart off"; Tasks: not autostart; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Quits the dock, gives the taskbar back and removes the start-up entries, before the files go.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "GravitoneUninstall"

[Code]
var
  DownloadPage: TDownloadWizardPage;

// The .NET 9 Desktop Runtime: listed in the registry by its installer, and present as a 9.x folder.
function RuntimeInstalled: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  Found: TFindRec;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '9.' then
      begin
        Result := True;
        Exit;
      end;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\9.*'), Found) then
  begin
    Result := True;
    FindClose(Found);
  end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(CustomMessage('RuntimeTitle'), CustomMessage('RuntimeText'), nil);
end;

// Quit a running copy first (an update): the dock gives the taskbar back and releases its files.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
    Exec(ExpandConstant('{app}\{#AppExe}'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Code: Integer;
begin
  Result := True;
  if (CurPageID = wpReady) and not RuntimeInstalled then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime-win-x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code)
          or ((Code <> 0) and (Code <> 3010)) then
          MsgBox(CustomMessage('RuntimeFailed'), mbError, MB_OK);
      except
        MsgBox(CustomMessage('RuntimeFailed') + #13#10#13#10 + GetExceptionMessage, mbError, MB_OK);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;
