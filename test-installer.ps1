# CAMS - Validate the built installers
# Run after build-everything.ps1; checks integrity of 4 output files.
#
# Usage:  powershell -File test-installer.ps1

param(
    [string]$Root = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
$failed = 0

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " CAMS INSTALLER VALIDATION" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$serverDist = Join-Path $Root "server-dist"
$clientDist = Join-Path $Root "client-dist"

# ---- Check 1: All files exist ----
$checks = @(
    @{Label="Server installer"; Path=Join-Path $serverDist "CAMS-Server-Setup.exe"},
    @{Label="Client installer"; Path=Join-Path $clientDist "CAMS-Client-Setup.exe"},
    @{Label="Server portable zip"; Path=Join-Path $serverDist "CAMS-Server-Portable.zip"},
    @{Label="Client portable zip"; Path=Join-Path $clientDist "CAMS-Client-Portable.zip"}
)

foreach ($c in $checks) {
    if (Test-Path $c.Path) {
        $size = "{0:N0} MB" -f ((Get-Item $c.Path).Length / 1MB)
        Write-Host "  [PASS] $($c.Label)  ($size)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] $($c.Label)  -- file not found: $($c.Path)" -ForegroundColor Red
        $failed++
    }
}

# ---- Check 2: Zip integrity ----
Write-Host ""
Write-Host "  Checking zip integrity..." -ForegroundColor Cyan

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temp = Join-Path $env:TEMP "cams-test-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force -Path $temp | Out-Null

    $svrZip = Join-Path $serverDist "CAMS-Server-Portable.zip"
    [System.IO.Compression.ZipFile]::ExtractToDirectory($svrZip, (Join-Path $temp "server"))
    if (Test-Path (Join-Path $temp "server\Server.exe")) {
        Write-Host "  [PASS] Server.zip contains Server.exe" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Server.zip missing Server.exe" -ForegroundColor Red
        $failed++
    }

    $cliZip = Join-Path $clientDist "CAMS-Client-Portable.zip"
    [System.IO.Compression.ZipFile]::ExtractToDirectory($cliZip, (Join-Path $temp "client"))
    if (Test-Path (Join-Path $temp "client\Client.exe")) {
        Write-Host "  [PASS] Client.zip contains Client.exe" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Client.zip missing Client.exe" -ForegroundColor Red
        $failed++
    }

    Remove-Item $temp -Recurse -Force

} catch {
    Write-Host "  [FAIL] Zip extract error: $_" -ForegroundColor Red
    $failed++
}

# ---- Done ----
Write-Host ""
if ($failed -eq 0) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " ALL CHECKS PASSED" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
} else {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host " $failed CHECK(S) FAILED" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit $failed
}