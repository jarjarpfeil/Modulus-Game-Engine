$ErrorActionPreference = "Stop"
Stop-Process -Name "MyGame2.Windows" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Deploy updated engine
Copy-Item "D:\Modulus-Game-Engine\sources\engine\Stride.Engine\bin\Debug\net10.0\Stride.Engine.dll" "D:\TestGame\MyGame2\Bin\Windows\Debug\Stride.Engine.dll" -Force
"Deployed updated Stride.Engine.dll"

# Create mod directory structure
New-Item -ItemType Directory -Force -Path "D:\TestGame\MyGame2\Bin\Windows\Debug\mods\mod-spike-script\bin\Debug" | Out-Null

# Copy mod files
Copy-Item "D:\TestGame\SpaceEscape\Bin\Windows\Debug\SpaceEscape.Game.dll" "D:\TestGame\MyGame2\Bin\Windows\Debug\mods\mod-spike-script\bin\Debug\SpaceEscape.Game.dll" -Force
Copy-Item "D:\TestGame\SpaceEscape\Bin\Windows\Debug\SpaceEscape.Game.pdb" "D:\TestGame\MyGame2\Bin\Windows\Debug\mods\mod-spike-script\bin\Debug\SpaceEscape.Game.pdb" -Force
Copy-Item "D:\Modulus-Game-Engine\mod-spike-script\mod.json" "D:\TestGame\MyGame2\Bin\Windows\Debug\mods\mod-spike-script\mod.json" -Force

# Verify
Get-ChildItem -Recurse "D:\TestGame\MyGame2\Bin\Windows\Debug\mods\mod-spike-script"
"Mod setup complete"
