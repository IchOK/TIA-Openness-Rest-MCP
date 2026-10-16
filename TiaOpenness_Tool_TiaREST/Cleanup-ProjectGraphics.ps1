#Requires -Version 5.1
<#
.SYNOPSIS
  Räumt doppelte Projektgrafiken (Name / Name_1 / Name_2) im aktuell geöffneten TIA-Projekt auf.

.DESCRIPTION
  Nutzt die lokale TiaREST-API (appsettings.json neben diesem Skript).
  Ablauf: offenes Projekt ermitteln → optional Dry-Run → Verweise in Unified-Bildern umbiegen → Duplikate löschen.
  Änderungen werden nicht automatisch gespeichert (außer -Save).

  Voraussetzungen: TIA Portal mit geöffnetem Projekt, TiaREST erreichbar (z. B. über den MCP-Server gestartet).

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

  [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
  $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

function Read-RestSettings {
  $settingsPath = Join-Path $scriptDir 'appsettings.json'
  if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "appsettings.json nicht gefunden: $settingsPath"
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
    BaseUrl = "http://${hostName}:${port}"
    ApiKey  = $apiKey
    Headers = @{ 'X-API-Key' = $apiKey }
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

Write-Host "TiaREST-Einstellungen aus $scriptDir\appsettings.json"
$rest = Read-RestSettings
Write-Host "REST: $($rest.BaseUrl)"

try {
  $null = Invoke-RestJson -Method GET -Url "$($rest.BaseUrl)/api/health" -Headers $rest.Headers -TimeoutSec 10
} catch {
  throw @"
TiaREST ist nicht erreichbar ($($rest.BaseUrl)).
Bitte zuerst den MCP-Server (oder TiaOpenness_Tool_TiaREST.exe) starten und TIA Portal mit offenem Projekt bereithalten.
$($_.Exception.Message)
"@
}

$projects = Get-OpenProjects -Rest $rest
if ($projects.Count -eq 0) {
  throw 'Kein geöffnetes TIA-Projekt gefunden.'
}

$selected = $projects
if ($ProcessId -gt 0) {
  $selected = @($selected | Where-Object { [int]$_.ProcessId -eq $ProcessId })
}
if (-not [string]::IsNullOrWhiteSpace($ProjectName)) {
  $selected = @($selected | Where-Object { $_.Name -eq $ProjectName })
}

if ($selected.Count -eq 0) {
  throw "Kein Projekt passt zu den Filterkriterien. Verfügbar:`n$(($projects | ForEach-Object { "  $($_.ProcessId)  $($_.Name)  $($_.Path)" }) -join "`n")"
}
if ($selected.Count -gt 1 -and $ProcessId -le 0 -and [string]::IsNullOrWhiteSpace($ProjectName)) {
  throw @"
Mehrere geöffnete Projekte gefunden. Bitte -ProjectName und/oder -ProcessId angeben:

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

$mode = if ($DryRun) { 'Dry-Run (keine Änderungen)' } else { 'Aufräumen (Verweise ersetzen + Duplikate löschen)' }
Write-Host "Modus: $mode"
Write-Host "CompareContent=$CompareContent  CompileAfter=$CompileAfter"
if ($NamePattern) { Write-Host "NamePattern=$NamePattern" }

$confirmTarget = "$($project.Name) Projektgrafiken"
if (-not $DryRun -and -not $PSCmdlet.ShouldProcess($confirmTarget, 'Doppelte Projektgrafiken aufräumen')) {
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
  Write-Host "Hinweis: Projekt wurde nicht gespeichert. In TIA speichern oder Skript mit -Save erneut ausführen."
}

exit 0
