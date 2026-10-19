#Requires -Version 5.1
<#
.SYNOPSIS
  Räumt doppelte Projektgrafiken (Name / Name_1 / Name_2) im aktuell geöffneten TIA-Projekt auf.

.DESCRIPTION
  Nutzt die lokale TiaREST-API. Nach der Installation liegen Skript, CMD, appsettings.json und
  TiaOpenness_Tool_TiaREST.exe gemeinsam unter <InstallDir>\TiaREST\ (nicht unter bin\Debug).
  Läuft TiaREST nicht, wird die EXE aus diesem Installationsordner (bzw. Dev-Build) gestartet
  und am Ende wieder beendet – nur wenn dieses Skript sie gestartet hat.
  Ablauf: offenes Projekt ermitteln → optional Dry-Run → Verweise umbiegen → Duplikate löschen.
  Änderungen werden nicht automatisch gespeichert (außer -Save).

  Voraussetzungen: TIA Portal mit geöffnetem Projekt.

.EXAMPLE
  .\Cleanup-ProjectGraphics.cmd -DryRun
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Cleanup-ProjectGraphics.ps1
.EXAMPLE
  .\Cleanup-ProjectGraphics.ps1 -NamePattern "Icon_*" -Save
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
  [switch] $DryRun,

  [bool] $CompareContent = $true,

  [switch] $CompileAfter,

  [string] $NamePattern,

  [string] $DeviceName,

  [string] $DeviceItemName,

  [string] $ProjectName,

  [int] $ProcessId,

  [switch] $Save,

  [int] $TimeoutSec = 14400,

  [int] $RestStartTimeoutSec = 30,

  [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
  $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$script:RestStartedByScript = $false
$script:RestProcess = $null

function Get-RestSettingsDirectory {
  # Nach Installation: EXE + appsettings.json unter <InstallDir>\TiaREST\
  try {
    return (Split-Path -Parent (Resolve-TiaRestExe))
  } catch {
    return $scriptDir
  }
}

function Read-RestSettings {
  $settingsDir = Get-RestSettingsDirectory
  $settingsPath = Join-Path $settingsDir 'appsettings.json'
  if (-not (Test-Path -LiteralPath $settingsPath)) {
    $fallback = Join-Path $scriptDir 'appsettings.json'
    if (Test-Path -LiteralPath $fallback) {
      $settingsPath = $fallback
      $settingsDir = $scriptDir
    } else {
      throw "appsettings.json nicht gefunden (erwartet neben der REST-EXE unter <InstallDir>\TiaREST\): $settingsPath"
    }
  }
  $tiaRest = (Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json).TiaRest
  if (-not $tiaRest) {
    throw "Abschnitt TiaRest fehlt in $settingsPath"
  }
  $hostName = if ([string]::IsNullOrWhiteSpace($tiaRest.Host)) { 'localhost' } else { $tiaRest.Host }
  if ($hostName -eq '0.0.0.0') { $hostName = 'localhost' }
  $port = [int]$tiaRest.Port
  if ($port -le 0) { $port = 6280 }
  $apiKey = [string]$tiaRest.ApiKey
  if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw "TiaRest.ApiKey ist in appsettings.json leer."
  }
  [pscustomobject]@{
    BaseUrl     = "http://${hostName}:${port}"
    Port        = $port
    ApiKey      = $apiKey
    Headers     = @{ 'X-API-Key' = $apiKey }
    SettingsDir = $settingsDir
    ExePath     = $(try { Resolve-TiaRestExe } catch { $null })
  }
}

function Test-RestAlive {
  param($Rest)
  try {
    $null = Invoke-RestJson -Method GET -Url "$($Rest.BaseUrl)/api/health" -Headers $Rest.Headers -TimeoutSec 5
    return $true
  } catch {
    return $false
  }
}

