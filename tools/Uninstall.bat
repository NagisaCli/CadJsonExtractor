@echo off
setlocal
title CadJsonExtractor Uninstaller

echo ===================================================
echo     CadJsonExtractor - One-Click Uninstaller
echo ===================================================
echo.

set "TARGET_DIR=%APPDATA%\Autodesk\ApplicationPlugins\CadJsonExtractor.bundle"

if exist "%TARGET_DIR%" (
    echo Removing CadJsonExtractor from:
    echo %TARGET_DIR%
    rd /s /q "%TARGET_DIR%"
    echo.
    echo CadJsonExtractor has been completely removed.
) else (
    echo CadJsonExtractor is not installed.
)

echo.
pause
