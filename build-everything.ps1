# CAMS -- One-shot build pipeline
# Run:  powershell -NoProfile -ExecutionPolicy Bypass -File build-everything.ps1
#
# Output (4 files):
#   server-dist\CAMS-Server-Setup.exe       -- installer wizard for teacher PC
#   client-dist\CAMS-Client-Setup.exe       -- installer wizard for student PCs
#   server-dist\CAMS-Server-Portable.zip    -- no-install portable bundle
#   client-dist\CAMS-Client-Portable.zip    -- no-install portable bundle

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$version = "2.5.0"

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  CAMS $version - FULL BUILD PIPELINE" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# ---- 0. Prerequisites ----
Write-Host "[0/7] Checking prerequisites..." -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "  FAIL: .NET 8 SDK not found." -ForegroundColor Red
    exit 1
}
Write-Host "  dotnet $(dotnet --version)" -ForegroundColor Green

$iscc = $null
foreach ($c in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
    if (Test-Path $c) { $iscc = $c; break }
}
if (-not $iscc) {
    Write-Host "  FAIL: Inno Setup 6 not found. Install from https://jrsoftware.org/isdl.php" -ForegroundColor Red
    exit 1
}
Write-Host "  Inno Setup 6 -> $iscc" -ForegroundColor Green
Write-Host "  OK." -ForegroundColor Green

# ---- 1. Run tests ----
Write-Host ""
Write-Host "[2/7] Running tests..." -ForegroundColor Cyan
$testProject = Join-Path $root "Monitoring And Remote Access\Server.Tests\Server.Tests.csproj"
dotnet test $testProject -c Release --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL: tests failed. Fix before building." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "  Tests passed." -ForegroundColor Green

# ---- 2. Build solution ----
Write-Host ""
Write-Host "[3/7] Building solution..." -ForegroundColor Cyan
$sln = Join-Path $root "Monitoring And Remote Access\RemoteMonitoring.sln"
dotnet build $sln -c Release -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL: build failed." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "  Build ok." -ForegroundColor Green

# ---- 3. Publish server ----
Write-Host ""
Write-Host "[4/7] Publishing server..." -ForegroundColor Cyan
$serverProject = Join-Path $root "Monitoring And Remote Access\Server\Server.csproj"
$serverPub = Join-Path $root "server-publish"
dotnet publish $serverProject -c Release -o $serverPub
if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL: server publish failed." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "  Server published." -ForegroundColor Green

# ---- 4. Publish client (self-contained) ----
Write-Host ""
Write-Host "[5/7] Publishing client (self-contained, single-file)..." -ForegroundColor Cyan
$clientProject = Join-Path $root "Monitoring And Remote Access\Client\Client.csproj"
$clientPub = Join-Path $root "client-publish"
dotnet publish $clientProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o $clientPub
if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL: client publish failed." -ForegroundColor Red
    exit $LASTEXITCODE
}
Copy-Item -Path (Join-Path $root "client-portable.bat") -Destination $clientPub -Force
Write-Host "  Client published." -ForegroundColor Green

# ---- 5. Build installers ----
Write-Host ""
Write-Host "[6/7] Building installers with Inno Setup..." -ForegroundColor Cyan
$serverDist = Join-Path $root "server-dist"
$clientDist = Join-Path $root "client-dist"
New-Item -ItemType Directory -Force -Path $serverDist | Out-Null
New-Item -ItemType Directory -Force -Path $clientDist | Out-Null

& $iscc "/o$serverDist" (Join-Path $root "server-installer.iss")
if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL: server installer build failed." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "  Server installer built." -ForegroundColor Green

& $iscc "/o$clientDist" (Join-Path $root "client-installer.iss")
if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL: client installer build failed." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "  Client installer built." -ForegroundColor Green

# ---- 6. Package portable zips ----
Write-Host ""
Write-Host "[7/7] Packaging portable zips..." -ForegroundColor Cyan

$serverZip = Join-Path $serverDist "CAMS-Server-Portable.zip"
$clientZip = Join-Path $clientDist "CAMS-Client-Portable.zip"

if (Test-Path $serverZip) { Remove-Item $serverZip -Force }
Compress-Archive -Path "$serverPub\*" -DestinationPath $serverZip -Force
$serverZipSize = "{0:N0}" -f ((Get-Item $serverZip).Length / 1MB)

if (Test-Path $clientZip) { Remove-Item $clientZip -Force }
Compress-Archive -Path "$clientPub\*" -DestinationPath $clientZip -Force
$clientZipSize = "{0:N0}" -f ((Get-Item $clientZip).Length / 1MB)

Write-Host "  Server zip  -> $serverZip  ($($serverZipSize) MB)" -ForegroundColor Green
Write-Host "  Client zip  -> $clientZip  ($($clientZipSize) MB)" -ForegroundColor Green

# ---- Done ----
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " BUILD COMPLETE - CAMS v$version" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  $serverDist\" -ForegroundColor Cyan
Get-ChildItem $serverDist | ForEach-Object { Write-Host "    $($_.Name)  ($('{0:N0}' -f ($_.Length / 1MB)) MB)" -ForegroundColor White }
Write-Host ""
Write-Host "  $clientDist\" -ForegroundColor Cyan
Get-ChildItem $clientDist | ForEach-Object { Write-Host "    $($_.Name)  ($('{0:N0}' -f ($_.Length / 1MB)) MB)" -ForegroundColor White }
Write-Host ""
Write-Host "  -> Distribute server installer to the teacher PC."
Write-Host "  -> Distribute client installer (or portable) to student PCs."
Write-Host ""