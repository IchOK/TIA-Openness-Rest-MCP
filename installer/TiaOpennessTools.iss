; Installer für TIA Openness Tools (REST-API + MCP-Server).
; Wird von scripts\Build-Installer.ps1 gebaut. StageDir enthält die Ordner TiaREST und TiaMCP;
; die Default*-Werte liest das Build-Skript aus den mitgelieferten appsettings.json.
; Während der Installation werden API-Key, Ports, Siemens-Pfad und TIA-Version abgefragt
; und in TiaREST\appsettings.json und TiaMCP\appsettings.json geschrieben.

#ifndef AppVersion
  #define AppVersion "1.0.0"
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

[Tasks]
Name: "autostart"; Description: "MCP-Server bei der Anmeldung automatisch starten"; Flags: unchecked

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

var
  ConfigPage: TInputQueryWizardPage;
  SiemensPage: TInputDirWizardPage;
  VersionPage: TInputOptionWizardPage;
  Versions: TStringList;
  PreferredVersion: String;

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
    Space + 'TIA-Portal-Version: V' + SelectedVersion();
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  FileName, S, ApiKey, McpPort, RestPort: String;
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
end;
