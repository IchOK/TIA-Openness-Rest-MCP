# Updates the TIA Portal Openness firewall whitelist for a rebuilt executable.
# Identity is Path + SHA-256 FileHash + UTC DateModified (not an App-ID / GUID).
#
# IMPORTANT: DateModified must use literal '/' separators (InvariantCulture).
# German locales otherwise produce "yyyy.MM.dd" which TIA Portal rejects.
param(
  [Parameter(Mandatory = $true)]
  [string] $ExePath,

  [string] $TiaVersion = "20.0",

  # When set, do not attempt UAC elevation (used from MSBuild Post-Build).
  [switch] $NoElevate
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-OpennessDateModified {
  param([datetime] $UtcTime)
  # Siemens format: yyyy/MM/dd HH:mm:ss.fff with literal slashes
  return $UtcTime.ToString("yyyy'/'MM'/'dd HH:mm:ss.fff", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Update-OpennessWhitelist {
  param(
    [string] $ExePath,
    [string] $TiaVersion
  )

  if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found: $ExePath"
  }

  $fileInfo = Get-Item -LiteralPath $ExePath
  $sha = [Security.Cryptography.SHA256]::Create()
  try {
    $stream = [IO.File]::OpenRead($fileInfo.FullName)
    try {
      $fileHash = [Convert]::ToBase64String($sha.ComputeHash($stream))
    }
    finally {
      $stream.Dispose()
    }
  }
  finally {
    $sha.Dispose()
  }

  $dateModified = Get-OpennessDateModified -UtcTime $fileInfo.LastWriteTimeUtc
  $exeName = $fileInfo.Name

  # TIA Portal (64-bit) reads the native HKLM\SOFTWARE view.
  # Also write Wow6432Node so 32-bit tools see the same entry.
  $roots = @(
    "HKLM:\SOFTWARE\Siemens\Automation\Openness\$TiaVersion\Whitelist\$exeName",
    "HKLM:\SOFTWARE\WOW6432Node\Siemens\Automation\Openness\$TiaVersion\Whitelist\$exeName"
  )

  foreach ($root in $roots) {
    if (Test-Path -LiteralPath $root) {
      Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -like "Entry*" } |
        ForEach-Object { Remove-Item -LiteralPath $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue }
    }

    $entryKey = Join-Path $root "Entry"
    New-Item -Path $entryKey -Force | Out-Null
    Set-ItemProperty -Path $entryKey -Name "Path" -Value $fileInfo.FullName
    Set-ItemProperty -Path $entryKey -Name "FileHash" -Value $fileHash
    Set-ItemProperty -Path $entryKey -Name "DateModified" -Value $dateModified
  }

  Write-Host "[Openness] Whitelist updated for $exeName (TIA $TiaVersion)"
  Write-Host "[Openness]   Path: $($fileInfo.FullName)"
  Write-Host "[Openness]   DateModified: $dateModified"
  Write-Host "[Openness]   FileHash: $fileHash"
}

try {
  if (-not (Test-IsAdministrator)) {
    if ($NoElevate) {
      Write-Warning "[Openness] Not elevated. Whitelist was NOT updated. Run task 'update-openness-whitelist' (UAC) or rebuild from an elevated shell."
      exit 0
    }

    Write-Host "[Openness] Admin rights required - elevating to update whitelist..."
    $process = Start-Process `
      -FilePath "powershell.exe" `
      -Verb RunAs `
      -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $PSCommandPath,
        "-ExePath", $ExePath,
        "-TiaVersion", $TiaVersion
      ) `
      -Wait `
      -PassThru

    if ($null -eq $process) {
      Write-Warning "[Openness] Elevation was cancelled or failed. Whitelist was NOT updated."
      exit 0
    }
    exit $process.ExitCode
  }

  Update-OpennessWhitelist -ExePath $ExePath -TiaVersion $TiaVersion
}
catch {
  Write-Warning "[Openness] Whitelist update failed: $($_.Exception.Message)"
  if ($NoElevate) {
    exit 0
  }
  exit 1
}
