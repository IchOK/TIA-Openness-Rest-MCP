# Builds REST-API and MCP server in Release and packs them into installer\output\TiaOpennessTools_Setup_<Version>.exe.
# Layout after installation: <InstallDir>\TiaREST, <InstallDir>\TiaMCP.
# Release contains no Openness whitelist handling; TIA Portal asks for access on the first attach.
# The MCP server is published self-contained (win-x64), so the target machine needs no .NET runtime for it.
# Requires Inno Setup 6 (winget install JRSoftware.InnoSetup).
param(
  [string] $Version = "1.0.0",
  [string] $IsccPath
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$installerDir = Join-Path $root "installer"
$stage = Join-Path $installerDir "stage"

if (-not $IsccPath) {
  $IsccPath = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
  ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $IsccPath) {
  throw "Inno Setup (ISCC.exe) not found. Install it with 'winget install JRSoftware.InnoSetup' or pass -IsccPath."
}

if (Test-Path -LiteralPath $stage) {
  Remove-Item -LiteralPath $stage -Recurse -Force
}
$restStage = New-Item -ItemType Directory -Path (Join-Path $stage "TiaREST")
$mcpStage = Join-Path $stage "TiaMCP"

Write-Host "== REST-API (Release)"
dotnet msbuild "$root\TiaOpenness_Tool_TiaREST\TiaOpenness_Tool_TiaREST.csproj" /p:Configuration=Release /t:Rebuild /v:m -nologo
if ($LASTEXITCODE -ne 0) { throw "REST build failed." }
Get-ChildItem "$root\TiaOpenness_Tool_TiaREST\bin\Release" -File |
  Where-Object { $_.Extension -in ".exe", ".dll", ".config", ".json" } |
  Copy-Item -Destination $restStage

Write-Host "== MCP-Server (Release, self-contained win-x64)"
dotnet publish "$root\TiaOpenness_Tool_TiaMCP\TiaOpenness_Tool_TiaMCP.csproj" -c Release -r win-x64 --self-contained true -o $mcpStage -nologo -v:m
if ($LASTEXITCODE -ne 0) { throw "MCP publish failed." }
Get-ChildItem $mcpStage -Filter *.pdb -Recurse | Remove-Item

# The installer suggests the values of the shipped appsettings.json and writes the user's choice back into them.
$restDefaults = (Get-Content -LiteralPath (Join-Path $restStage "appsettings.json") -Raw | ConvertFrom-Json).TiaRest
$mcpDefaults = (Get-Content -LiteralPath (Join-Path $mcpStage "appsettings.json") -Raw | ConvertFrom-Json).TiaMcp

Write-Host "== Installer"
& $IsccPath "/DAppVersion=$Version" "/DStageDir=$stage" `
  "/DDefaultMcpPort=$($mcpDefaults.Port)" "/DDefaultRestPort=$($restDefaults.Port)" `
  "/DDefaultSiemensPath=$($restDefaults.SiemensPath)" "/DDefaultTiaVersion=$($restDefaults.TiaVersion)" `
  "$installerDir\TiaOpennessTools.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

Write-Host "Installer: $installerDir\output\TiaOpennessTools_Setup_$Version.exe"
