# Package Release Archive for CadJsonExtractor
$ErrorActionPreference = 'Stop'

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Packaging CadJsonExtractor Release" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$root = Join-Path $PSScriptRoot ".."
$deployScript = Join-Path $PSScriptRoot "deploy-local.ps1"
powershell -ExecutionPolicy Bypass -File $deployScript

$version = "1.0.0"
$releaseDir = Join-Path $root "release"
$stagingDir = Join-Path $releaseDir "CadJsonExtractor-v$version"
$zipFile = Join-Path $releaseDir "CadJsonExtractor-v$version.zip"

if (Test-Path $stagingDir) { Remove-Item -Path $stagingDir -Recurse -Force }
if (Test-Path $zipFile) { Remove-Item -Path $zipFile -Force }

New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# Copy Bundle
$sourceBundle = Join-Path $root "deploy\CadJsonExtractor-$version\CadJsonExtractor.bundle"
Copy-Item -Path $sourceBundle -Destination (Join-Path $stagingDir "CadJsonExtractor.bundle") -Recurse -Force

# Copy Install and Uninstall scripts
Copy-Item -Path (Join-Path $PSScriptRoot "Install.bat") -Destination (Join-Path $stagingDir "Install.bat") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall.bat") -Destination (Join-Path $stagingDir "Uninstall.bat") -Force

# Create README.txt in staging
$readmeContent = @"
========================================================================
CadJsonExtractor - AutoCAD Entity to JSON Extractor (v$version)
========================================================================

Supported AutoCAD Versions:
  - AutoCAD 2025, 2026+ (.NET 8 Windows x64)
  - AutoCAD 2021, 2022, 2023, 2024 (.NET Framework 4.8 x64)
  - AutoCAD 2018 - 2020

QUICK INSTALLATION:
  1. Simply double-click 'Install.bat'.
  2. Launch AutoCAD.
  3. Type 'JS' in the AutoCAD command line to export geometry to JSON.

COMMANDS:
  JS          - Export selected entities to a .json file
  JSC         - Copy selected entities JSON to Windows clipboard
  JSONCLEANTEMP - Clean temporary payload cache

UNINSTALL:
  Double-click 'Uninstall.bat'.

GitHub Repository:
  https://github.com/NagisaCli/CadJsonExtractor
========================================================================
"@

Set-Content -Path (Join-Path $stagingDir "README.txt") -Value $readmeContent -Encoding UTF8

# Create Zip Archive
Write-Host "`nCreating distribution zip: $zipFile..." -ForegroundColor Yellow
Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipFile -Force

Write-Host "Release archive created successfully!" -ForegroundColor Green
Write-Host "Package: $zipFile" -ForegroundColor Cyan
