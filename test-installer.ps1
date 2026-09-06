# Validates canonical CAMS build staging and packaged release outputs.
param(
    [string]$Root = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
$failed = 0
$Root = [System.IO.Path]::GetFullPath($Root)

function Pass {
    param([string]$Message)
    Write-Host "  [PASS] $Message" -ForegroundColor Green
}

function Fail {
    param([string]$Message)
    $script:failed++
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
}

function Test-VersionAlignment {
    param([string]$Actual, [string]$Expected)

    $actualVersion = $null
    $expectedVersion = $null
    if (-not [Version]::TryParse(($Actual -split '\+')[0], [ref]$actualVersion) -or
        -not [Version]::TryParse(($Expected -split '\+')[0], [ref]$expectedVersion)) {
        return $Actual -eq $Expected
    }
    return $actualVersion.Major -eq $expectedVersion.Major -and
        $actualVersion.Minor -eq $expectedVersion.Minor -and
        [Math]::Max(0, $actualVersion.Build) -eq [Math]::Max(0, $expectedVersion.Build) -and
        [Math]::Max(0, $actualVersion.Revision) -eq [Math]::Max(0, $expectedVersion.Revision)
}

function Test-ChecksumFile {
    param([string]$InstallerPath, [string]$ChecksumPath)

    if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $ChecksumPath -PathType Leaf)) {
        Fail "Missing installer or checksum: $InstallerPath"
        return $null
    }

    $parts = @((Get-Content -LiteralPath $ChecksumPath -Raw).Trim() -split '\s+' | Where-Object { $_ })
    $expectedName = [System.IO.Path]::GetFileName($InstallerPath)
    if ($parts.Count -ne 2 -or $parts[0] -notmatch '^[A-Fa-f0-9]{64}$' -or
        $parts[1].TrimStart('*') -cne $expectedName) {
        Fail "Invalid checksum format for $expectedName"
        return $null
    }

    $actual = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($parts[0].ToUpperInvariant() -ne $actual) {
        Fail "SHA-256 mismatch for $expectedName"
        return $null
    }
    Pass "SHA-256 $expectedName"
    return $actual
}

function Find-InnoUnpacker {
    $command = Get-Command innounp.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA "Programs\innounp\innounp.exe"),
        (Join-Path $env:ProgramFiles "innounp\innounp.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "innounp\innounp.exe")
    )) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $candidate }
    }
    return $null
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " CAMS INSTALLER VALIDATION" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$versionPath = Join-Path $Root "version.json"
try {
    $version = [string](Get-Content -LiteralPath $versionPath -Raw | ConvertFrom-Json).version
} catch {
    Fail "version.json is missing or invalid: $($_.Exception.Message)"
    $version = ""
}
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    Fail "version.json must contain a three-part numeric version"
}

$serverDist = Join-Path $Root "server-dist"
$clientDist = Join-Path $Root "client-dist"
$serverPublish = Join-Path $Root "server-publish"
$clientPublish = Join-Path $Root "client-publish"
$deploymentAssets = Join-Path $serverPublish "DeploymentAssets"
$serverInstaller = Join-Path $serverDist "CAMS-Server-Setup.exe"
$serverChecksum = "$serverInstaller.sha256"
$clientInstaller = Join-Path $clientDist "CAMS-Client-Setup.exe"
$clientChecksum = "$clientInstaller.sha256"

$serverHash = Test-ChecksumFile $serverInstaller $serverChecksum
$clientHash = Test-ChecksumFile $clientInstaller $clientChecksum

foreach ($installer in @($serverInstaller, $clientInstaller)) {
    if (Test-Path -LiteralPath $installer -PathType Leaf) {
        $productVersion = (Get-Item -LiteralPath $installer).VersionInfo.ProductVersion
        if ($productVersion -and (Test-VersionAlignment $productVersion $version)) {
            Pass "$([System.IO.Path]::GetFileName($installer)) product version $productVersion aligns with version.json"
        } else {
            Fail "$([System.IO.Path]::GetFileName($installer)) product version '$productVersion' does not align with version.json '$version'"
        }

        # Checked separately from the product version because the two come from
        # different Inno directives. Only this one reaches Explorer's "File
        # version" column, and it was empty through 2.12.0 - the setup file had
        # no version on its face and one release looked like the last.
        $fileVersion = (Get-Item -LiteralPath $installer).VersionInfo.FileVersion
        if ($fileVersion -and (Test-VersionAlignment $fileVersion $version)) {
            Pass "$([System.IO.Path]::GetFileName($installer)) file version $fileVersion aligns with version.json"
        } else {
            Fail "$([System.IO.Path]::GetFileName($installer)) file version '$fileVersion' does not align with version.json '$version'"
        }
    }
}

$manifestPath = Join-Path $deploymentAssets "deployment-manifest.json"
$manifest = $null
try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
} catch {
    Fail "Deployment manifest is missing or invalid JSON: $($_.Exception.Message)"
}

