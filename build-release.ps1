<#
.SYNOPSIS
  Builds and packages Fallout 67 as a Velopack release.

.PARAMETER Version
  The semantic version for this release (e.g. 1.0.0, 1.2.3).
  Defaults to 1.0.0 if not specified.

.PARAMETER GithubToken
  Optional GitHub personal access token for uploading releases.
  If provided, the release will be uploaded to GitHub Releases automatically.

.EXAMPLE
  .\build-release.ps1 -Version 1.0.0
  .\build-release.ps1 -Version 1.1.0 -GithubToken ghp_xxxx
#>
param(
    [string]$Version = "1.1.0",
    [string]$GithubToken = ""
)

$ErrorActionPreference = "Stop"

$ProjectDir = "$PSScriptRoot\fallout 67"
$PublishDir = "$PSScriptRoot\publish"
$ReleasesDir = "$PSScriptRoot\releases"
$PackId = "Fallout67"
$MainExe = "fallout 67.exe"

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  VAULT-TEC RELEASE BUILDER  v$Version" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""

# ── Step 1: Publish the app ──────────────────────────────────────────────────
Write-Host "[1/3] Publishing .NET app (self-contained, win-x64)..." -ForegroundColor Cyan

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

dotnet publish "$ProjectDir\fallout 67.csproj" `
    --self-contained `
    -r win-x64 `
    -p:Version=$Version `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  Published to $PublishDir" -ForegroundColor Gray

# ── Step 2: Pack with Velopack ───────────────────────────────────────────────
Write-Host "[2/3] Packing Velopack release..." -ForegroundColor Cyan

if (Test-Path $ReleasesDir) { Remove-Item $ReleasesDir -Recurse -Force }

vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $PublishDir `
    --mainExe $MainExe `
    --outputDir $ReleasesDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: vpk pack failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  Release files created in $ReleasesDir" -ForegroundColor Gray

# ── Step 3 (Optional): Upload to GitHub Releases ────────────────────────────
if ($GithubToken -ne "") {
    Write-Host "[3/3] Uploading to GitHub Releases..." -ForegroundColor Cyan

    vpk upload github `
        --repoUrl "https://github.com/uziiDevelopment/fallout67" `
        --token $GithubToken `
        --outputDir $ReleasesDir `
        --tag "v$Version" `
        --publish

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: vpk upload failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "  Uploaded to GitHub Releases as v$Version" -ForegroundColor Gray
} else {
    Write-Host "[3/3] Skipping upload (no --GithubToken provided)" -ForegroundColor Yellow
    Write-Host "  To upload: .\build-release.ps1 -Version $Version -GithubToken YOUR_TOKEN" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  BUILD COMPLETE!" -ForegroundColor Green
Write-Host "  Installer: $ReleasesDir\${PackId}-Setup.exe" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
