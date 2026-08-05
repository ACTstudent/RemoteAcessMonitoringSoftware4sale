# Publish the CAMS server into a deployable folder and build an installer wizard.
# Requires: .NET SDK 8 and optionally Inno Setup 6 (https://jrsoftware.org/isdl.php)
# Run from the repo root:  .\publish.ps1

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$serverProject = Join-Path $root "Monitoring And Remote Access\Server\Server.csproj"
$publishDir  = Join-Path $root "server-publish"
$distDir     = Join-Path $root "server-dist"
$issFile     = Join-Path $root "server-installer.iss"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

# Publish server as framework-dependent (the server PC runs .NET 8 hosting bundle / SDK).
Write-Host "Publishing CAMS server to $publishDir..." -ForegroundColor Cyan
dotnet publish $serverProject -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: server publish failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Locate Inno Setup compiler
$iscc = $null
foreach ($candidate in @(
    "$env:ProgramFiles (x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)) {
    if (Test-Path $candidate) { $iscc = $candidate; break }
}

if (-not $iscc) {
    Write-Host ""
    Write-Host "Server published to: $publishDir" -ForegroundColor Green
    Write-Host "Inno Setup 6 not found, so the installer was NOT built." -ForegroundColor Yellow
    Write-Host "Install it from https://jrsoftware.org/isdl.php then run this script again." -ForegroundColor Yellow
    exit 0
}

Write-Host "Building server installer with Inno Setup..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
& $iscc "/o$distDir" $issFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: installer build failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "Installer: $(Get-ChildItem $distDir -Filter *.exe | Select-Object -ExpandProperty FullName)" -ForegroundColor Green
Write-Host ""
Write-Host "Copy this installer to the server PC and run it." -ForegroundColor Green
Write-Host "The server will install to %LOCALAPPDATA%\CAMS Server and SQLite auto-creates the DB on first run." -ForegroundColor Green