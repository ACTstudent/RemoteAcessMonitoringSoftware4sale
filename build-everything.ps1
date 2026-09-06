# CAMS canonical Windows build, package, and validation pipeline.
# Output:
#   server-dist\CAMS-Server-Setup.exe
#   server-dist\CAMS-Server-Setup.exe.sha256
#   server-dist\release-manifest.json
#   client-dist\CAMS-Client-Setup.exe
#   client-dist\CAMS-Client-Setup.exe.sha256

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($PSScriptRoot)

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Reset-BuildDirectory {
    param([Parameter(Mandatory = $true)][string]$Name)

    $allowed = @("client-publish", "client-dist", "server-publish", "server-dist", "test-results")
    if ($allowed -notcontains $Name) {
        throw "Refusing to clean unexpected build directory '$Name'."
    }

    $path = [System.IO.Path]::GetFullPath((Join-Path $root $Name))
    $expected = Join-Path ($root.TrimEnd('\')) $Name
    if (-not $path.Equals($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean path outside the repository build outputs: $path"
    }

    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

function Write-Checksum {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$ChecksumPath
    )

    $hash = (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToUpperInvariant()
    "$hash  $([System.IO.Path]::GetFileName($FilePath))" |
        Set-Content -LiteralPath $ChecksumPath -Encoding Ascii
    return $hash
}

$versionPath = Join-Path $root "version.json"
$versionConfig = Get-Content -LiteralPath $versionPath -Raw | ConvertFrom-Json
$version = [string]$versionConfig.version
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "version.json version '$version' must be a three-part numeric version such as 2.8.0."
}
$versionArguments = @("-p:Version=$version", "-p:InformationalVersion=$version")

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host " CAMS $version - CANONICAL BUILD" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta

Write-Host "[1/9] Checking prerequisites and cleaning generated outputs..." -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK was not found."
}

# `dotnet` existing is not the same as the right SDK being selectable. global.json
# pins the band to 8.0.x, so a machine with only a 9.x SDK fails here with a
# reason rather than part-way through a build with a target-framework error.
$sdkVersion = & dotnet --version 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "No .NET SDK satisfies global.json. Install a .NET 8 SDK. dotnet reported: $sdkVersion"
}
if ([string]$sdkVersion -notmatch '^8\.') {
    throw "global.json requires a .NET 8 SDK but '$sdkVersion' was selected."
}

$iscc = $null
foreach ($candidate in @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)) {
    if ($candidate -and (Test-Path -LiteralPath $candidate)) {
        $iscc = $candidate
        break
    }
}
if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php."
}

$clientPub = Reset-BuildDirectory "client-publish"
$clientDist = Reset-BuildDirectory "client-dist"
$serverPub = Reset-BuildDirectory "server-publish"
$serverDist = Reset-BuildDirectory "server-dist"
Write-Host "  dotnet $(dotnet --version); Inno Setup: $iscc" -ForegroundColor Green

$serverTestProject = Join-Path $root "Monitoring And Remote Access\Server.Tests\Server.Tests.csproj"
$clientTestProject = Join-Path $root "Monitoring And Remote Access\Client.Tests\Client.Tests.csproj"
$solution = Join-Path $root "Monitoring And Remote Access\RemoteMonitoring.sln"
$clientProject = Join-Path $root "Monitoring And Remote Access\Client\Client.csproj"
$serverProject = Join-Path $root "Monitoring And Remote Access\Server\Server.csproj"

Write-Host "[2/9] Running server and client tests..." -ForegroundColor Cyan
# Results are written as TRX so a failing release build leaves behind which test
# failed and why, rather than only a red step in the log. CI uploads this folder.
$testResults = Reset-BuildDirectory "test-results"
Invoke-Native "dotnet" (@(
    "test", $serverTestProject, "-c", "Release", "--verbosity", "minimal",
    "--logger", "trx;LogFileName=server-tests.trx", "--results-directory", $testResults
) + $versionArguments) "Server tests failed"
Invoke-Native "dotnet" (@(
    "test", $clientTestProject, "-c", "Release", "--verbosity", "minimal",
    "--logger", "trx;LogFileName=client-tests.trx", "--results-directory", $testResults
) + $versionArguments) "Client tests failed"

Write-Host "[3/9] Building the solution..." -ForegroundColor Cyan
Invoke-Native "dotnet" (@("build", $solution, "-c", "Release", "-v", "minimal") + $versionArguments) "Solution build failed"

Write-Host "[4/9] Publishing and packaging the client first..." -ForegroundColor Cyan
$clientPublishArguments = @(
    "publish", $clientProject, "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true", "-o", $clientPub
) + $versionArguments
Invoke-Native "dotnet" $clientPublishArguments "Client publish failed"
Invoke-Native $iscc @("/DMyAppVersion=$version", "/o$clientDist", (Join-Path $root "client-installer.iss")) "Client installer build failed"

