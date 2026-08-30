# Modulus Engine Mod Template

This template creates a Modulus Engine mod project with:
- `mod.json` manifest with all required fields
- `ModEntry.cs` implementing `IMod` (the stable ABI)
- `.csproj` referencing `Modulus.Mod.Sdk` (provides MSBuild targets, AssetCompiler, verification, packaging)
- `Assets/com.example.mymod/` namespaced subdirectory for asset URL safety

## Usage

```bash
# Install the template (once)
dotnet new install Modulus.Templates.Mod

# Create a new mod
dotnet new modulus-mod -n MyMod

# Build (compiles C# + assets + verifies + packages into .modpkg)
cd MyMod
dotnet build
```

The `.modpkg` file appears in `bin/Debug/MyMod.modpkg`.

## Options

- `-n MyMod` — project name (also used as AssemblyName)
- `--ModId com.example.mymod` — mod ID (kebab-case, used for asset URL virtualization)
- `--ModType standard` — mod type: `standard` (code+assets), `data` (assets only), `patch` (patches another mod)

## Asset URL Virtualization

Assets placed in `Assets/com.example.mymod/` resolve to virtual URLs like `com.example.mymod/Scene`.
This prevents collisions with the game's own assets (e.g., `Scene`).

To intentionally override a game asset at the same URL, add it to `explicitOverrides` in `mod.json`:
```json
"explicitOverrides": ["Scene", "Ground Material"]
```
