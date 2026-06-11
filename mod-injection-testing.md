# Mod Injection Testing — Summary

## Overview

This document summarizes the work done on the Modulus Engine's mod injection system — the ability to drop mods into a game's `mods/` directory and have them automatically loaded, with cross-mod asset resolution.

---

## What Was Built

### 1. Mod Scene Behavior (Phase 9)

Mods can declare scenes in `mod.json` with optional load behavior:

```json
{
  "scenes": [
    { "path": "assets/DungeonLevel", "name": "Dark Dungeon", "behavior": "additive" },
    { "path": "assets/HubWorld" }
  ]
}
```

Behaviors: `MenuSelect` (default/player-choice), `Replace`, `Additive`, `Background`.

**Files:**
- `Modulus.Modding.Api/ModSceneLoadBehavior.cs` — Enum (stable ABI)
- `Stride.Engine/Modding/ModManifest.cs` — Added `ModSceneDeclaration` class + `scenes` field
- `Stride.Engine/Modding/ModSceneManager.cs` — Catalog queries + scene loading/unloading

### 2. Mod Auto-Loading

`Game.Initialize()` now adds `ModAutoLoadSystem` — a one-shot `GameSystemBase` that calls `ModHost.LoadAllMods()` on the first frame. Zero game code required.

If no mod scene loads successfully, the auto-loader creates a fallback scene with camera, directional light, and a green ground plane.

**Files:**
- `Stride.Engine/Modding/ModAutoLoadSystem.cs` — Auto-discovery + fallback scene creation
- `Stride.Engine/Engine/Game.cs` — Adds `ModAutoLoadSystem` in `Initialize()`

### 3. Cross-Mod Asset Resolution Architecture

When a mod's scene references an asset from another mod, the engine resolves it automatically through a composite provider chain.

**How it works:**
1. Each mod's `ContentIndexMap` entries are merged into the game's primary `ContentIndexMap`
2. `ContentManager` has a `CompositeProvider` property pointing to the composite chain
3. When the primary provider doesn't have an asset, `TryLoadFromComposite` iterates all mod providers
4. The provider that has the data serves the stream

**Files:**
- `Stride.Core.Serialization/Contents/ContentManager.cs` — Added `CompositeProvider` property + `TryLoadFromComposite` fallback method
- `Stride.Engine/Modding/ModContentManager.cs` — Wires `CompositeProvider`, merges indices, mounts VFS providers for mod asset directories

### 4. NuGet Package Rename

`Stride.Engine.csproj` now has `<PackageId>Modulus.Engine</PackageId>`. The NuGet package is `Modulus.Engine.nupkg` — no ambiguity with vanilla Stride. The DLL name stays `Stride.Engine.dll` (namespace unchanged) because `InternalsVisibleTo("Stride.Engine")` in other assemblies would break.

**Files:**
- `Stride.Engine/Stride.Engine.csproj` — Added `<PackageId>Modulus.Engine</PackageId>`
- Editor templates updated to reference `Modulus.Engine`
- TestGame's `MyGame.csproj` uses `<PackageReference Include="Modulus.Engine" Version="4.4.0.2" />`

### 5. ModAssetBuilder Tool

A build tool that creates compiled `ObjectDatabase` files for mod assets. Creates the `index` file (URL→ObjectId mappings) and data files in the correct directory structure (`<root>/<id[0:2]>/<id[2:]>`).

**Files:**
- `tests/integration/space-escape-mods/ModAssetBuilder/Program.cs`
- `tests/integration/space-escape-mods/ModAssetBuilder/ModAssetBuilder.csproj`

---

## Bugs Fixed

| Bug | Fix | File |
|-----|-----|------|
| Relative path crash (`"mods\mod-rendering\..."` is not absolute) | `ModDiscovery.Discover()` now resolves to absolute path | `ModDiscovery.cs` |
| Camera slot mismatch (no camera assigned to Slot[Main]) | Fallback scene wires camera to compositor's first slot | `ModAutoLoadSystem.cs` |
| Ground plane invisible (rotated into a wall) | Removed incorrect `RotationX(-PiOverTwo)` — quad was already flat | `ModAutoLoadSystem.cs` |
| `ObjectDatabase` can't resolve mod asset path | Mount VFS provider via `VirtualFileSystem.MountFileSystem()` | `ModContentManager.cs` |
| Raw assets directory crash (no compiled index) | Check for `index` file before creating ObjectDatabase | `ModContentManager.cs` |
| UTF-8 BOM in index file | `ModAssetBuilder` writes without BOM | `ModAssetBuilder/Program.cs` |
| ContentIndexMap merge not working | Moved `CompositeProvider` wiring to `RegisterGameProvider()` (called after ContentManager exists) | `ModContentManager.cs`, `Game.cs` |
| Composite fallback only triggers when `FileExists` returns false | Added `TryLoadFromComposite` call after `OpenStream` returns null | `ContentManager.cs` |

---

## Current State (as of 2026-06-10)

### Working ✅
- All 6 SpaceEscape mods load in the blank TestGame (6/6, 0 errors)
- ModAutoLoadSystem discovers and loads mods automatically
- ModSceneManager catalogs scenes and resolves behaviors
- ContentIndexMap merge (6 entries from mod-assets into game index)
- ObjectDatabase created from VFS-mounted mod asset directory
- NuGet package `Modulus.Engine` resolves correctly
- 1635+ tests pass (0 failures)
- **ModCompiler produces real Stride-serialized assets** (ChunkHeader + MurmurHash3 ObjectId)
- **Editor "Compile Assets" button** in Mod Manager panel
- **CLI tool** (`Modulus.ModCompilerApp`) for command-line compilation

