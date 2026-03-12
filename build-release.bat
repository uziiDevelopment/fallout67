@echo off
echo Running Velopack Build and GitHub Upload...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" -Version 2.0.0 -GithubToken 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
pause
