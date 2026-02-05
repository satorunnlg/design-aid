; Design Aid Inno Setup Script
; https://jrsoftware.org/isinfo.php

#define MyAppName "Design Aid"
#define MyAppPublisher "satorunnlg"
#define MyAppURL "https://github.com/satorunnlg/design-aid"
#define MyAppExeName "daid.exe"

; Version is passed from command line: iscc /DMyAppVersion=0.1.2 setup.iss
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

[Setup]
AppId={{7A8E9F12-3B4C-5D6E-7F8A-9B0C1D2E3F4A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=DesignAid-Setup-{#MyAppVersion}
; SetupIconFile=..\assets\icon.ico  ; アイコンファイルが存在する場合にコメント解除
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "addtopath"; Description: "PATH 環境変数に追加する"; GroupDescription: "追加オプション:"; Flags: checkedonce

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Registry]
; Add to PATH for all users
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; \
    Tasks: addtopath; Check: NeedsAddPath(ExpandConstant('{app}'))

[Code]
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', OrigPath)
  then begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Param + ';', ';' + OrigPath + ';') = 0;
end;

// 環境変数変更の通知は ChangesEnvironment=yes により Inno Setup が自動で行う
// 追加のコードは不要

[Run]
; コマンドプロンプトを開いてバージョン確認（ウィンドウを開いたままにする）
Filename: "cmd.exe"; Parameters: "/k ""{app}\{#MyAppExeName}"" --version"; \
    Flags: postinstall skipifsilent unchecked; Description: "コマンドプロンプトでバージョンを確認"
