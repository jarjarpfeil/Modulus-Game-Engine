#!/usr/bin/env python3
"""Write step1.ps1 to temp directory."""
import os

content = """Stop-Process -Name 'MyGame2.Windows' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$src = 'D:\\TestGame\\SpaceEscape\\Bin\\Windows\\Debug\\data\\db'
$dst = 'D:\\TestGame\\MyGame2\\Bin\\Windows\\Debug\\mods\\mod-assets\\assets'

if (-not (Test-Path $src)) {
    Write-Output 'BLOCKED: source SpaceEscape data/db/ missing'
    exit 1
}
Write-Output 'Source SpaceEscape db exists, files:'
Get-ChildItem $src -Recurse | Select-Object Name, Length | Format-Table -AutoSize

$srcIndex = Join-Path $src 'index'
if (Test-Path $srcIndex) {
    Write-Output ('Source index size: ' + (Get-Item $srcIndex).Length)
    Select-String -Path $srcIndex -Pattern 'Scene.sdscene' -SimpleMatch | ForEach-Object { Write-Output ('  ' + $_.Line) }
}

if (Test-Path $dst) {
    Remove-Item $dst -Recurse -Force
    Write-Output 'Deleted stale assets directory'
}

New-Item -ItemType Directory -Path $dst -Force | Out-Null
Copy-Item -Path ($src + '\\*') -Destination $dst -Recurse -Force
Write-Output 'Copied SpaceEscape data/db/ to mod-assets/assets'

$scenePath = Join-Path $dst '86\\3a8cc7cefc7ca8169b2d4feccaf643'
if (Test-Path $scenePath) {
    Write-Output ('Standalone scene binary at: ' + $scenePath + ', size: ' + (Get-Item $scenePath).Length + ' bytes (target: >789)')
} else {
    Write-Output 'Standalone scene binary NOT FOUND - checking bundle'
    $bundle = Join-Path $dst 'bundles\\default.50bfde97cf23adde754598792d52e4a2.bundle'
    if (Test-Path $bundle) {
        Write-Output ('Bundle exists, size: ' + (Get-Item $bundle).Length + ' bytes')
    } else {
        Write-Output 'BLOCKED: no standalone scene binary AND no bundle'
        exit 1
    }
}
"""

outdir = r"C:\Users\jarja\AppData\Local\Temp\kilo"
os.makedirs(outdir, exist_ok=True)
with open(os.path.join(outdir, "step1.ps1"), "w") as f:
    f.write(content)
print("step1.ps1 written")
