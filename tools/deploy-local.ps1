# Local Deployment Script for CadJsonExtractor (Multi-Version AutoCAD Support)
$ErrorActionPreference = 'Stop'

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Starting CadJsonExtractor Local Deployment" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$root = Join-Path $PSScriptRoot ".."
$sourceRoot = Join-Path $root "deploy\CadJsonExtractor-1.0.0"
if (-not (Test-Path $sourceRoot)) {
    throw "Source deploy directory not found: $sourceRoot"
}

# 1. Build Multi-Target Solution in Release mode (.NET 8 + .NET 48)
Write-Host "`n[1/4] Building CadJsonExtractor for net8.0-windows and net48..." -ForegroundColor Yellow
dotnet build (Join-Path $root "src\CadJsonExtractor.AutoCAD\CadJsonExtractor.AutoCAD.csproj") -c Release

# 2. Stage binaries into bundle directory
Write-Host "`n[2/4] Staging multi-target bundle artifacts..." -ForegroundColor Yellow
$bundleContents = Join-Path $sourceRoot "CadJsonExtractor.bundle\Contents"
$net8Dest = Join-Path $bundleContents "net8.0"
$net48Dest = Join-Path $bundleContents "net48"

New-Item -ItemType Directory -Path $net8Dest -Force | Out-Null
New-Item -ItemType Directory -Path $net48Dest -Force | Out-Null

# Clean legacy flat files in Contents root if present
Get-ChildItem -Path $bundleContents -File | Remove-Item -Force -ErrorAction SilentlyContinue

$net8Src = Join-Path $root "src\CadJsonExtractor.AutoCAD\bin\Release\net8.0-windows"
$net48Src = Join-Path $root "src\CadJsonExtractor.AutoCAD\bin\Release\net48"

# Copy net8.0 (AutoCAD 2025+)
Copy-Item -Path (Join-Path $net8Src "CadJsonExtractor.AutoCAD.dll") -Destination (Join-Path $net8Dest "CadJsonExtractor.AutoCAD.dll") -Force
Copy-Item -Path (Join-Path $net8Src "CadJsonExtractor.AutoCAD.pdb") -Destination (Join-Path $net8Dest "CadJsonExtractor.AutoCAD.pdb") -Force
Copy-Item -Path (Join-Path $net8Src "CadJsonExtractor.Core.dll") -Destination (Join-Path $net8Dest "CadJsonExtractor.Core.dll") -Force
Copy-Item -Path (Join-Path $net8Src "CadJsonExtractor.Core.pdb") -Destination (Join-Path $net8Dest "CadJsonExtractor.Core.pdb") -Force
if (Test-Path (Join-Path $net8Src "CadJsonExtractor.AutoCAD.deps.json")) {
    Copy-Item -Path (Join-Path $net8Src "CadJsonExtractor.AutoCAD.deps.json") -Destination (Join-Path $net8Dest "CadJsonExtractor.AutoCAD.deps.json") -Force
}

# Copy net48 (AutoCAD 2021-2024)
Get-ChildItem -Path $net48Src -File | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $net48Dest $_.Name) -Force
}

function Deploy-FileSmart {
    param (
        [string]$SourceFile,
        [string]$TargetFile
    )
    $targetDir = Split-Path -Path $TargetFile -Parent
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    if (Test-Path $TargetFile) {
        try {
            Copy-Item -Path $SourceFile -Destination $TargetFile -Force -ErrorAction Stop
            Write-Host "  -> Updated: $TargetFile" -ForegroundColor Green
        }
        catch {
            $oldPath = "$TargetFile.old." + (Get-Date -Format "yyyyMMdd-HHmmss")
            try {
                Move-Item -Path $TargetFile -Destination $oldPath -Force -ErrorAction Stop
                Copy-Item -Path $SourceFile -Destination $TargetFile -Force -ErrorAction Stop
                Write-Host "  -> Replaced in-use file: $TargetFile (previous version renamed to .old)" -ForegroundColor Green
            }
            catch {
                Write-Host "  -> [ERROR] Failed to replace: $TargetFile ($($_))" -ForegroundColor Red
            }
        }
    }
    else {
        Copy-Item -Path $SourceFile -Destination $TargetFile -Force
        Write-Host "  -> Installed: $TargetFile" -ForegroundColor Green
    }
}

function Deploy-FolderSmart {
    param (
        [string]$SourceDir,
        [string]$TargetDir
    )
    $SourceDir = (Get-Item -LiteralPath $SourceDir).FullName.TrimEnd('\', '/')
    if (-not (Test-Path $TargetDir)) {
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    }
    $allFiles = Get-ChildItem -Path $SourceDir -Recurse -File
    foreach ($file in $allFiles) {
        $relPath = $file.FullName.Substring($SourceDir.Length).TrimStart('\', '/')
        $destPath = Join-Path $TargetDir $relPath
        Deploy-FileSmart -SourceFile $file.FullName -TargetFile $destPath
    }
}

# 3. Deploy to AutoCAD ApplicationPlugins
Write-Host "`n[3/4] Deploying to AutoCAD ApplicationPlugins..." -ForegroundColor Yellow
$acadPluginsDir = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$acadTargetBundle = Join-Path $acadPluginsDir "CadJsonExtractor.bundle"
$acadSourceBundle = Join-Path $sourceRoot "CadJsonExtractor.bundle"

# Clean legacy flat files in target bundle if present
if (Test-Path (Join-Path $acadTargetBundle "Contents\CadJsonExtractor.AutoCAD.dll")) {
    Get-ChildItem -Path (Join-Path $acadTargetBundle "Contents") -File | Remove-Item -Force -ErrorAction SilentlyContinue
}

Deploy-FolderSmart -SourceDir $acadSourceBundle -TargetDir $acadTargetBundle

# 4. Unblock Files
Write-Host "`n[4/4] Unblocking deployed files..." -ForegroundColor Yellow
Get-ChildItem -Path $acadTargetBundle -Recurse -File -ErrorAction SilentlyContinue | Unblock-File
Write-Host "  -> All files unblocked successfully." -ForegroundColor Green

Write-Host "`n=========================================" -ForegroundColor Cyan
Write-Host "Deployment Completed Successfully!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