$clientInstallerName = "CAMS-Client-Setup.exe"
$clientChecksumName = "$clientInstallerName.sha256"
$clientInstaller = Join-Path $clientDist $clientInstallerName
$clientChecksum = Join-Path $clientDist $clientChecksumName
if (-not (Test-Path -LiteralPath $clientInstaller -PathType Leaf)) {
    throw "Client installer was not produced: $clientInstaller"
}
$clientHash = Write-Checksum $clientInstaller $clientChecksum
$clientSize = (Get-Item -LiteralPath $clientInstaller).Length

Write-Host "[5/9] Publishing the server with release version metadata..." -ForegroundColor Cyan
$serverPublishArguments = @(
    "publish", $serverProject, "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-o", $serverPub
) + $versionArguments
Invoke-Native "dotnet" $serverPublishArguments "Server publish failed"

# Only the checked-in, blank-secret base settings file may enter the installer.
Get-ChildItem -LiteralPath $serverPub -Recurse -File -Filter "appsettings.*.json" -ErrorAction SilentlyContinue |
    Remove-Item -Force
$publishedSettingsPath = Join-Path $serverPub "appsettings.json"
if (-not (Test-Path -LiteralPath $publishedSettingsPath -PathType Leaf)) {
    throw "Published server is missing appsettings.json."
}
$publishedSettings = Get-Content -LiteralPath $publishedSettingsPath -Raw | ConvertFrom-Json
foreach ($property in @("CertificatePassword", "InitialAdminPassword")) {
    if (-not [string]::IsNullOrEmpty([string]$publishedSettings.Cams.$property)) {
        throw "Refusing to package a non-empty Cams:$property appsettings secret."
    }
}

$forbiddenPublishFiles = @(Get-ChildItem -LiteralPath $serverPub -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -ieq ".pfx" -or $_.Name -like "CAMS.db*" })
if ($forbiddenPublishFiles.Count -ne 0) {
    throw "Refusing to package PFX or CAMS database files from server-publish."
}

Write-Host "[6/9] Staging exact Deployment Hub assets..." -ForegroundColor Cyan
$deploymentAssets = Join-Path $serverPub "DeploymentAssets"
New-Item -ItemType Directory -Path $deploymentAssets | Out-Null
Copy-Item -LiteralPath $clientInstaller -Destination (Join-Path $deploymentAssets $clientInstallerName)
Copy-Item -LiteralPath $clientChecksum -Destination (Join-Path $deploymentAssets $clientChecksumName)

$deploymentManifest = [ordered]@{
    schemaVersion = 1
    product = "CAMS Student Client"
    clientVersion = $version
    serverVersion = $version
    installerFileName = $clientInstallerName
    installerSize = $clientSize
    installerSha256 = $clientHash
}
$deploymentManifest | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $deploymentAssets "deployment-manifest.json") -Encoding UTF8

$stagedNames = @(Get-ChildItem -LiteralPath $deploymentAssets -File | ForEach-Object { $_.Name } | Sort-Object)
$expectedStagedNames = @($clientInstallerName, $clientChecksumName, "deployment-manifest.json") | Sort-Object
if ((Compare-Object $stagedNames $expectedStagedNames).Count -ne 0) {
    throw "DeploymentAssets must contain exactly the installer, checksum, and deployment manifest."
}

Write-Host "[7/9] Building the server installer after Deployment Hub staging..." -ForegroundColor Cyan
Invoke-Native $iscc @("/DMyAppVersion=$version", "/o$serverDist", (Join-Path $root "server-installer.iss")) "Server installer build failed"

Write-Host "[8/9] Generating server checksum and release manifest..." -ForegroundColor Cyan
$serverInstallerName = "CAMS-Server-Setup.exe"
$serverChecksumName = "$serverInstallerName.sha256"
$serverInstaller = Join-Path $serverDist $serverInstallerName
$serverChecksum = Join-Path $serverDist $serverChecksumName
if (-not (Test-Path -LiteralPath $serverInstaller -PathType Leaf)) {
    throw "Server installer was not produced: $serverInstaller"
}
$serverHash = Write-Checksum $serverInstaller $serverChecksum
$serverSize = (Get-Item -LiteralPath $serverInstaller).Length

$releaseManifest = [ordered]@{
    schemaVersion = 1
    product = "CAMS"
    version = $version
    artifacts = @(
        [ordered]@{
            fileName = $serverInstallerName
            checksumFileName = $serverChecksumName
            size = $serverSize
            sha256 = $serverHash
        },
        [ordered]@{
            fileName = $clientInstallerName
            checksumFileName = $clientChecksumName
            size = $clientSize
            sha256 = $clientHash
        }
    )
}
$releaseManifest | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $serverDist "release-manifest.json") -Encoding UTF8

Write-Host "[9/9] Validating installers and release metadata..." -ForegroundColor Cyan
& (Join-Path $root "test-installer.ps1") -Root $root
if ($LASTEXITCODE -ne 0) {
    throw "Installer validation failed (exit code $LASTEXITCODE)."
}

Write-Host ""
Write-Host "BUILD COMPLETE - CAMS v$version" -ForegroundColor Green
Write-Host "  Server release: $serverDist" -ForegroundColor Cyan
Write-Host "  Client release: $clientDist" -ForegroundColor Cyan
