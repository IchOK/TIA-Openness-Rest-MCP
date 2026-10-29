; Installer für TIA Openness Tools (REST-API + MCP-Server).
; Wird von scripts\Build-Installer.ps1 gebaut. StageDir enthält die Ordner TiaREST und TiaMCP;
; die Default*-Werte liest das Build-Skript aus den mitgelieferten appsettings.json.
; Während der Installation werden API-Key, Ports, Siemens-Pfad und TIA-Version abgefragt
; und in TiaREST\appsettings.json und TiaMCP\appsettings.json geschrieben.

#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#ifndef StageDir
  #define StageDir "stage"
#endif
#if !Defined(DefaultMcpPort) || !Defined(DefaultRestPort) || !Defined(DefaultSiemensPath) || !Defined(DefaultTiaVersion)
  #error Default values missing: build the installer with scripts\Build-Installer.ps1
#endif
#define RestExe "TiaOpenness_Tool_TiaREST.exe"
#define McpExe "TiaOpenness_Tool_TiaMCP.exe"

[Setup]
AppId={{90272404-1F55-4F0B-8022-FFF6AA5E0627}
AppName=TIA Openness Tools
AppVersion={#AppVersion}
AppPublisher=Tophinke
DefaultDirName={autopf}\TiaOpennessTools
DefaultGroupName=TIA Openness Tools
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=TiaOpennessTools_Setup_{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\TiaMCP\{#McpExe}
CloseApplications=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

; Zusätzliche Tools: Git / Python / VS Code. Silent: /GIT=1 /PYTHON=1 /VSCODE=1
[Tasks]
Name: "autostart"; Description: "{cm:TaskAutostart}"; Flags: unchecked

[CustomMessages]
; --- German ---
german.TaskAutostart=MCP-Server bei der Anmeldung automatisch starten
german.DepsPageCaption=Zusätzliche Tools
german.DepsPageDescription=Git, Python und VS Code für den TIA-PLC-Workspace-Skill
german.DepsPageInfo=Der MCP-Skill „tia-plc-workspace“ (Projekt abbilden / Sync / Merge) braucht auf diesem Rechner zusätzlich:
german.DepsGitTitle=Git über winget installieren
german.DepsGitBody=Sync: lokale Dateiänderungen erkennen (Tag „tia/last-export“, git status / diff). Paket Git.Git → PATH.
german.DepsPythonTitle=Python über winget installieren
german.DepsPythonBody=Skill-Hilfsskripte (update_catalog, compare_sync, merge_element, …). Paket Python.PythonInstallManager + aktuelle Runtime („py install default“).
german.DepsVsCodeTitle=VS Code über winget installieren
german.DepsVsCodeBody=3-Wege-Merge bei Sync-Konflikten (Choice Merge): code --merge. OpenCode hat keinen Merge-Editor. Paket Microsoft.VisualStudioCode → PATH („code“).
german.DepsNote=Bereits vorhandene Programme bleiben unverändert (Vorab-Prüfung). Nach Installation ggf. neues Terminal für PATH. Ohne winget: Optionen deaktiviert. Silent: /GIT=1 /PYTHON=1 /VSCODE=1.
german.DepsGitAlready=bereits verfügbar — Installation nicht nötig
german.DepsPythonAlready=bereits verfügbar — Installation nicht nötig
german.DepsVsCodeAlready=bereits verfügbar — Installation nicht nötig
german.DepsWingetMissing=winget wurde nicht gefunden. Git/Python/VS Code bitte manuell installieren oder den App Installer (Microsoft Store) aktualisieren.
german.DepsInstallingGit=Git wird über winget installiert …
german.DepsInstallingPython=Python Install Manager wird über winget installiert …
german.DepsInstallingPythonRuntime=Aktuelle Python-Runtime wird installiert (py install default) …
german.DepsInstallingVsCode=VS Code wird über winget installiert …
german.DepsGitFailed=Git-Installation über winget ist fehlgeschlagen (Exit-Code %1). Manuell: winget install -e --id Git.Git
german.DepsPythonFailed=Python-Installation über winget ist fehlgeschlagen (Exit-Code %1). Manuell: winget install -e --id Python.PythonInstallManager
german.DepsPythonRuntimeFailed=Python-Runtime (py install default) ist fehlgeschlagen (Exit-Code %1). Manuell: py install default
german.DepsVsCodeFailed=VS-Code-Installation über winget ist fehlgeschlagen (Exit-Code %1). Manuell: winget install -e --id Microsoft.VisualStudioCode
; --- English ---
english.TaskAutostart=Start the MCP server automatically at logon
english.DepsPageCaption=Additional tools
english.DepsPageDescription=Git, Python, and VS Code for the TIA PLC workspace skill
english.DepsPageInfo=The MCP skill “tia-plc-workspace” (project mirror / sync / merge) also needs on this machine:
english.DepsGitTitle=Install Git via winget
english.DepsGitBody=Sync: detect local file changes (tag “tia/last-export”, git status / diff). Package Git.Git → PATH.
english.DepsPythonTitle=Install Python via winget
english.DepsPythonBody=Skill helper scripts (update_catalog, compare_sync, merge_element, …). Package Python.PythonInstallManager + current runtime (“py install default”).
english.DepsVsCodeTitle=Install VS Code via winget
english.DepsVsCodeBody=3-way merge on sync conflicts (choice Merge): code --merge. OpenCode has no merge editor. Package Microsoft.VisualStudioCode → PATH (“code”).
english.DepsNote=Already installed programs are left unchanged (pre-check). After install, open a new terminal for PATH. Without winget: options disabled. Silent: /GIT=1 /PYTHON=1 /VSCODE=1.
english.DepsGitAlready=already available — install not needed
english.DepsPythonAlready=already available — install not needed
english.DepsVsCodeAlready=already available — install not needed
english.DepsWingetMissing=winget was not found. Install Git/Python/VS Code manually or update App Installer (Microsoft Store).
english.DepsInstallingGit=Installing Git via winget …
english.DepsInstallingPython=Installing Python Install Manager via winget …
english.DepsInstallingPythonRuntime=Installing current Python runtime (py install default) …
english.DepsInstallingVsCode=Installing VS Code via winget …
english.DepsGitFailed=Git install via winget failed (exit code %1). Manual: winget install -e --id Git.Git
english.DepsPythonFailed=Python install via winget failed (exit code %1). Manual: winget install -e --id Python.PythonInstallManager
english.DepsPythonRuntimeFailed=Python runtime (py install default) failed (exit code %1). Manual: py install default
english.DepsVsCodeFailed=VS Code install via winget failed (exit code %1). Manual: winget install -e --id Microsoft.VisualStudioCode

[Files]
Source: "{#StageDir}\TiaREST\*"; DestDir: "{app}\TiaREST"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\TiaMCP\*"; DestDir: "{app}\TiaMCP"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\TIA Openness Tools\TIA Openness MCP-Server"; Filename: "{app}\TiaMCP\{#McpExe}"; WorkingDir: "{app}\TiaMCP"
Name: "{autostartup}\TIA Openness MCP-Server"; Filename: "{app}\TiaMCP\{#McpExe}"; WorkingDir: "{app}\TiaMCP"; Tasks: autostart

[Run]
Filename: "{app}\TiaMCP\{#McpExe}"; WorkingDir: "{app}\TiaMCP"; Description: "MCP-Server jetzt starten"; Flags: nowait postinstall skipifsilent unchecked runasoriginaluser

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#McpExe}"; Flags: runhidden; RunOnceId: "StopMcp"
Filename: "taskkill.exe"; Parameters: "/F /IM {#RestExe}"; Flags: runhidden; RunOnceId: "StopRest"

[Code]
type
  TGuidData = record
    A, B, C, D: LongWord;
  end;

function CoCreateGuid(var Guid: TGuidData): Integer; external 'CoCreateGuid@ole32.dll stdcall';

const
  WM_SETTINGCHANGE = $001A;
  SMTO_ABORTIFHUNG = $0002;

function SendMessageTimeout(Wnd: HWND; Msg, wParam: LongWord; lParam: String;
  Flags, Timeout: LongWord; var dwResult: DWORD): DWORD;
  external 'SendMessageTimeoutW@user32.dll stdcall';

var
  ConfigPage: TInputQueryWizardPage;
  SiemensPage: TInputDirWizardPage;
  VersionPage: TInputOptionWizardPage;
  DepsPage: TWizardPage;
  DepsGitCheck: TNewCheckBox;
  DepsPythonCheck: TNewCheckBox;
  DepsVsCodeCheck: TNewCheckBox;
  DepsInfoLabel: TNewStaticText;
  DepsGitBodyLabel: TNewStaticText;
  DepsPythonBodyLabel: TNewStaticText;
  DepsVsCodeBodyLabel: TNewStaticText;
  DepsNoteLabel: TNewStaticText;
  Versions: TStringList;
  PreferredVersion: String;
  GitAlreadyInstalled: Boolean;
  PythonAlreadyInstalled: Boolean;
  VsCodeAlreadyInstalled: Boolean;
  WingetAvailableFlag: Boolean;

{ API-Key aus zwei zufälligen GUIDs (Windows-Kryptozufall), 64 Hex-Zeichen }
function NewApiKey(): String;
var
  Guid: TGuidData;
  I: Integer;
begin
  Result := '';
  for I := 1 to 2 do begin
    if CoCreateGuid(Guid) <> 0 then
      RaiseException('CoCreateGuid failed');
    Result := Result + Format('%.8x%.8x%.8x%.8x', [Guid.A, Guid.B, Guid.C, Guid.D]);
  end;
  Result := Lowercase(Result);
end;

{ --- Minimaler JSON-Zugriff auf "Key": Wert; die Schlüssel sind in den appsettings.json eindeutig. --- }

function JsonFindValue(const S, Key: String; var ValueStart, ValueEnd: Integer; var Quoted: Boolean): Boolean;
var
  P, I, L: Integer;
begin
  Result := False;
  P := Pos('"' + Key + '"', S);
  if P = 0 then
    Exit;
  L := Length(S);
  I := P + Length(Key) + 2;
  while (I <= L) and (S[I] <> ':') do
    I := I + 1;
  I := I + 1;
  while (I <= L) and ((S[I] = ' ') or (S[I] = #9)) do
    I := I + 1;
  if I > L then
    Exit;
  Quoted := S[I] = '"';
  if Quoted then begin
    I := I + 1;
    ValueStart := I;
    while (I <= L) and (S[I] <> '"') do begin
      if S[I] = '\' then
        I := I + 1;
      I := I + 1;
    end;
    if I > L then
      Exit;
    ValueEnd := I;
  end else begin
    ValueStart := I;
    while (I <= L) and (S[I] <> ',') and (S[I] <> '}') and (S[I] <> #13) and (S[I] <> #10) do
      I := I + 1;
    ValueEnd := I;
    while (ValueEnd > ValueStart) and ((S[ValueEnd - 1] = ' ') or (S[ValueEnd - 1] = #9)) do
      ValueEnd := ValueEnd - 1;
  end;
  Result := True;
end;

{ Nicht-ASCII-Zeichen werden als \uXXXX geschrieben, damit die Datei unabhängig von der Codepage gültiges UTF-8 bleibt. }
function JsonEscape(const Value: String): String;
var
  I: Integer;
  C: Char;
begin
  Result := '';
  for I := 1 to Length(Value) do begin
    C := Value[I];
    if (C = '\') or (C = '"') then
      Result := Result + '\' + C
    else if (Ord(C) < 32) or (Ord(C) > 126) then
      Result := Result + '\u' + Format('%.4x', [Ord(C)])
    else
      Result := Result + C;
  end;
end;

function JsonUnescape(const Value: String): String;
var
  I: Integer;
begin
  Result := '';
  I := 1;
  while I <= Length(Value) do begin
    if (Value[I] = '\') and (I < Length(Value)) then begin
      I := I + 1;
      if (Value[I] = 'u') and (I + 4 <= Length(Value)) then begin
        Result := Result + Chr(StrToInt('$' + Copy(Value, I + 1, 4)));
        I := I + 4;
      end else
        Result := Result + Value[I];
    end else
      Result := Result + Value[I];
    I := I + 1;
  end;
end;

function LoadJson(const FileName: String; var S: String): Boolean;
var
  Raw: AnsiString;
begin
  Result := LoadStringFromFile(FileName, Raw);
  if Result then
    S := String(Raw);
end;

function ReadJsonSetting(const FileName, Key, Default: String): String;
var
  S: String;
  ValueStart, ValueEnd: Integer;
  Quoted: Boolean;
begin
  Result := Default;
  if LoadJson(FileName, S) and JsonFindValue(S, Key, ValueStart, ValueEnd, Quoted) then begin
    if Quoted then
      Result := JsonUnescape(Copy(S, ValueStart, ValueEnd - ValueStart))
    else
      Result := Copy(S, ValueStart, ValueEnd - ValueStart);
  end;
end;

{ Ersetzt den Wert; ein Wert in Anführungszeichen bleibt ein String, sonst wird er unverändert (Zahl) geschrieben. }
procedure SetJsonValue(var S: String; const FileName, Key, Value: String);
var
  ValueStart, ValueEnd: Integer;
  Quoted: Boolean;
  NewValue: String;
begin
  if not JsonFindValue(S, Key, ValueStart, ValueEnd, Quoted) then
    RaiseException('Setting ' + Key + ' not found in ' + FileName);
  if Quoted then
    NewValue := JsonEscape(Value)
  else
    NewValue := Value;
  S := Copy(S, 1, ValueStart - 1) + NewValue + Copy(S, ValueEnd, Length(S));
end;

procedure SaveJson(const FileName, S: String);
begin
  if not SaveStringToFile(FileName, AnsiString(S), False) then
    RaiseException('Cannot write ' + FileName);
end;

{ --- TIA-Portal-Versionen: <Siemens-Pfad>\Portal V<Version>\PublicAPI\V<Version>\Siemens.Engineering.dll --- }

procedure FindVersions(const BasePath: String; List: TStringList);
var
  FindRec: TFindRec;
  Version: String;
begin
  List.Clear;
  if FindFirst(AddBackslash(BasePath) + 'Portal V*', FindRec) then begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then begin
          Version := Copy(FindRec.Name, 9, Length(FindRec.Name));
          if FileExists(AddBackslash(BasePath) + FindRec.Name + '\PublicAPI\V' + Version + '\Siemens.Engineering.dll') then
            List.Add(Version);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
  List.Sort;
end;

function SelectedVersion(): String;
begin
  if Versions.Count = 0 then
    FindVersions(SiemensPage.Values[0], Versions);
  if Versions.IndexOf(PreferredVersion) >= 0 then
    Result := PreferredVersion
  else if Versions.Count > 0 then
    Result := Versions[Versions.Count - 1]
  else
    Result := PreferredVersion;
end;

function IsValidPort(const Value: String): Boolean;
var
  Port: Integer;
begin
  Port := StrToIntDef(Trim(Value), 0);
  Result := (Port >= 1) and (Port <= 65535) and (IntToStr(Port) = Trim(Value));
end;

{ --- Git / Python / winget --- }

function CommandSucceeds(const Exe, Params: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function DetectWinget(): Boolean;
begin
  { App Installer legt winget oft unter LocalAppData ab; PATH der Setup-Session kann veraltet sein. }
  Result := FileExists(ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe'))
    or CommandSucceeds('winget', '--version')
    or CommandSucceeds(ExpandConstant('{cmd}'), '/C winget --version >nul 2>&1');
end;

function CmdLineFlag(const Name: String): Boolean;
begin
  Result := ExpandConstant('{param:' + Name + '|0}') = '1';
end;

function DetectGit(): Boolean;
begin
  Result := CommandSucceeds('git', '--version')
    or FileExists(ExpandConstant('{pf}\Git\cmd\git.exe'))
    or FileExists(ExpandConstant('{pf32}\Git\cmd\git.exe'));
end;

function DetectPython(): Boolean;
begin
  { Nutzbare Runtime — nicht nur der Install Manager ohne Interpreter. }
  Result := CommandSucceeds('python', '--version')
    or CommandSucceeds('py', '-3 --version')
    or CommandSucceeds('py', '--version')
    or DirExists(ExpandConstant('{localappdata}\Programs\Python'));
end;

function DetectVsCode(): Boolean;
begin
  Result := CommandSucceeds('code', '--version')
    or FileExists(ExpandConstant('{localappdata}\Programs\Microsoft VS Code\Code.exe'))
    or FileExists(ExpandConstant('{pf}\Microsoft VS Code\Code.exe'))
    or FileExists(ExpandConstant('{localappdata}\Programs\Microsoft VS Code\bin\code.cmd'))
    or FileExists(ExpandConstant('{pf}\Microsoft VS Code\bin\code.cmd'));
end;

function WingetExe(): String;
var
  Candidate: String;
begin
  Candidate := ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe');
  if FileExists(Candidate) then begin
    Result := Candidate;
    Exit;
  end;
  Result := 'winget';
end;

function InstallWithWinget(const PackageId, StatusMsg: String; var ResultCode: Integer): Boolean;
var
  Params: String;
begin
  WizardForm.StatusLabel.Caption := StatusMsg;
  WizardForm.StatusLabel.Update;
  { --scope machine: systemweiter PATH (Setup läuft als Admin). }
  Params := 'install --id ' + PackageId +
    ' -e --scope machine --accept-source-agreements --accept-package-agreements --disable-interactivity';
  Result := Exec(WingetExe(), Params, '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  if (not Result) or (ResultCode <> 0) then begin
    { Fallback ohne machine-scope (z. B. wenn Paket nur user-scope anbietet) }
    Params := 'install --id ' + PackageId +
      ' -e --accept-source-agreements --accept-package-agreements --disable-interactivity';
    Result := Exec(WingetExe(), Params, '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;
  Result := Result and (ResultCode = 0);
end;

procedure BroadcastPathChange();
var
  Dummy: DWORD;
begin
  { HWND_BROADCAST + WM_SETTINGCHANGE("Environment") — neue Prozesse sehen PATH. }
  SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, 'Environment', SMTO_ABORTIFHUNG, 5000, Dummy);
end;

function InstallPythonRuntime(var ResultCode: Integer): Boolean;
begin
  WizardForm.StatusLabel.Caption := ExpandConstant('{cm:DepsInstallingPythonRuntime}');
  WizardForm.StatusLabel.Update;
  BroadcastPathChange();
  Result := Exec('py', 'install default', '', SW_SHOW, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0);
  if not Result then
    Result := Exec('pymanager', 'install default', '', SW_SHOW, ewWaitUntilTerminated, ResultCode)
      and (ResultCode = 0);
  if not Result then
    Result := Exec(ExpandConstant('{cmd}'), '/C py install default', '', SW_SHOW, ewWaitUntilTerminated, ResultCode)
      and (ResultCode = 0);
end;

procedure CreateDepsPage();
var
  TopPos: Integer;
begin
  DepsPage := CreateCustomPage(VersionPage.ID, ExpandConstant('{cm:DepsPageCaption}'),
    ExpandConstant('{cm:DepsPageDescription}'));

  DepsInfoLabel := TNewStaticText.Create(DepsPage);
  DepsInfoLabel.Parent := DepsPage.Surface;
  DepsInfoLabel.Left := ScaleX(0);
  DepsInfoLabel.Top := ScaleY(0);
  DepsInfoLabel.Width := DepsPage.SurfaceWidth;
  DepsInfoLabel.AutoSize := True;
  DepsInfoLabel.WordWrap := True;
  DepsInfoLabel.Caption := ExpandConstant('{cm:DepsPageInfo}');

  TopPos := DepsInfoLabel.Top + DepsInfoLabel.Height + ScaleY(12);

  DepsGitCheck := TNewCheckBox.Create(DepsPage);
  DepsGitCheck.Parent := DepsPage.Surface;
  DepsGitCheck.Left := ScaleX(0);
  DepsGitCheck.Top := TopPos;
  DepsGitCheck.Width := DepsPage.SurfaceWidth;
  DepsGitCheck.Caption := ExpandConstant('{cm:DepsGitTitle}');

  DepsGitBodyLabel := TNewStaticText.Create(DepsPage);
  DepsGitBodyLabel.Parent := DepsPage.Surface;
  DepsGitBodyLabel.Left := ScaleX(20);
  DepsGitBodyLabel.Top := DepsGitCheck.Top + DepsGitCheck.Height + ScaleY(2);
  DepsGitBodyLabel.Width := DepsPage.SurfaceWidth - ScaleX(20);
  DepsGitBodyLabel.AutoSize := True;
  DepsGitBodyLabel.WordWrap := True;
  DepsGitBodyLabel.Caption := ExpandConstant('{cm:DepsGitBody}');

  TopPos := DepsGitBodyLabel.Top + DepsGitBodyLabel.Height + ScaleY(14);

  DepsPythonCheck := TNewCheckBox.Create(DepsPage);
  DepsPythonCheck.Parent := DepsPage.Surface;
  DepsPythonCheck.Left := ScaleX(0);
  DepsPythonCheck.Top := TopPos;
  DepsPythonCheck.Width := DepsPage.SurfaceWidth;
  DepsPythonCheck.Caption := ExpandConstant('{cm:DepsPythonTitle}');

  DepsPythonBodyLabel := TNewStaticText.Create(DepsPage);
  DepsPythonBodyLabel.Parent := DepsPage.Surface;
  DepsPythonBodyLabel.Left := ScaleX(20);
  DepsPythonBodyLabel.Top := DepsPythonCheck.Top + DepsPythonCheck.Height + ScaleY(2);
  DepsPythonBodyLabel.Width := DepsPage.SurfaceWidth - ScaleX(20);
  DepsPythonBodyLabel.AutoSize := True;
  DepsPythonBodyLabel.WordWrap := True;
  DepsPythonBodyLabel.Caption := ExpandConstant('{cm:DepsPythonBody}');

  TopPos := DepsPythonBodyLabel.Top + DepsPythonBodyLabel.Height + ScaleY(10);

  DepsVsCodeCheck := TNewCheckBox.Create(DepsPage);
  DepsVsCodeCheck.Parent := DepsPage.Surface;
  DepsVsCodeCheck.Left := ScaleX(0);
  DepsVsCodeCheck.Top := TopPos;
  DepsVsCodeCheck.Width := DepsPage.SurfaceWidth;
  DepsVsCodeCheck.Caption := ExpandConstant('{cm:DepsVsCodeTitle}');

  DepsVsCodeBodyLabel := TNewStaticText.Create(DepsPage);
  DepsVsCodeBodyLabel.Parent := DepsPage.Surface;
  DepsVsCodeBodyLabel.Left := ScaleX(20);
  DepsVsCodeBodyLabel.Top := DepsVsCodeCheck.Top + DepsVsCodeCheck.Height + ScaleY(2);
  DepsVsCodeBodyLabel.Width := DepsPage.SurfaceWidth - ScaleX(20);
  DepsVsCodeBodyLabel.AutoSize := True;
  DepsVsCodeBodyLabel.WordWrap := True;
  DepsVsCodeBodyLabel.Caption := ExpandConstant('{cm:DepsVsCodeBody}');

  DepsNoteLabel := TNewStaticText.Create(DepsPage);
  DepsNoteLabel.Parent := DepsPage.Surface;
  DepsNoteLabel.Left := ScaleX(0);
  DepsNoteLabel.Top := DepsVsCodeBodyLabel.Top + DepsVsCodeBodyLabel.Height + ScaleY(10);
  DepsNoteLabel.Width := DepsPage.SurfaceWidth;
  DepsNoteLabel.AutoSize := True;
  DepsNoteLabel.WordWrap := True;
  DepsNoteLabel.Caption := ExpandConstant('{cm:DepsNote}');

  WingetAvailableFlag := DetectWinget();
  GitAlreadyInstalled := DetectGit();
  PythonAlreadyInstalled := DetectPython();
  VsCodeAlreadyInstalled := DetectVsCode();

  if not WingetAvailableFlag then begin
    DepsGitCheck.Enabled := False;
    DepsPythonCheck.Enabled := False;
    DepsVsCodeCheck.Enabled := False;
    DepsGitCheck.Checked := False;
    DepsPythonCheck.Checked := False;
    DepsVsCodeCheck.Checked := False;
    DepsNoteLabel.Caption := ExpandConstant('{cm:DepsWingetMissing}');
  end else begin
    if GitAlreadyInstalled then begin
      DepsGitCheck.Checked := False;
      DepsGitCheck.Enabled := False;
      DepsGitCheck.Caption := ExpandConstant('{cm:DepsGitTitle}') + ' — ' + ExpandConstant('{cm:DepsGitAlready}');
    end else if WizardSilent then
      DepsGitCheck.Checked := CmdLineFlag('GIT')
    else
      DepsGitCheck.Checked := True;

    if PythonAlreadyInstalled then begin
      DepsPythonCheck.Checked := False;
      DepsPythonCheck.Enabled := False;
      DepsPythonCheck.Caption := ExpandConstant('{cm:DepsPythonTitle}') + ' — ' + ExpandConstant('{cm:DepsPythonAlready}');
    end else if WizardSilent then
      DepsPythonCheck.Checked := CmdLineFlag('PYTHON')
    else
      DepsPythonCheck.Checked := True;

    if VsCodeAlreadyInstalled then begin
      DepsVsCodeCheck.Checked := False;
      DepsVsCodeCheck.Enabled := False;
      DepsVsCodeCheck.Caption := ExpandConstant('{cm:DepsVsCodeTitle}') + ' — ' + ExpandConstant('{cm:DepsVsCodeAlready}');
    end else if WizardSilent then
      DepsVsCodeCheck.Checked := CmdLineFlag('VSCODE')
    else
      DepsVsCodeCheck.Checked := True;
  end;
end;

{ --- Assistent --- }

procedure InitializeWizard();
var
  ApiKey, McpPort, RestPort, SiemensPath, RestFile, McpFile: String;
begin
  Versions := TStringList.Create;

  ApiKey := NewApiKey();
  McpPort := '{#DefaultMcpPort}';
  RestPort := '{#DefaultRestPort}';
  SiemensPath := '{#DefaultSiemensPath}';
  PreferredVersion := '{#DefaultTiaVersion}';

  { Bei einer Aktualisierung die bisherige Konfiguration vorschlagen }
  if WizardForm.PrevAppDir <> '' then begin
    RestFile := AddBackslash(WizardForm.PrevAppDir) + 'TiaREST\appsettings.json';
    McpFile := AddBackslash(WizardForm.PrevAppDir) + 'TiaMCP\appsettings.json';
    ApiKey := ReadJsonSetting(McpFile, 'ApiKey', ApiKey);
    McpPort := ReadJsonSetting(McpFile, 'Port', McpPort);
    RestPort := ReadJsonSetting(RestFile, 'Port', RestPort);
    SiemensPath := ReadJsonSetting(RestFile, 'SiemensPath', SiemensPath);
    PreferredVersion := ReadJsonSetting(RestFile, 'TiaVersion', PreferredVersion);
  end;

  ConfigPage := CreateInputQueryPage(wpSelectDir, 'Konfiguration', 'API-Key und Ports',
    'Der API-Key schützt REST-API und MCP-Server. MCP-Clients (z. B. Cursor) senden ihn im Header X-API-Key. ' +
    'Der vorgeschlagene Key ist zufällig erzeugt.');
  ConfigPage.Add('API-Key:', False);
  ConfigPage.Add('Port des MCP-Servers:', False);
  ConfigPage.Add('Port der REST-API:', False);
  ConfigPage.Values[0] := ApiKey;
  ConfigPage.Values[1] := McpPort;
  ConfigPage.Values[2] := RestPort;

  SiemensPage := CreateInputDirPage(ConfigPage.ID, 'TIA Portal', 'Siemens-Installationsordner',
    'Ordner, in dem TIA Portal installiert ist. Er enthält je Version einen Unterordner "Portal V<Version>" mit der Openness-API (PublicAPI).',
    False, '');
  SiemensPage.Add('Siemens-Ordner:');
  SiemensPage.Values[0] := SiemensPath;

  VersionPage := CreateInputOptionPage(SiemensPage.ID, 'TIA Portal', 'TIA-Portal-Version',
    'Gefundene TIA-Portal-Versionen mit Openness-API. Die REST-API verwendet die ausgewählte Version:', True, False);

  CreateDepsPage();
end;

procedure DeinitializeSetup();
begin
  Versions.Free;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  I, Selected: Integer;
begin
  if CurPageID = VersionPage.ID then begin
    VersionPage.CheckListBox.Items.Clear;
    Selected := Versions.Count - 1;
    for I := 0 to Versions.Count - 1 do begin
      VersionPage.Add('TIA Portal V' + Versions[I]);
      if Versions[I] = PreferredVersion then
        Selected := I;
    end;
    VersionPage.SelectedValueIndex := Selected;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = ConfigPage.ID then begin
    if Trim(ConfigPage.Values[0]) = '' then begin
      MsgBox('Bitte einen API-Key angeben.', mbError, MB_OK);
      Result := False;
    end else if (not IsValidPort(ConfigPage.Values[1])) or (not IsValidPort(ConfigPage.Values[2])) then begin
      MsgBox('Die Ports müssen Zahlen zwischen 1 und 65535 sein.', mbError, MB_OK);
      Result := False;
    end else if Trim(ConfigPage.Values[1]) = Trim(ConfigPage.Values[2]) then begin
      MsgBox('MCP-Server und REST-API brauchen verschiedene Ports.', mbError, MB_OK);
      Result := False;
    end;
  end else if CurPageID = SiemensPage.ID then begin
    FindVersions(SiemensPage.Values[0], Versions);
    if Versions.Count = 0 then begin
      MsgBox('In "' + SiemensPage.Values[0] + '" wurde keine TIA-Portal-Installation mit Openness-API gefunden ' +
        '(erwartet: Portal V<Version>\PublicAPI\V<Version>\Siemens.Engineering.dll).', mbError, MB_OK);
      Result := False;
    end;
  end else if CurPageID = VersionPage.ID then begin
    if VersionPage.SelectedValueIndex < 0 then begin
      MsgBox('Bitte eine TIA-Portal-Version auswählen.', mbError, MB_OK);
      Result := False;
    end else
      PreferredVersion := Versions[VersionPage.SelectedValueIndex];
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine +
    'Konfiguration:' + NewLine +
    Space + 'API-Key: ' + Trim(ConfigPage.Values[0]) + NewLine +
    Space + 'Port MCP-Server: ' + Trim(ConfigPage.Values[1]) + NewLine +
    Space + 'Port REST-API: ' + Trim(ConfigPage.Values[2]) + NewLine +
    Space + 'Siemens-Ordner: ' + SiemensPage.Values[0] + NewLine +
    Space + 'TIA-Portal-Version: V' + SelectedVersion() + NewLine + NewLine +
    ExpandConstant('{cm:DepsPageCaption}') + ':' + NewLine +
    Space + 'Git (winget): ';
  if DepsGitCheck.Checked and DepsGitCheck.Enabled then
    Result := Result + 'ja'
  else if GitAlreadyInstalled then
    Result := Result + 'bereits vorhanden'
  else
    Result := Result + 'nein';
  Result := Result + NewLine + Space + 'Python (winget): ';
  if DepsPythonCheck.Checked and DepsPythonCheck.Enabled then
    Result := Result + 'ja'
  else if PythonAlreadyInstalled then
    Result := Result + 'bereits vorhanden'
  else
    Result := Result + 'nein';
  Result := Result + NewLine + Space + 'VS Code (winget): ';
  if DepsVsCodeCheck.Checked and DepsVsCodeCheck.Enabled then
    Result := Result + 'ja'
  else if VsCodeAlreadyInstalled then
    Result := Result + 'bereits vorhanden'
  else
    Result := Result + 'nein';
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

function ShouldInstallGit(): Boolean;
begin
  Result := Assigned(DepsGitCheck) and DepsGitCheck.Checked and DepsGitCheck.Enabled and WingetAvailableFlag;
end;

function ShouldInstallPython(): Boolean;
begin
  Result := Assigned(DepsPythonCheck) and DepsPythonCheck.Checked and DepsPythonCheck.Enabled and WingetAvailableFlag;
end;

function ShouldInstallVsCode(): Boolean;
begin
  Result := Assigned(DepsVsCodeCheck) and DepsVsCodeCheck.Checked and DepsVsCodeCheck.Enabled and WingetAvailableFlag;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  FileName, S, ApiKey, McpPort, RestPort: String;
  ResultCode: Integer;
  InstalledSomething: Boolean;
begin
  if CurStep <> ssPostInstall then
    Exit;

  ApiKey := Trim(ConfigPage.Values[0]);
  McpPort := Trim(ConfigPage.Values[1]);
  RestPort := Trim(ConfigPage.Values[2]);

  FileName := ExpandConstant('{app}\TiaREST\appsettings.json');
  if not LoadJson(FileName, S) then
    RaiseException('Cannot read ' + FileName);
  SetJsonValue(S, FileName, 'ApiKey', ApiKey);
  SetJsonValue(S, FileName, 'Port', RestPort);
  SetJsonValue(S, FileName, 'TiaVersion', SelectedVersion());
  SetJsonValue(S, FileName, 'SiemensPath', SiemensPage.Values[0]);
  SaveJson(FileName, S);

  FileName := ExpandConstant('{app}\TiaMCP\appsettings.json');
  if not LoadJson(FileName, S) then
    RaiseException('Cannot read ' + FileName);
  SetJsonValue(S, FileName, 'ApiKey', ApiKey);
  SetJsonValue(S, FileName, 'Port', McpPort);
  SetJsonValue(S, FileName, 'RestPort', RestPort);
  SaveJson(FileName, S);

  InstalledSomething := False;

  if ShouldInstallGit() then begin
    if not InstallWithWinget('Git.Git', ExpandConstant('{cm:DepsInstallingGit}'), ResultCode) then
      MsgBox(FmtMessage(ExpandConstant('{cm:DepsGitFailed}'), [IntToStr(ResultCode)]), mbError, MB_OK)
    else
      InstalledSomething := True;
  end;

  if ShouldInstallPython() then begin
    if not InstallWithWinget('Python.PythonInstallManager', ExpandConstant('{cm:DepsInstallingPython}'), ResultCode) then
      MsgBox(FmtMessage(ExpandConstant('{cm:DepsPythonFailed}'), [IntToStr(ResultCode)]), mbError, MB_OK)
    else begin
      InstalledSomething := True;
      if not InstallPythonRuntime(ResultCode) then
        MsgBox(FmtMessage(ExpandConstant('{cm:DepsPythonRuntimeFailed}'), [IntToStr(ResultCode)]), mbError, MB_OK);
    end;
  end;

  if ShouldInstallVsCode() then begin
    if not InstallWithWinget('Microsoft.VisualStudioCode', ExpandConstant('{cm:DepsInstallingVsCode}'), ResultCode) then
      MsgBox(FmtMessage(ExpandConstant('{cm:DepsVsCodeFailed}'), [IntToStr(ResultCode)]), mbError, MB_OK)
    else
      InstalledSomething := True;
  end;

  if InstalledSomething then
    BroadcastPathChange();
end;
