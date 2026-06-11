# Modulus Engine — Agent Context

> **Always-loaded context for any AI agent working in this repository.**
> This file is automatically picked up by Kilo, Claude, and other compatible agents.
> Keep it lean. Pointers over content.

## What is this project?

**Modulus Engine** is a modding-focused game engine forked from [Stride](https://stride3d.net/).
It adds an AssemblyLoadContext (ALC) isolation layer so that mods written in C# can be
hot-swapped at runtime and shared across multiple games built on the engine.

- **Repo:** `D:\Modulus-Game-Engine\` (git-bash: `/d/modulus-game-engine/`)
- **Upstream:** `stride3d/stride` (full fork, ~120+ projects preserved)
- **PackageId:** `Modulus.Engine` (NuGet) — **DLL assembly name stays `Stride.Engine.dll`**
- **Stable ABI:** `Modulus.Modding.Api` (everything else is internal)

## Tech stack

| Layer | Tech |
|---|---|
| Language | C# 12+ |
| Runtime | .NET 10 |
| GPU | Vulkan 1.4.350.0 (target: AMD RX 6950 XT, 4K @ 150% DPI) |
| Editor | WPF (Game Studio) |
| OS | Windows required (Game Studio, asset pipeline) |
| Tests | xUnit |

## Project layout

```
D:\Modulus-Game-Engine\
├── sources/
│   ├── core/                  # Stride.Core, Stride.Core.Serialization, etc.
│   ├── engine/                # Stride.Engine (ECS, Scene, components, Modding/)
│   ├── graphics/              # Stride.Graphics (Vulkan/D3D abstraction)
│   ├── rendering/             # Stride.Rendering (render pipeline, materials)
│   ├── editor/                # Stride.GameStudio (WPF) + Stride.Modding.Editor
│   ├── assets/                # Asset pipeline
│   ├── presentation/          # Stride.UI (WPF-based UI)
│   ├── tools/                 # AssetCompiler, etc.
│   │   └── ModulusEngine.MCPServer/   # C# MCP server (HTTP → engine)
│   └── templates/             # dotnet new templates
├── build/                     # Solution + solution filters
│   ├── Stride.sln             # full solution
│   ├── Stride.Tests.Simple.slnf
│   └── Stride.Runtime.slnf    # fast subset
├── tests/
├── docs/                      # Internal Stride docs
├── docs-site/                 # Modulus DocFX site (docs-site/articles/...)
└── bin/packages/              # Local NuGet feed (Modulus.Engine.nupkg)
```

## Build, test, run

**CRITICAL:** Every `dotnet build` and `dotnet test` in this repo must pass:

```
-p:StrideNativeWindowsArm64Enabled=false
```

ARM64 native linking fails without the MSVC ARM64 toolset. The full build is:

```bash
# from D:\Modulus-Game-Engine (in git-bash: /d/modulus-game-engine)
dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false
dotnet test  build/Stride.Tests.Simple.slnf -p:StrideNativeWindowsArm64Enabled=false --no-build
```

Currently: **0 errors, 180+ modding tests pass, 1635+ Stride tests pass.**

## Modding (the unique value-add)

Hot path: `sources/engine/Stride.Engine/Modding/`

| File / area | Purpose |
|---|---|
| `ModHost.cs` | Lifecycle manager; auto-created by `Game()` constructor |
| `ModLoadContext.cs` | Collectible ALC per mod |
| `ModAutoLoadSystem.cs` | One-shot `GameSystemBase` — `ModHost.LoadAllMods()` on first Update |
| `ModLoadOrderResolver.cs` | Topological sort + DFS cycle detection |
| `ModPackageManager.cs` | `.modpkg` ZIP install/uninstall with metadata tracking |
| `ModContentManager.cs` | Multi-source content resolution (incl. GUID injection) |
| `ModEventBus.cs` (+ `ModEventBusProxy.cs`) | Inter-mod pub/sub (auto-tagged with modId) |
| `ModShaderManager.cs` | Mod shader registration with `EffectSystem` |
| `OrphanComponentHandler.cs` | Save-compat wrapper for unknown component types |
| `Api/IMod*.cs` | Stable ABI interfaces (`Modulus.Modding.Api`) |

**Phase status:** 0–9 complete. Total: 192 modding tests passing.

### Mod auto-loading

Any host app (`new Game(); game.Run();`) gets auto-mod-loading. `Game()` ctor creates
`ModHost` and registers it as a service. `Game.Initialize()` adds `ModAutoLoadSystem`
which scans `mods/` and calls `ModHost.LoadAllMods()` on first Update. Zero game code needed.

### Mod project recipe (the only working pattern)

Every mod `.csproj` MUST have:
1. `<PackageId>Modulus.Mod</PackageId>`-style, but `AssemblyName` matches DLL.
2. `PrivateAssets="all"` on `<ProjectReference>` to `Stride.Engine.csproj` (and other
   Stride assemblies the mod touches). This prevents transitive `Stride.*.dll` from
   copying into the mod's bin folder.
3. `RemoveStrideDlls` post-build target that explicitly `Delete` any `Stride.*.dll`
   that snuck into `$(OutDir)`. Without this, mod types inherit a mod-local
   `EntityComponent` and engine rejects them as type mismatches.
4. `freetype.dll` (native) must be in `runtimes/win-x64/native/` of the host output
   (font system needs it).

### Mod runtime constraints

- Mods are **managed C# only**. No native DLLs in `.modpkg`.
- `Modulus.Modding.Api` is the **only stable ABI**. Other assemblies are internal.
- `IModEventBus` does NOT expose `SubscriptionCount` — cast to concrete `ModEventBus`.
- `EntityProcessor<T>` has no `.Entity` — use `ComponentDatas.First().Key.Entity`.
- `EntityTransformExtensions.AddChild/RemoveChild` are extension methods, not instance
  methods on `Entity`. Use static call when there is namespace conflict.
- `SceneInstance` inherits `EntityManager` (which is `IReadOnlySet<Entity>`) — use
  `scene.Add(...)` / `scene.Remove(...)` / `scene.Count` / `scene.FirstOrDefault()`,
  **not** `scene.Entities`.
- `EffectSystem` is in `Stride.Rendering` (not `Stride.Effects`).
- `ContentManager` is in `Stride.Core.Serialization.Contents`.
- `GraphicsDevice` is in `Stride.Graphics`.
- `IContentManager` is in `Stride.Core.Serialization.Contents`.
- `PlayingAnimation` is in `Stride.Animations`.
- `DefaultEntityComponentProcessorAttribute` uses `.TypeName` (string), NOT `.ProcessorType`.
- `DataContractAttribute.Alias` is the alias field (not `.Name`).
- `MicroThreadState` has no `Waiting` value.

### ALC unload — the hardest problem

To unload a mod cleanly, ALL of the following must be done, in this order:

1. Cancel `MicroThread` entries whose `.Action.Method.DeclaringType.Assembly == modAssembly`
   (query via `ScriptSystem.Scheduler`).
2. `DataSerializerFactory.ClearAssemblySerializers(modAssembly)` (clears ALL aliases).
3. `AssemblyRegistry.Unregister(modAssembly)`.
4. Remove all mod `EntityProcessor` instances from `EntityManager`.
5. Destroy all entities with mod components.
6. Unsubscribe all event handlers from mod types.
7. Call `mod.Dispose()` on all `IMod` instances.
8. Null cached `MethodInfo`/`PropertyInfo` for mod types.
9. `AssemblyLoadContext.Unload()` then **null the `ModLoadContext` reference itself**.
10. `GC.Collect()` + `GC.WaitForPendingFinalizers()` — run 2–3 cycles.
11. Verify with `WeakReference` (the 100-cycle memory test is the authoritative leak test;
    xUnit `[Fact]`s with ALC `WeakReference` are unreliable in parallel test runs).

A single leaked reference → that mod's DLL never collects → repeated load/unload
leaks one full copy of the DLL per cycle.

## HTTP API + MCP

The engine embeds an HTTP server on **`http://localhost:9876`** (see
`sources/engine/Stride.Engine/HttpApi/`). All HTTP handlers must marshal work to the
game thread via `HttpApiSystem` (two queues: `_updateQueue` for ECS, `_drawQueue` for GPU).

