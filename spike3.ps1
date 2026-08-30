$ErrorActionPreference = 'Continue'

# Step 1: Deploy engine
Stop-Process -Name "MyGame2.Windows" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Copy-Item "D:\Modulus-Game-Engine\sources\engine\Stride.Engine\bin\Debug\net10.0\Stride.Engine.dll" "D:\TestGame\MyGame2\Bin\Windows\Debug\Stride.Engine.dll" -Force
$engSize = (Get-Item 'D:\TestGame\MyGame2\Bin\Windows\Debug\Stride.Engine.dll').Length
Write-Host "Deployed updated Stride.Engine.dll (size: $engSize bytes)"

# Step 2: Create spike mod directory
$modDir = "D:\TestGame\MyGame2\Bin\Windows\Debug\mods\mod-spike-script"
$binDir = Join-Path $modDir "bin\Debug"
New-Item -ItemType Directory -Path $binDir -Force | Out-Null

# Step 3: Create mod.json (no apostrophes)
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
} | ConvertTo-Json -Depth 5

Set-Content -Path (Join-Path $modDir "mod.json") -Value $modJson -Encoding UTF8

# Step 4: Copy SpaceEscape.Game.dll and pdb
Copy-Item "D:\TestGame\SpaceEscape\Bin\Windows\Debug\SpaceEscape.Game.dll" (Join-Path $binDir "SpaceEscape.Game.dll") -Force
Copy-Item "D:\TestGame\SpaceEscape\Bin\Windows\Debug\SpaceEscape.Game.pdb" (Join-Path $binDir "SpaceEscape.Game.pdb") -Force

# Step 5: Verify mod structure
Write-Host "`n=== MOD DIRECTORY STRUCTURE ==="
Get-ChildItem -Recurse $modDir | ForEach-Object {
    Write-Host $_.FullName
}

Write-Host "`n=== MOD JSON ==="
Get-Content (Join-Path $modDir "mod.json")

Write-Host "`n=== STEP 1-2 COMPLETE ==="
