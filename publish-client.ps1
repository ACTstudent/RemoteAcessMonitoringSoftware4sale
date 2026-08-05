# Publish the CAMS WinForms client and build an installer wizard.
# Requires: .NET SDK 8 and Inno Setup 6 (https://jrsoftware.org/isdl.php)
# Run from the repo root:  .\publish-client.ps1

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$clientProject = Join-Path $root "Monitoring And Remote Access\Client\Client.csproj"
$outDir = Join-Path $root "client-publish"
$distDir = Join-Path $root "client-dist"
$issFile = Join-Path $root "client-installer.iss"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

# Build a self-contained, single-file client so student PCs don't need .NET installed.
Write-Host "Publishing client (self-contained)..." -ForegroundColor Cyan
dotnet publish $clientProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o $outDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: client publish failed." -ForegroundColor Red
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
    Write-Host "Client published to: $outDir" -ForegroundColor Green
    Write-Host "Inno Setup 6 not found, so the installer was NOT built." -ForegroundColor Yellow
    Write-Host "Install it from https://jrsoftware.org/isdl.php then run this script again (or compile client-installer.iss manually)." -ForegroundColor Yellow
    exit 0
}

Write-Host "Building installer with Inno Setup..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
& $iscc "/o$distDir" $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: installer build failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "Installer: $(Get-ChildItem $distDir -Filter *.exe | Select-Object -ExpandProperty FullName)" -ForegroundColor Green
