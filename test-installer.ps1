# CAMS - Validate the built installers
# Run after build-everything.ps1; checks installers and their checksums.
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

# ---- Check 1: Installers and checksums exist ----
$checks = @(
    @{Label="Server installer"; Path=Join-Path $serverDist "CAMS-Server-Setup.exe"},
    @{Label="Server checksum"; Path=Join-Path $serverDist "CAMS-Server-Setup.exe.sha256"},
    @{Label="Client installer"; Path=Join-Path $clientDist "CAMS-Client-Setup.exe"},
    @{Label="Client checksum"; Path=Join-Path $clientDist "CAMS-Client-Setup.exe.sha256"}
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

# ---- Check 2: SHA-256 checksums match the installers ----
$artifacts = @(
    @{Installer=Join-Path $serverDist "CAMS-Server-Setup.exe"; Checksum=Join-Path $serverDist "CAMS-Server-Setup.exe.sha256"},
    @{Installer=Join-Path $clientDist "CAMS-Client-Setup.exe"; Checksum=Join-Path $clientDist "CAMS-Client-Setup.exe.sha256"}
)

foreach ($artifact in $artifacts) {
    $expected = (Get-Content -LiteralPath $artifact.Checksum -Raw).Trim().Split()[0].ToUpperInvariant()
    $actual = (Get-FileHash -LiteralPath $artifact.Installer -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($expected -eq $actual) {
        Write-Host "  [PASS] SHA-256 $([System.IO.Path]::GetFileName($artifact.Installer))" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] SHA-256 mismatch: $($artifact.Installer)" -ForegroundColor Red
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