function Resolve-TiaRestExe {
  # Entwicklungs-/Repo-Layout: Skript neben der EXE oder im Geschwisterordner TiaREST\
  $installRoot = Split-Path -Parent $scriptDir
  $candidates = [System.Collections.Generic.List[string]]::new()
  foreach ($path in @(
      # 1) gleiche Ordner wie dieses Skript
      (Join-Path $scriptDir 'TiaOpenness_Tool_TiaREST.exe'),
      # 2) Skript im Repo-Root, EXE unter TiaREST\
      (Join-Path $scriptDir 'TiaREST\TiaOpenness_Tool_TiaREST.exe'),
      # 3) Skript z. B. unter TiaMCP, EXE im Geschwisterordner TiaREST\
      (Join-Path $installRoot 'TiaREST\TiaOpenness_Tool_TiaREST.exe'),
      # 4) Entwicklungsbuilds im Repo
      (Join-Path $scriptDir 'bin\Debug\TiaOpenness_Tool_TiaREST.exe'),
      (Join-Path $scriptDir 'bin\Release\TiaOpenness_Tool_TiaREST.exe')
    )) {
    if (-not [string]::IsNullOrWhiteSpace($path)) {
      $full = [IO.Path]::GetFullPath($path)
      if (-not $candidates.Contains($full)) {
        $candidates.Add($full)
      }
    }
  }

  foreach ($path in $candidates) {
    if (Test-Path -LiteralPath $path) {
      return $path
    }
  }
  throw "TiaOpenness_Tool_TiaREST.exe nicht gefunden. Nach Installation erwartet unter <InstallDir>\TiaREST\. Gesucht:`n  - $($candidates -join "`n  - ")"
}

function Start-TiaRestIfNeeded {
  param($Rest, [int] $StartTimeoutSec)

  if (Test-RestAlive -Rest $Rest) {
    Write-Host "TiaREST laeuft bereits."
    return
  }

  $exe = Resolve-TiaRestExe
  # WorkingDirectory = EXE-Ordner, damit die installierte appsettings.json (TiaREST\) geladen wird
  $exeDir = Split-Path -Parent $exe
  Write-Host "TiaREST antwortet nicht - starte installierte/gebaute EXE:"
  Write-Host "  $exe"
  $args = "--apikey `"$($Rest.ApiKey)`" --port $($Rest.Port)"
  $script:RestProcess = Start-Process -FilePath $exe -ArgumentList $args -WorkingDirectory $exeDir -PassThru -WindowStyle Normal
  $script:RestStartedByScript = $true
  Write-Host "TiaREST gestartet (PID $($script:RestProcess.Id)), warte auf Health (max ${StartTimeoutSec}s) ..."

  $deadline = [datetime]::UtcNow.AddSeconds($StartTimeoutSec)
  while ([datetime]::UtcNow -lt $deadline) {
    Start-Sleep -Seconds 1
    if ($script:RestProcess.HasExited) {
      throw "TiaREST-Prozess ist sofort wieder beendet (ExitCode=$($script:RestProcess.ExitCode))."
    }
    if (Test-RestAlive -Rest $Rest) {
      Write-Host "TiaREST ist erreichbar."
      return
    }
  }
  throw "TiaREST wurde gestartet, antwortet aber innerhalb von ${StartTimeoutSec}s nicht auf $($Rest.BaseUrl)/api/health."
}

function Stop-TiaRestIfStarted {
  if (-not $script:RestStartedByScript -or $null -eq $script:RestProcess) {
    return
  }
  try {
    if (-not $script:RestProcess.HasExited) {
      Write-Host "Beende TiaREST (PID $($script:RestProcess.Id)), weil sie von diesem Skript gestartet wurde ..."
      Stop-Process -Id $script:RestProcess.Id -Force -ErrorAction SilentlyContinue
      $null = $script:RestProcess.WaitForExit(10000)
      Write-Host "TiaREST beendet."
    }
  } catch {
    Write-Warning "TiaREST konnte nicht beendet werden: $($_.Exception.Message)"
  } finally {
    $script:RestStartedByScript = $false
    $script:RestProcess = $null
  }
}

function Invoke-RestJson {
  param(
    [string] $Method,
    [string] $Url,
    [hashtable] $Headers,
    [string] $Body = $null,
    [int] $TimeoutSec,
    [switch] $HasBody
  )
  $params = @{
    Uri             = $Url
    Method          = $Method
    Headers         = $Headers
    UseBasicParsing = $true
    TimeoutSec      = $TimeoutSec
  }
  if ($HasBody) {
    $params.Body = [Text.Encoding]::UTF8.GetBytes($(if ($null -eq $Body) { '' } else { $Body }))
    $params.ContentType = 'application/json; charset=utf-8'
  }
  try {
    $response = Invoke-WebRequest @params
  } catch {
    $msg = $_.Exception.Message
    if ($_.Exception.Response) {
      try {
        $stream = $_.Exception.Response.GetResponseStream()
        $reader = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)
        $errBody = $reader.ReadToEnd()
        if (-not [string]::IsNullOrWhiteSpace($errBody)) { $msg = $errBody }
      } catch { }
    }
    throw $msg
  }
  $text = [Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
  if ([string]::IsNullOrWhiteSpace($text)) { return $null }
  if ($response.Headers['Content-Type'] -match 'json' -or $text.TrimStart().StartsWith('{') -or $text.TrimStart().StartsWith('[')) {
    return ($text | ConvertFrom-Json)
  }
  return $text
}

function Get-OpenProjects {
  param($Rest)
  $raw = Invoke-RestJson -Method GET -Url "$($Rest.BaseUrl)/api/projects/list" -Headers $Rest.Headers -TimeoutSec 120
  return @($raw)
}

$rest = Read-RestSettings
Write-Host "TiaREST-Einstellungen: $($rest.SettingsDir)\appsettings.json"
if ($rest.ExePath) { Write-Host "TiaREST-EXE: $($rest.ExePath)" }
Write-Host "REST: $($rest.BaseUrl)"

