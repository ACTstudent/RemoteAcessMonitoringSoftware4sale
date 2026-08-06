# CAMS -- Build both installers in one shot
# Double-click build-installers.bat or run:  .\build-installers.ps1
#
# Requires: .NET 8 SDK + Inno Setup 6 (https://jrsoftware.org/isdl.php)
# Output:
#   server-dist\CAMS-Server-Setup.exe   -- run on the teacher/lab PC
#   client-dist\CAMS-Client-Setup.exe   -- run on each student PC

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host " CAMS Installer Builder" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# ---- prerequisites ----
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET 8 SDK not found." -ForegroundColor Red
    Write-Host "Install from https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    Pause
    exit 1
}

$iscc = $null
foreach ($candidate in @(
    "$env:ProgramFiles (x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)) {
    if (Test-Path $candidate) { $iscc = $candidate; break }
}

if (-not $iscc) {
    Write-Host "WARNING: Inno Setup 6 not found." -ForegroundColor Yellow
    Write-Host "Install it from https://jrsoftware.org/isdl.php then re-run." -ForegroundColor Yellow
    Write-Host "Without it, only the publish folders will be created (no .exe installers)." -ForegroundColor Yellow
    Write-Host ""
}

# ---- 1. Build solution ----
Write-Host "[1/4] Building solution..." -ForegroundColor Cyan
$sln = Join-Path $root "Monitoring And Remote Access\RemoteMonitoring.sln"
dotnet build $sln -c Release -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: build failed." -ForegroundColor Red
    Pause
    exit $LASTEXITCODE
}
Write-Host "  Build ok." -ForegroundColor Green

# ---- 2. Publish server ----
Write-Host ""
Write-Host "[2/4] Publishing server..." -ForegroundColor Cyan
$serverProject = Join-Path $root "Monitoring And Remote Access\Server\Server.csproj"
$serverPub = Join-Path $root "server-publish"
dotnet publish $serverProject -c Release -o $serverPub
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: server publish failed." -ForegroundColor Red
    Pause
    exit $LASTEXITCODE
}
Write-Host "  Server published to server-publish" -ForegroundColor Green

# ---- 3. Publish client (self-contained) ----
Write-Host ""
Write-Host "[3/4] Publishing client (self-contained, single-file)..." -ForegroundColor Cyan
$clientProject = Join-Path $root "Monitoring And Remote Access\Client\Client.csproj"
$clientPub = Join-Path $root "client-publish"
dotnet publish $clientProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o $clientPub
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: client publish failed." -ForegroundColor Red
    pause
    exit $LASTEXITCODE
}
Write-Host "  Client published to client-publish" -ForegroundColor Green

# Copy the portable launcher into the client publish folder
Copy-Item -Path (Join-Path $root "client-portable.bat") -Destination $clientPub -Force
Write-Host "  Portable launcher copied to client-publish" -ForegroundColor Green

# ---- 4. Build installers (Inno Setup) ----
if (-not $iscc) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host " DONE (no installers -- Inno Setup missing)" -ForegroundColor Yellow
    Write-Host " server-publish  -- copy this folder to the server PC" -ForegroundColor Cyan
    Write-Host " client-publish  -- copy this folder to each student PC" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Yellow
    pause
    exit 0
}

Write-Host ""
Write-Host "[4/4] Building installers with Inno Setup..." -ForegroundColor Cyan

$serverDist = Join-Path $root "server-dist"
$clientDist = Join-Path $root "client-dist"

New-Item -ItemType Directory -Force -Path $serverDist | Out-Null
New-Item -ItemType Directory -Force -Path $clientDist | Out-Null

$serverIss = Join-Path $root "server-installer.iss"
$clientIss = Join-Path $root "client-installer.iss"

Write-Host "  Compiling server installer..."
& $iscc "/o$serverDist" $serverIss
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: server installer failed." -ForegroundColor Red
    pause
    exit $LASTEXITCODE
}

Write-Host "  Compiling client installer..."
& $iscc "/o$clientDist" $clientIss
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: client installer failed." -ForegroundColor Red
    pause
    exit $LASTEXITCODE
}

# ---- Done ----
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " ALL DONE!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Server installer:" -ForegroundColor Cyan
Write-Host "    $(Get-ChildItem $serverDist -Filter *.exe | Select-Object -ExpandProperty FullName)" -ForegroundColor White
Write-Host "    --> Copy to the lab teacher PC and run it." -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Client installer:" -ForegroundColor Cyan
Write-Host "    $(Get-ChildItem $clientDist -Filter *.exe | Select-Object -ExpandProperty FullName)" -ForegroundColor White
Write-Host "    --> Distribute to each student PC." -ForegroundColor DarkGray
Write-Host ""
pause