### Mod Compilation (NEW — 2026-06-10)

The previous `ModAssetBuilder` produced fake data (SHA1 hashes, plain UTF-8 strings without ChunkHeader). This caused `ContentManager.TryLoadFromComposite` to fail because `ChunkHeader.Read()` found no "CHNK" magic header.

**Fix:** Created `Modulus.ModCompiler` — a proper mod asset compiler that:
- Uses `ContentManager.Save()` to serialize assets with real ChunkHeader format
- Computes MurmurHash3 ObjectIds via `DigestStream`/`ObjectIdBuilder`
- Produces correct `index` file + `{id[0:2]}/{id[2:]}` data file layout
- Reads `mod.json` to discover assets (scenes + general assets)

**Tools:**
- `sources/tools/Modulus.ModCompiler/` — Library (`ModCompilerService`)
- `sources/tools/Modulus.ModCompilerApp/` — CLI (`modcompiler <mod-dir> [--output <dir>]`)
- Editor integration: "🔨" button in Mod Manager panel
- `tests/integration/space-escape-mods/ModAssetBuilder/` — Now wraps `ModCompilerService`

### Not Yet Verified ⚠️
- **End-to-end scene loading**: The compiled assets now have real ChunkHeader + MurmurHash3, but haven't been tested with a running game instance yet. The `TryLoadFromComposite` code path should now work since the data is properly formatted.

### What's Needed Next
1. Test end-to-end scene loading with a running game instance (ModReassemblyHost)
2. Verify `TryLoadFromComposite` successfully deserializes the ChunkHeader-formatted data
3. Create a real compiled scene with visible content (not just camera+light)
4. Support compiling from source `.sdscene` files (via PackageBuilder pipeline)

---

## Test Game Setup

**Location**: `D:\TestGame\MyGame\Bin\Windows\Debug\`

**Blank game**: `MyGameApp.cs` is just:
```csharp
using Stride.Engine;
using var game = new Game();
game.Run();
```

**Mods directory**: `mods/` contains 6 SpaceEscape mods:
- `mod-assets` (loadOrder 1) — Scene files + compiled ObjectDatabase
- `mod-rendering` (loadOrder 5) — Custom shaders
- `mod-character` (loadOrder 10) — Character component + processor
- `mod-background` (loadOrder 20) — Level generation
- `mod-ui` (loadOrder 30) — UI screens
- `mod-chaos` (loadOrder 999) — Adversarial test mod (intentionally throws)

**Build**: `dotnet build MyGame.Windows/MyGame.Windows.csproj -c Debug` (in `D:\TestGame\MyGame\`)
**Package source**: `D:\Modulus-Game-Engine\bin\packages\` (via `nuget.config`)

---

## Key Architecture Decisions

1. **PackageId only (not AssemblyName)**: `Modulus.Engine` NuGet package, `Stride.Engine.dll` DLL. Avoids `InternalsVisibleTo` breakage.
2. **Auto-load on first frame**: `ModAutoLoadSystem` runs once, loads all mods, then disables itself.
3. **VFS mounting for mod assets**: Each mod's `assets/` directory is mounted as a VFS provider at `/mod-assets-<modId>`.
4. **Composite fallback in ContentManager**: When primary provider doesn't have data, iterate composite providers to find it.
5. **Index merging**: Mod ContentIndexMap entries are merged into the game's primary index so `ContentManager.Exists()` finds mod URLs.

---

## Files Modified (Summary)

| File | Changes |
|------|---------|
| `Modulus.Modding.Api/ModSceneLoadBehavior.cs` | NEW — Scene behavior enum |
| `Stride.Engine/Modding/ModManifest.cs` | Added `ModSceneDeclaration`, `scenes` field |
| `Stride.Engine/Modding/ModSceneManager.cs` | NEW — Scene catalog + loading |
| `Stride.Engine/Modding/ModAutoLoadSystem.cs` | NEW — Auto-load + fallback scene |
| `Stride.Engine/Modding/ModDiscovery.cs` | Absolute path resolution |
| `Stride.Engine/Modding/ModContentManager.cs` | VFS mounting, index merge, CompositeProvider wiring |
| `Stride.Engine/Modding/ModHost.cs` | Added SceneManager, RegisterModScenes, UnregisterModScenes |
| `Stride.Engine/Engine/Game.cs` | ModAutoLoadSystem + RegisterGameProvider call |
| `Stride.Core.Serialization/Contents/ContentManager.cs` | CompositeProvider + TryLoadFromComposite |
| `Stride.Engine/Stride.Engine.csproj` | `<PackageId>Modulus.Engine</PackageId>` |
| Editor templates | Updated PackageReference to `Modulus.Engine` |
| `tests/integration/space-escape-mods/ModAssetBuilder/` | Rewritten — now wraps `ModCompilerService` |
| `tests/integration/space-escape-mods/mod-*/mod.json` | Added `scenes`, `dependencies` |
| `sources/tools/Modulus.ModCompiler/` | NEW — Mod asset compiler library (ChunkHeader + MurmurHash3) |
| `sources/tools/Modulus.ModCompilerApp/` | NEW — CLI tool for mod asset compilation |
| `sources/editor/Stride.Modding.Editor/ModManagerViewModel.cs` | Added CompileModAssets command |
| `sources/editor/Stride.Modding.Editor/ModManagerView.xaml` | Added Compile Assets toolbar button |
| `sources/editor/Stride.Modding.Editor/Stride.Modding.Editor.csproj` | Added ModCompiler reference |
