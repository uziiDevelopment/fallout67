@echo off
echo Running Velopack Build and GitHub Upload...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" -Version 2.11.2
pause
