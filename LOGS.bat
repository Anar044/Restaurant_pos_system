@echo off
cd /d "%~dp0"
if not exist ".devlogs" mkdir ".devlogs"
start "" explorer.exe "%~dp0.devlogs"
