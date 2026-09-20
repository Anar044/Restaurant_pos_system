@echo off
title Restaurant Platform - STATUS
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\dev-platform.ps1" -Action Status
echo.
pause