**The MCP server (`tools/ModulusEngine.MCPServer/`) calls these endpoints.** It only
works while Game Studio (or a Game host) is running.

Available endpoints (see `MODULUS-ENGINE-PLAN.md` for the full table):

```
GET  /api/v1/status
GET  /api/v1/editor/status
POST /api/v1/editor/launch
POST /api/v1/editor/screenshot
GET  /api/v1/scene/entities
POST /api/v1/scene/entities
GET  /api/v1/scene/scenes
GET  /api/v1/debug/logs
GET  /api/v1/debug/console
GET  /api/v1/mod/list          (Phase 3+)
POST /api/v1/mod/install       (Phase 6)
... etc
```

## Workspace rules (from USER.md)

1. **Read logs directly** from `D:\Project-Modulus-Game-Engine\editor-*.log`,
   `pipeline-debug.log`. Don't ask the user to copy-paste.
2. **Don't make the user test manually.** Launch editor/API from git-bash, use
   `vision_analyze` on screenshots, fix, repeat.
3. **Targeted, verifiable fixes.** Understand the root cause first. No
   "fix → test → still broken → guess again" loops.
4. **Small, incremental changes.** Never bundle multiple changes into one commit.
5. **DPI-scaled physical pixels** for all Win32 child window math: multiply by
   `TopLevel.RenderScaling` (user display is 4K @ 150% DPI = 1.5×).
6. **Pragmatic renaming:** "any system that we have actively changed needs to be
   rebranded, if its still the same package we leave it alone."

## Where to find more

- **High-level plan:** `MODULUS-ENGINE-PLAN.md` (10 phases, 0–9 done)
- **Session log:** `MEMORY.md` (chronological decisions and gotchas)
- **User prefs:** `USER.md`
- **Stride repo context:** `.github/copilot-instructions.md`
- **Detailed gotchas:** load the `stride-engine-development` skill
- **Modding API surface:** `docs-site/articles/modding/` (writing-a-mod, mod-api-reference, architecture)
- **MCP setup:** `docs-site/articles/mcp-server/setup.md` and `tool-reference.md`
- **Stride modifications:** `docs-site/articles/development/stride-modifications.md`
- **Strider internal docs:** `docs/`

## Quick command reference

```bash
# build full engine
dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false

# run simple test suite (fast, no Graphics/Physics/Audio)
dotnet test build/Stride.Tests.Simple.slnf -p:StrideNativeWindowsArm64Enabled=false --no-build

# run only modding tests
dotnet test build/Stride.Tests.Simple.slnf -p:StrideNativeWindowsArm64Enabled=false --no-build \
  --filter "FullyQualifiedName~Modding"

# launch Game Studio
dotnet run --project sources/editor/Stride.GameStudio/Stride.GameStudio.csproj

# pack the NuGet (produces bin/packages/Modulus.Engine.nupkg)
dotnet pack sources/engine/Stride.Engine/Stride.Engine.csproj -p:StrideNativeWindowsArm64Enabled=false

# build & run the MCP server
dotnet run --project tools/ModulusEngine.MCPServer/ModulusEngine.MCPServer.csproj

# delete locked ssdeps files (Windows file lock workaround)
find . -name "*.ssdeps" -delete
```
