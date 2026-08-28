# Zip the PopTracker pack for release / install.
# Usage: .\tools\Pack-Poptracker.ps1
$ErrorActionPreference = "Stop"
$Repo = Split-Path $PSScriptRoot -Parent
$Src = Join-Path $Repo "poptracker\HSSB"
$Dist = Join-Path $Repo "dist"
$Out = Join-Path $Dist "HardspaceShipbreaker-PopTracker.zip"

if (-not (Test-Path $Src)) {
    Write-Error "Pack not found: $Src (run python tools/generate_poptracker.py first)"
    exit 1
}

New-Item -ItemType Directory -Force -Path $Dist | Out-Null
if (Test-Path $Out) {
    Remove-Item -Force $Out
}

# Zip contents of HSSB so the archive extracts as a single pack folder named HSSB
$staging = Join-Path $env:TEMP "hssb-poptracker-pack"
if (Test-Path $staging) {
    Remove-Item -Recurse -Force $staging
}
New-Item -ItemType Directory -Force -Path (Join-Path $staging "HSSB") | Out-Null
Copy-Item -Recurse -Force (Join-Path $Src "*") (Join-Path $staging "HSSB")
Compress-Archive -Path (Join-Path $staging "HSSB") -DestinationPath $Out -Force
Remove-Item -Recurse -Force $staging

Write-Host "Packed $Out ($((Get-Item $Out).Length) bytes)"
