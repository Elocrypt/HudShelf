<#
.SYNOPSIS
    Packages HudShelf into a release zip ready for ModDB upload.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Version
    Version string for the zip filename (e.g. "0.3.0").
    If omitted, reads from modinfo.json.

.EXAMPLE
    ./build/package.ps1 -Configuration Release -Version 0.3.0
#>
param(
    [string]$Configuration = "Release",
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$binDir = Join-Path $repoRoot "bin/$Configuration"
$distDir = Join-Path $repoRoot "build/dist"

# modinfo.json is read from the repo root, not bin/. The bin copy is
# only refreshed on build, so packaging right after a version bump
# would otherwise ship the previous version's modinfo.
$modinfoPath = Join-Path $repoRoot "modinfo.json"
$modinfo = Get-Content $modinfoPath -Raw | ConvertFrom-Json

if (-not $Version) {
    $Version = $modinfo.version
    Write-Host "Version from modinfo.json: $Version"
}
elseif ($Version -ne $modinfo.version) {
    Write-Error "Requested version '$Version' does not match modinfo.json version '$($modinfo.version)'."
    exit 1
}

# Verify the build output exists.
$dllPath = Join-Path $binDir "HudShelf.dll"

if (-not (Test-Path $dllPath)) {
    Write-Error "Build output not found at '$dllPath'. Run 'dotnet build -c $Configuration' first."
    exit 1
}

# Refuse to package a stale DLL. FileVersion comes from <Version> in
# HudShelf.csproj (x.y.z -> x.y.z.0).
$dllVersion = (Get-Item $dllPath).VersionInfo.FileVersion
if ($dllVersion -ne "$Version.0") {
    Write-Error "HudShelf.dll FileVersion is '$dllVersion', expected '$Version.0'. Bump <Version> in HudShelf.csproj and rebuild."
    exit 1
}

# Create dist directory.
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

# Stage files in a temp directory matching the mod folder structure.
$stageName = "HudShelf"
$stageDir = Join-Path ([System.IO.Path]::GetTempPath()) "hudshelf-package-$([System.Guid]::NewGuid().ToString('N'))"
$modDir = Join-Path $stageDir $stageName

New-Item -ItemType Directory -Path $modDir -Force | Out-Null

# Copy required files.
Copy-Item $dllPath $modDir
Copy-Item $modinfoPath $modDir

# Copy modicon if it exists.
$modiconSrc = Join-Path $repoRoot "modicon.png"
if (Test-Path $modiconSrc) {
    Copy-Item $modiconSrc $modDir
}

# Create the zip.
$zipName = "HudShelf_${Version}.zip"
$zipPath = Join-Path $distDir $zipName

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$modDir/*" -DestinationPath $zipPath -CompressionLevel Optimal

$zipSize = (Get-Item $zipPath).Length
Write-Host ""
Write-Host "Packaged: $zipPath ($([math]::Round($zipSize / 1024, 1)) KB)"
Write-Host "Contents:"
Get-ChildItem $modDir -Recurse -File | ForEach-Object {
    Write-Host "  $($_.FullName.Replace($modDir, ''))"
}

# Cleanup.
Remove-Item $stageDir -Recurse -Force -ErrorAction SilentlyContinue