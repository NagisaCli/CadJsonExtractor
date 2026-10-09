# Local Deployment Script for CadJsonExtractor
$ErrorActionPreference = 'Stop'

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Starting CadJsonExtractor Local Deployment" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$root = Join-Path $PSScriptRoot ".."
$sourceRoot = Join-Path $root "deploy\CadJsonExtractor-1.0.0"
if (-not (Test-Path $sourceRoot)) {
    throw "Source deploy directory not found: $sourceRoot"
}

# 1. Build Solution in Release mode
Write-Host "`n[1/4] Building CadJsonExtractor (Release)..." -ForegroundColor Yellow
$acadBuildBin = Join-Path $root "src\CadJsonExtractor.AutoCAD\bin\Release\net8.0-windows"
$coreBuildBin = Join-Path $root "src\CadJsonExtractor.Core\bin\Release\net8.0"

dotnet build (Join-Path $root "src\CadJsonExtractor.AutoCAD\CadJsonExtractor.AutoCAD.csproj") -c Release

# 2. Stage binaries into bundle directory
Write-Host "`n[2/4] Staging bundle artifacts..." -ForegroundColor Yellow
$bundleContents = Join-Path $sourceRoot "CadJsonExtractor.bundle\Contents"
if (-not (Test-Path $bundleContents)) {
    New-Item -ItemType Directory -Path $bundleContents -Force | Out-Null
}

Copy-Item -Path (Join-Path $acadBuildBin "CadJsonExtractor.AutoCAD.dll") -Destination (Join-Path $bundleContents "CadJsonExtractor.AutoCAD.dll") -Force
Copy-Item -Path (Join-Path $acadBuildBin "CadJsonExtractor.AutoCAD.pdb") -Destination (Join-Path $bundleContents "CadJsonExtractor.AutoCAD.pdb") -Force
Copy-Item -Path (Join-Path $acadBuildBin "CadJsonExtractor.Core.dll") -Destination (Join-Path $bundleContents "CadJsonExtractor.Core.dll") -Force
Copy-Item -Path (Join-Path $acadBuildBin "CadJsonExtractor.Core.pdb") -Destination (Join-Path $bundleContents "CadJsonExtractor.Core.pdb") -Force
if (Test-Path (Join-Path $acadBuildBin "CadJsonExtractor.AutoCAD.deps.json")) {
    Copy-Item -Path (Join-Path $acadBuildBin "CadJsonExtractor.AutoCAD.deps.json") -Destination (Join-Path $bundleContents "CadJsonExtractor.AutoCAD.deps.json") -Force
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
Write-Host "`n[3/4] Deploying to AutoCAD 2025 ApplicationPlugins..." -ForegroundColor Yellow
$acadPluginsDir = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$acadTargetBundle = Join-Path $acadPluginsDir "CadJsonExtractor.bundle"
$acadSourceBundle = Join-Path $sourceRoot "CadJsonExtractor.bundle"

Deploy-FolderSmart -SourceDir $acadSourceBundle -TargetDir $acadTargetBundle

# 4. Unblock Files
Write-Host "`n[4/4] Unblocking deployed files..." -ForegroundColor Yellow
Get-ChildItem -Path $acadTargetBundle -Recurse -File -ErrorAction SilentlyContinue | Unblock-File
Write-Host "  -> All files unblocked successfully." -ForegroundColor Green

Write-Host "`n=========================================" -ForegroundColor Cyan
Write-Host "Deployment Completed Successfully!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
