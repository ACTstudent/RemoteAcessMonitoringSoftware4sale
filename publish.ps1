# Publish the CAMS server into a deployable folder.
# Run from the repo root:  .\publish.ps1

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$serverProject = Join-Path $root "Monitoring And Remote Access\Server\Server.csproj"
$outDir = Join-Path $root "publish"

Write-Host "Publishing CAMS server to $outDir" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

# Publish as framework-dependent (fast). Add --self-contained true -r win-x64 for a standalone build.
dotnet publish $serverProject -c Release -o $outDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: publish failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Done. Deployable server is in: $outDir" -ForegroundColor Green
Write-Host "Copy this folder to the server PC and run Server.exe" -ForegroundColor Green
Write-Host "See DEPLOYMENT.md for database setup and Windows Service steps." -ForegroundColor Green
