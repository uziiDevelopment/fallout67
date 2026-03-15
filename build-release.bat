@echo off
echo Running Velopack Build and GitHub Upload...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" -Version 2.9.5
pause
