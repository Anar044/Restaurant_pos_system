@echo off
title Restaurant Platform - START
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\dev-platform.ps1" -Action Start
echo.
if errorlevel 1 (
  echo Startup failed. See the error above and .devlogs folder.
) else (
  echo You can close this window. Services will keep running.
)
pause
