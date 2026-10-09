@echo off
setlocal enabledelayedexpansion
title CadJsonExtractor Installer

echo ===================================================
echo     CadJsonExtractor - One-Click Installer
echo     Supported AutoCAD: 2021, 2022, 2023, 2024, 2025, 2026+
echo ===================================================
echo.

set "SOURCE_BUNDLE=%~dp0CadJsonExtractor.bundle"

if not exist "!SOURCE_BUNDLE!\PackageContents.xml" (
    set "SOURCE_BUNDLE=%~dp0deploy\CadJsonExtractor-1.0.0\CadJsonExtractor.bundle"
)

if not exist "!SOURCE_BUNDLE!\PackageContents.xml" (
    echo [ERROR] Could not find CadJsonExtractor.bundle directory!
    echo Please make sure this Install.bat is kept in the same folder as CadJsonExtractor.bundle.
    echo.
    pause
    exit /b 1
)

set "TARGET_DIR=%APPDATA%\Autodesk\ApplicationPlugins\CadJsonExtractor.bundle"

echo [1/3] Preparing target plugin directory...
if not exist "%APPDATA%\Autodesk\ApplicationPlugins" (
    mkdir "%APPDATA%\Autodesk\ApplicationPlugins"
)

echo [2/3] Installing CadJsonExtractor.bundle to:
echo       !TARGET_DIR!
xcopy /E /I /Y /Q "!SOURCE_BUNDLE!" "!TARGET_DIR!" >nul
if errorlevel 1 (
    echo.
    echo [ERROR] Copy failed. If AutoCAD is currently running, please close it and try again.
    echo.
    pause
    exit /b 1
)

echo [3/3] Unblocking security markers on installed assemblies...
powershell -NoProfile -Command "Get-ChildItem -Path '!TARGET_DIR!' -Recurse -File | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

echo.
echo ===================================================
echo   SUCCESS: CadJsonExtractor installed successfully!
echo.
echo   How to use:
echo     1. Open AutoCAD (2021, 2022, 2023, 2024, 2025, 2026+)
echo     2. Type 'JS'  to extract CAD geometry to a .json file
echo     3. Type 'JSC' to extract CAD geometry to the clipboard
echo ===================================================
echo.
pause
