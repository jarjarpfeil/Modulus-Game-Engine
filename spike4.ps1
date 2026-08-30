# Spike test script - deploy engine, create mod, launch game
$ErrorActionPreference = "Stop"

# Step 1: Stop game and deploy engine
Stop-Process -Name "MyGame2.Windows" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$engineSrc = "D:\Modulus-Game-Engine\sources\engine\Stride.Engine\bin\Debug\net10.0\Stride.Engine.dll"
$engineDst = "D:\TestGame\MyGame2\Bin\Windows\Debug\Stride.Engine.dll"
if (Test-Path $engineSrc) {
    Copy-Item $engineSrc $engineDst -Force
    Write-Output "Deployed Stride.Engine.dll (size: $((Get-Item $engineDst).Length) bytes)"
} else {
    Write-Output "ERROR: Engine DLL not found at $engineSrc"
    exit 1
}

# Step 2: Create mod directory and mod.json
$modDir = "D:\TestGame\MyGame2\Bin\Windows\Debug\mods\mod-spike-script"
$modBinDir = Join-Path $modDir "bin\Debug"
New-Item -ItemType Directory -Path $modBinDir -Force | Out-Null

$modJson = @{
    id = "com.modulus.spike.script"
    name = "Spike mod-assembly ScriptComponent"
    version = "1.0.0"
    apiVersion = "1.0"
    author = "spike"
    description = "Throwaway spike shipping SpaceEscape.Game.dll so ScriptSystem can attempt to resolve scene ScriptComponent type references from a mod assembly."
    entryPoint = $null
    components = @()
    systems = @()
    scenes = @()
    assets = @()
    loadOrder = 50
    tags = @("spike")
    type = "standard"
}
$modJson | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $modDir "mod.json") -Encoding UTF8
Write-Output "Created mod.json"

# Step 3: Copy SpaceEscape.Game.dll and .pdb
$seDll = "D:\TestGame\SpaceEscape\Bin\Windows\Debug\SpaceEscape.Game.dll"
$sePdb = "D:\TestGame\SpaceEscape\Bin\Windows\Debug\SpaceEscape.Game.pdb"
if (Test-Path $seDll) {
    Copy-Item $seDll (Join-Path $modBinDir "SpaceEscape.Game.dll") -Force
    if (Test-Path $sePdb) {
        Copy-Item $sePdb (Join-Path $modBinDir "SpaceEscape.Game.pdb") -Force
    }
    Write-Output "Copied SpaceEscape.Game.dll + .pdb"
} else {
    Write-Output "ERROR: SpaceEscape.Game.dll not found at $seDll"
    exit 1
}

# Verify mod structure
Write-Output "Mod structure:"
Get-ChildItem -Recurse $modDir | ForEach-Object { Write-Output "  $($_.FullName)" }

# Step 4: Launch game with stdout capture
$logPath = "C:\Users\jarja\AppData\Local\Temp\kilo\spike4-run.log"
$errPath = "C:\Users\jarja\AppData\Local\Temp\kilo\spike4-err.log"
$gameExe = "D:\TestGame\MyGame2\Bin\Windows\Debug\MyGame2.Windows.exe"
$gameDir = "D:\TestGame\MyGame2\Bin\Windows\Debug"

if (Test-Path $gameExe) {
    $proc = Start-Process -FilePath $gameExe -WorkingDirectory $gameDir -RedirectStandardOutput $logPath -RedirectStandardError $errPath -PassThru -WindowStyle Normal
    Write-Output "Launched game, PID: $($proc.Id)"
    Start-Sleep -Seconds 15
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    
    $logSize = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }
    $errSize = if (Test-Path $errPath) { (Get-Item $errPath).Length } else { 0 }
    Write-Output "STDOUT: $logSize bytes / STDERR: $errSize bytes"
} else {
    Write-Output "ERROR: Game exe not found at $gameExe"
    exit 1
}

# Step 5: Copy log to .txt for reading
if (Test-Path $logPath) {
    Copy-Item $logPath "C:\Users\jarja\AppData\Local\Temp\kilo\spike4-run.txt" -Force
    Write-Output "Copied log to spike4-run.txt"
}

# Step 6: Cleanup mod
Remove-Item -Recurse -Force $modDir -ErrorAction SilentlyContinue
Write-Output "Cleaned up mod directory"

Write-Output "=== SPIKE TEST COMPLETE ==="
