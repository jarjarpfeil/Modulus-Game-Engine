# Compile SpaceEscape assets for mod-assets
# This script builds the SpaceEscape sample and copies compiled assets to mod-assets

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$SpaceEscapeDir = Join-Path $Root "..\..\..\samples\Games\SpaceEscape"
$ModAssetsDir = Join-Path $Root "mod-assets"
$OutputAssetsDir = Join-Path $ModAssetsDir "assets"

Write-Host "=== Compiling SpaceEscape Assets for Mod ===" -ForegroundColor Cyan

if (-not (Test-Path $SpaceEscapeDir)) {
    Write-Error "SpaceEscape sample not found at: $SpaceEscapeDir"
    exit 1
}

Write-Host "SpaceEscape directory: $SpaceEscapeDir"

# Build the SpaceEscape project to generate compiled assets
$ProjectFile = Join-Path $SpaceEscapeDir "SpaceEscape.Windows.csproj"
if (Test-Path $ProjectFile) {
    Write-Host "Building SpaceEscape project..." -ForegroundColor Yellow
    dotnet build $ProjectFile -p:StrideNativeWindowsArm64Enabled=false -c Debug

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build SpaceEscape project"
        exit 1
    }

    # Copy compiled assets from build output
    $BuildOutputDir = Join-Path $SpaceEscapeDir "bin\Debug\Assets"
    if (Test-Path $BuildOutputDir) {
        Write-Host "Copying compiled assets from: $BuildOutputDir" -ForegroundColor Green

        if (Test-Path $OutputAssetsDir) {
            Remove-Item $OutputAssetsDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $OutputAssetsDir -Force | Out-Null

        Copy-Item -Path "$BuildOutputDir\*" -Destination $OutputAssetsDir -Recurse -Force
        Write-Host "Copied compiled assets to: $OutputAssetsDir" -ForegroundColor Green
    } else {
        Write-Warning "Build output not found at: $BuildOutputDir"
        Write-Warning "Assets may need to be compiled through Game Studio"
    }
} else {
    Write-Error "SpaceEscape project file not found: $ProjectFile"
    exit 1
}

Write-Host "`n=== Compilation Complete ===" -ForegroundColor Cyan