if ($manifest) {
    $requiredProperties = @("schemaVersion", "product", "clientVersion", "serverVersion", "installerFileName", "installerSize", "installerSha256")
    $actualProperties = @($manifest.PSObject.Properties.Name)
    $schemaDifference = @(Compare-Object ($requiredProperties | Sort-Object) ($actualProperties | Sort-Object))
    if ($schemaDifference.Count -eq 0 -and [int]$manifest.schemaVersion -eq 1 -and
        $manifest.product -eq "CAMS Student Client" -and $manifest.installerFileName -ceq "CAMS-Client-Setup.exe") {
        Pass "Deployment manifest schema version 1"
    } else {
        Fail "Deployment manifest schema or fixed metadata is invalid"
    }

    if ($manifest.clientVersion -eq $version -and $manifest.serverVersion -eq $version) {
        Pass "Deployment client/server versions align with version.json"
    } else {
        Fail "Deployment manifest versions do not align with version.json '$version'"
    }

    $stagedClient = Join-Path $deploymentAssets "CAMS-Client-Setup.exe"
    $stagedChecksum = Join-Path $deploymentAssets "CAMS-Client-Setup.exe.sha256"
    $stagedHash = Test-ChecksumFile $stagedClient $stagedChecksum
    if ($stagedHash -and $clientHash -and $stagedHash -eq $clientHash -and
        [long]$manifest.installerSize -eq (Get-Item -LiteralPath $stagedClient).Length -and
        ([string]$manifest.installerSha256).ToUpperInvariant() -eq $stagedHash) {
        Pass "Deployment installer size/hash match the release client installer"
    } else {
        Fail "Deployment installer, size, or hash does not match the release client installer"
    }

    $stagedNames = @(Get-ChildItem -LiteralPath $deploymentAssets -File -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Name } | Sort-Object)
    $expectedNames = @("CAMS-Client-Setup.exe", "CAMS-Client-Setup.exe.sha256", "deployment-manifest.json") | Sort-Object
    if ((Compare-Object $stagedNames $expectedNames).Count -eq 0) {
        Pass "DeploymentAssets contains exactly the three canonical files"
    } else {
        Fail "DeploymentAssets contains missing or stale files"
    }
}

$releaseManifestPath = Join-Path $serverDist "release-manifest.json"
$releaseManifest = $null
try {
    $releaseManifest = Get-Content -LiteralPath $releaseManifestPath -Raw | ConvertFrom-Json
} catch {
    Fail "Release manifest is missing or invalid JSON: $($_.Exception.Message)"
}
if ($releaseManifest) {
    if ([int]$releaseManifest.schemaVersion -eq 1 -and $releaseManifest.product -eq "CAMS" -and
        $releaseManifest.version -eq $version -and @($releaseManifest.artifacts).Count -eq 2) {
        Pass "Release manifest schema and version align with version.json"
    } else {
        Fail "Release manifest schema or version is invalid"
    }

    $releaseArtifactsValid = $true
    foreach ($expected in @(
        @{ Name = "CAMS-Server-Setup.exe"; Hash = $serverHash; Path = $serverInstaller },
        @{ Name = "CAMS-Client-Setup.exe"; Hash = $clientHash; Path = $clientInstaller }
    )) {
        $artifact = @($releaseManifest.artifacts | Where-Object { $_.fileName -ceq $expected.Name })
        if ($artifact.Count -ne 1 -or -not $expected.Hash -or
            ([string]$artifact[0].sha256).ToUpperInvariant() -ne $expected.Hash -or
            [long]$artifact[0].size -ne (Get-Item -LiteralPath $expected.Path).Length -or
            $artifact[0].checksumFileName -cne "$($expected.Name).sha256") {
            $releaseArtifactsValid = $false
        }
    }
    if ($releaseArtifactsValid) { Pass "Release manifest artifact size/hash metadata" } else { Fail "Release manifest artifact metadata does not match outputs" }
}

$forbidden = @()
foreach ($directory in @($serverPublish, $clientPublish, $deploymentAssets)) {
    if (Test-Path -LiteralPath $directory -PathType Container) {
        $forbidden += @(Get-ChildItem -LiteralPath $directory -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -ieq ".pfx" })
    }
}
if ($forbidden.Count -eq 0) { Pass "No PFX exists in package staging" } else { Fail "PFX found in package staging" }

$innounp = Find-InnoUnpacker
if ($innounp -and (Test-Path -LiteralPath $serverInstaller -PathType Leaf)) {
    $extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("cams-installer-validation-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $extractRoot | Out-Null
    try {
        & $innounp -x "-d$extractRoot" $serverInstaller | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "innounp exited with code $LASTEXITCODE" }
        $embeddedClients = @(Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter "CAMS-Client-Setup.exe")
        $embeddedPfx = @(Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter "*.pfx")
        if ($embeddedClients.Count -eq 1 -and
            (Get-FileHash -LiteralPath $embeddedClients[0].FullName -Algorithm SHA256).Hash -eq $clientHash) {
            Pass "Extracted server installer embeds the matching client installer"
        } else {
            Fail "Extracted server installer does not contain exactly one matching client installer"
        }
        if ($embeddedPfx.Count -eq 0) { Pass "Extracted server installer contains no PFX" } else { Fail "Extracted server installer contains a PFX" }
    } catch {
        Fail "Could not extract server installer with innounp: $($_.Exception.Message)"
    } finally {
        if (Test-Path -LiteralPath $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }
    }
} else {
    if ($manifest -and $clientHash) {
        Pass "Matching embedded client validated from exact pre-package staging (Inno extraction unavailable)"
    } else {
        Fail "Could not validate pre-package client staging"
    }
}

Write-Host ""
if ($failed -ne 0) {
    Write-Host "$failed installer validation check(s) failed." -ForegroundColor Red
    exit $failed
}
Write-Host "ALL INSTALLER CHECKS PASSED" -ForegroundColor Green
