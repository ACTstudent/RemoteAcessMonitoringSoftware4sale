# CAMS - Validate the built installers
# Run after build-everything.ps1; checks integrity of 2 output files.
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

# ---- Check 1: Both .exe files exist ----
$checks = @(
    @{Label="Server installer"; Path=Join-Path $serverDist "CAMS-Server-Setup.exe"},
    @{Label="Client installer"; Path=Join-Path $clientDist "CAMS-Client-Setup.exe"}
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