try {
  Start-TiaRestIfNeeded -Rest $rest -StartTimeoutSec $RestStartTimeoutSec

  $projects = Get-OpenProjects -Rest $rest
  if ($projects.Count -eq 0) {
    throw 'Kein geoeffnetes TIA-Projekt gefunden.'
  }

  $selected = $projects
  if ($ProcessId -gt 0) {
    $selected = @($selected | Where-Object { [int]$_.ProcessId -eq $ProcessId })
  }
  if (-not [string]::IsNullOrWhiteSpace($ProjectName)) {
    $selected = @($selected | Where-Object { $_.Name -eq $ProjectName })
  }

  if ($selected.Count -eq 0) {
    throw "Kein Projekt passt zu den Filterkriterien. Verfuegbar:`n$(($projects | ForEach-Object { "  $($_.ProcessId)  $($_.Name)  $($_.Path)" }) -join "`n")"
  }
  if ($selected.Count -gt 1 -and $ProcessId -le 0 -and [string]::IsNullOrWhiteSpace($ProjectName)) {
    throw @"
Mehrere geoeffnete Projekte gefunden. Bitte -ProjectName und/oder -ProcessId angeben:

$(($selected | ForEach-Object { "  $($_.ProcessId)  $($_.Name)  $($_.Path)" }) -join "`n")
"@
  }

  $project = $selected[0]
  Write-Host "Projekt: $($project.Name) (ProcessId=$($project.ProcessId))"
  Write-Host "Pfad:    $($project.Path)"

  $bodyObj = [ordered]@{
    DryRun         = [bool]$DryRun
    CompareContent = $CompareContent
    CompileAfter   = [bool]$CompileAfter
  }
  if (-not [string]::IsNullOrWhiteSpace($NamePattern)) { $bodyObj.NamePattern = $NamePattern }
  if (-not [string]::IsNullOrWhiteSpace($DeviceName)) { $bodyObj.DeviceName = $DeviceName }
  if (-not [string]::IsNullOrWhiteSpace($DeviceItemName)) { $bodyObj.DeviceItemName = $DeviceItemName }
  $body = ($bodyObj | ConvertTo-Json -Compress)

  $mode = if ($DryRun) { 'Dry-Run (keine Aenderungen)' } else { 'Aufraeumen (Verweise ersetzen + Duplikate loeschen)' }
  Write-Host "Modus: $mode"
  Write-Host "CompareContent=$CompareContent  CompileAfter=$CompileAfter"
  if ($NamePattern) { Write-Host "NamePattern=$NamePattern" }

  $confirmTarget = "$($project.Name) Projektgrafiken"
  if (-not $DryRun -and -not $PSCmdlet.ShouldProcess($confirmTarget, 'Doppelte Projektgrafiken aufraeumen')) {
    Write-Host 'Abgebrochen.'
    exit 0
  }

  $q = "processId=$($project.ProcessId)&projectName=$([Uri]::EscapeDataString($project.Name))"
  $url = "$($rest.BaseUrl)/api/project/graphics/cleanup-duplicates?$q"
  Write-Host "Starte Cleanup ($TimeoutSec s Timeout) ..."
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $result = Invoke-RestJson -Method POST -Url $url -Headers $rest.Headers -Body $body -HasBody -TimeoutSec $TimeoutSec
  $sw.Stop()

  if (-not $ResultPath) {
    $ResultPath = Join-Path $env:TEMP ("graphics-cleanup-{0}-{1:yyyyMMdd-HHmmss}.json" -f $project.Name, (Get-Date))
  }
  ($result | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $ResultPath -Encoding UTF8

  Write-Host ""
  Write-Host ("Fertig in {0:n1} min" -f $sw.Elapsed.TotalMinutes)
  Write-Host "GraphicCount=$($result.GraphicCount) DuplicateCount=$($result.DuplicateCount)"
  Write-Host "Usages=$($result.Usages.Count) DeletedGraphics=$($result.DeletedGraphics.Count)"
  if ($result.Warnings -and $result.Warnings.Count -gt 0) {
    Write-Host "Warnings:"
    $result.Warnings | ForEach-Object { Write-Host "  $_" }
  }
  Write-Host "Ergebnis: $ResultPath"

  if ($Save -and -not $DryRun) {
    Write-Host "Speichere Projekt ..."
    $saveUrl = "$($rest.BaseUrl)/api/projects/save?processId=$($project.ProcessId)&projectName=$([Uri]::EscapeDataString($project.Name))"
    $saveResult = Invoke-RestJson -Method POST -Url $saveUrl -Headers $rest.Headers -Body '{}' -HasBody -TimeoutSec 600
    Write-Host "Gespeichert: $($saveResult | ConvertTo-Json -Compress)"
  } elseif (-not $DryRun) {
    Write-Host "Hinweis: Projekt wurde nicht gespeichert. In TIA speichern oder Skript mit -Save erneut ausfuehren."
  }
} finally {
  Stop-TiaRestIfStarted
}

exit 0
