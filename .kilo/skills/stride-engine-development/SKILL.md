---
name: stride-engine-development
description: Detailed gotchas, API traps, and proven patterns for working in the Modulus/Stride codebase. Load this when modifying Stride.Engine, writing mods, debugging ECS issues, dealing with ALC isolation, or touching anything under sources/.
---

# Stride / Modulus Engine Development Skill

This skill consolidates the non-obvious knowledge accumulated across many sessions
of working in this repo. **Read this BEFORE touching ECS, ALC, or mod code.**

## 1. Build & Test Invariants

- **Always pass** `-p:StrideNativeWindowsArm64Enabled=false` to every `dotnet build`
  and `dotnet test`. ARM64 native linking fails without the MSVC ARM64 ARM64 toolset.
- Use `build/Stride.Tests.Simple.slnf` for fast tests. Skip Graphics/Physics/Audio.
- `*.ssdeps` files lock during build. If rebuild fails: `find . -name "*.ssdeps" -delete`.
- `BACKERS.md` is required by GameStudio's About page — don't delete it during cleanup.

## 2. Namespace & Type Traps (THE BIG LIST)

| What you expect | Where it actually lives |
|---|---|
| `IServiceRegistry` | `Stride.Core` (NOT `Stride.Games`) |
| `GraphicsDevice.Platform` | **static** field, not instance |
| `EffectSystem` | `Stride.Rendering` (NOT `Stride.Effects`) |
| `ContentManager` | `Stride.Core.Serialization.Contents` |
| `IContentManager` | `Stride.Core.Serialization.Contents` |
| `PlayingAnimation` | `Stride.Animations` |
| `GraphicsDevice` | `Stride.Graphics` |
| `MicroThreadState.Waiting` | **does not exist** — check enum before using |
| `DefaultEntityComponentProcessorAttribute.ProcessorType` | **field is `.TypeName` (string)** |
| `DataContractAttribute.Name` | **field is `.Alias`** |
| `IModEventBus.SubscriptionCount` | **does not exist** — cast to `ModEventBus` |
| `Material.New()` at runtime | **silently fails** — EffectSystem can't compile shaders on the fly. Meshes appear in VisibilityGroup but never render. Use pre-compiled assets from the database. |

## 3. ECS / Entity Pitfalls

- `SceneInstance` inherits `EntityManager` which implements `IReadOnlySet<Entity>`.
  - ✅ `scene.Add(e)` / `scene.Remove(e)` / `scene.Count` / `scene.FirstOrDefault()`
  - ❌ `scene.Entities` (extension method, not a property)
- `EntityManager.Add()` is **internal** — accessible only within the same assembly.
- `EntityTransformExtensions.AddChild`/`RemoveChild` are **extension methods**, not
  instance methods. When `Entity` is the variable type, the extension resolves fine.
  When `EntityManager` is the type, the extension is ambiguous — call the static
  `EntityTransformExtensions.AddChild(entity, child)` explicitly.
- `EntityProcessor<T>` does **NOT** have an `.Entity` property. To get the entity
  from inside a processor:
  ```csharp
  var entity = ComponentDatas.First().Key.Entity;
  ```
- `EntityManager.Processors` is a list — use `entityManager.Processors.Add(proc)` /
  `.Remove(proc)` for mod processor registration. Detect by `proc.GetType().Assembly`.

## 4. ALC Isolation (THE HARDEST PROBLEM)

A collectible `AssemblyLoadContext` only GCs when **zero strong references** from
outside the ALC point at types defined inside it. This includes:
- Reflection caches (`Assembly.GetTypes()`, `Type.GetMethods()`)
- `AssemblyRegistry.AssemblyNameToAssembly`
- `DataSerializerFactory.AvailableAssemblySerializers`
- Event handler delegates whose target is a mod type
- Live `EntityComponent` instances from a mod still in the scene
- Static fields in engine code holding mod objects

### Mod LoadContext resolution policy

```csharp
protected override Assembly? Load(AssemblyName assemblyName)
{
    // Stable ABI: route to default context
    if (assemblyName.Name == "Modulus.Modding.Api")
        return null;

    // Shared dependency: route to its active ALC (preserves type identity)
    if (_host.TryGetLoadedSharedAssembly(assemblyName.Name, out var shared))
        return shared;

    return null; // standard isolation
}
```

**Critical:** when the mod shares an assembly with the engine or another mod
(e.g. `Stride.Engine.dll` for `EntityComponent` base class), the mod's ALC must
**return null** so the assembly is loaded from the default ALC. Returning the
assembly from another mod's ALC creates a **second copy** and breaks type identity.

### Unload checklist (executed in this order)

```
1. Cancel mod MicroThreads (query ScriptSystem.Scheduler for entries whose
   .Action.Method.DeclaringType.Assembly == modAssembly, call .Cancel())
2. DataSerializerFactory.ClearAssemblySerializers(modAssembly)
3. AssemblyRegistry.Unregister(modAssembly)
4. Remove mod EntityProcessors from EntityManager
5. Destroy all entities with mod components
6. Unsubscribe all event handlers from mod types
7. Call mod.Dispose() on all IMod instances
8. Null all cached MethodInfo/PropertyInfo for mod types
9. AssemblyLoadContext.Unload()
10. Null the ModLoadContext reference itself
11. GC.Collect() + GC.WaitForPendingFinalizers() — 2-3 cycles
12. Verify with WeakReference
```

A single leaked reference → that DLL never collects → repeated load/unload leaks
one full copy of the DLL per cycle. The 100-cycle memory test is authoritative;
xUnit `[Fact]`s that check `WeakReference.Target` in the same run are unreliable
in parallel test runs because of cross-test ALC pollution.

## 5. Mod Project Recipe (REQUIRED)

Every mod `.csproj` MUST have these four things or it WILL fail at runtime:

1. `<PackageId>Modulus.Mod</PackageId>` (or matching pattern), but `AssemblyName`
   matches the DLL name.
2. `PrivateAssets="all"` on `<ProjectReference>` to `Stride.Engine.csproj` and any
   other Stride assembly the mod touches. This prevents transitive `Stride.*.dll`
   from being copied into the mod's bin folder.
3. A `RemoveStrideDlls` post-build target that **explicitly deletes** any
   `Stride.*.dll` that snuck into `$(OutDir)`. Without it, mod types inherit a
   mod-local `EntityComponent` and the engine rejects them as type mismatches.
4. `freetype.dll` native dependency in `runtimes/win-x64/native/` of the host
   output — the font system needs it.

If a mod's components can't be cast to `EntityComponent` after load, it's
invariably a missing `RemoveStrideDlls` target.

## 6. Scene Management System

| Component | File | Purpose |
|---|---|---|
| `ModAutoLoadSystem` | `Modding/ModAutoLoadSystem.cs` | One-shot: loads mods, merges original scene geometry into mod scenes lacking renderable content, assigns camera to compositor slot |
| `ModSceneManager` | `Modding/ModSceneManager.cs` | Scene caching, registration, runtime switching. Tracks `OriginalSceneUrl` (game's default scene before mod override) |
| `ModSceneSwitchSystem` | `Modding/ModSceneSwitchSystem.cs` | Processes pending scene switches at end of frame (UpdateOrder=1000) |
| `ModSceneEntry` | `Modding/ModSceneEntry.cs` | Scene entry with ModId, SceneUrl, DisplayName, Behavior |

**How mod scene loading works:**
1. `Game.LoadModsEarly()` checks if any mod provides a `Replace`-behavior scene
2. If yes: saves `SceneSystem.InitialSceneUrl` to `ModSceneManager.OriginalSceneUrl`, then overrides `InitialSceneUrl` with the mod scene URL
3. `SceneSystem.LoadContent()` loads the mod scene (compositor before scene to avoid dummy-compositor binding)
4. `ModAutoLoadSystem.Update()` runs on first frame: merges original scene entities into the mod scene (mod scenes compiled without GPU resources typically only have Camera+Light)
5. Camera is assigned to compositor slot; duplicate cameras are removed

**`Material.New()` does NOT work at runtime.** The `EffectSystem` can't compile shaders on the fly — shader source files are stripped from the asset bundle by the dead-code eliminator, and the runtime shader compiler (`dxcompiler.dll` etc.) isn't shipped. Pre-compiled assets from the asset database work fine. Runtime `Material.New()` creates `RenderMesh` objects that silently fail at `MeshRenderFeature.PrepareEffectPermutationsImpl()` — they appear in the VisibilityGroup but are never submitted to the GPU. Fix: mods must ship pre-compiled shader bytecode in `shaders/` bundles (Phase 4.7 — `ModShaderManager`).

## 7. Game() / ModHost Integration

- `Game()` constructor (line ~246 of `Game.cs`) **already creates and registers
  `ModHost`** as a service. Do NOT pre-register in host apps — it throws
  "Service is already registered".
- `Game.Initialize()` checks for existing `ModHost` and uses it; only creates a
  new one if absent.
- `Game.Initialize()` adds `ModAutoLoadSystem` — a one-shot `GameSystemBase` that
  calls `ModHost.LoadAllMods()` on first Update, then merges original scene geometry
  into mod scenes lacking renderable content and assigns cameras. Zero host code needed.
- To get ModHost from a host app: `game.Services.GetService<ModHost>()`.
- Don't subscribe to `Game.GameStarted` to do scene creation — use a `SyncScript`
  instead, since `GameStarted` fires before the scene is fully loaded.

## 8. Content & GUID Pipeline

- Each `.modpkg` includes an `asset-guids.json` mapping virtual asset paths to
  compiled GUIDs:
  ```json
  { "mappings": [
      { "virtualPath": "models/sword", "guid": "a1b2c3d4-..." }
  ]}
  ```
- `ModContentManager.InjectGuidMappingsIntoRuntime` patches Stride's
  `ContentManager` `ContentIndexMap` so `ContentManager.Load<Scene>(url)` resolves
  mod assets by URL.
- **GUID collision check:** if a mod tries to register a GUID that the host game
  already uses, `ModValidator` aborts the mod with an error.
- Loose `.sd*` files can't be loaded by `ObjectDatabase` without indexing —
  mods need pre-compiled `asset.db` files.

## 9. HTTP API (engine's localhost:9876 server)

- All requests (GET and POST) **must** marshal work to the game thread.
  Stride's ECS and Scene Graph are not thread-safe.
- `HttpApiSystem` uses two queues:
  - `_updateQueue` — drained in `Update()` (ECS reads/writes)
  - `_drawQueue` — drained in `Draw()` (GPU commands, e.g. screenshot readback)
- Wrap `tcs.Task` with `.WithTimeout(TimeSpan.FromSeconds(5), ...)` to avoid
  indefinite hangs if the game thread is paused (loading, blocked on I/O).
- Screenshot readback is async: enqueue a `CopyTextureToBuffer` on the command
  list, signal a fence, resume on a background thread. **Don't** read the
  backbuffer synchronously — it stalls the GPU.

## 10. ModEventBus Proxy

`ModEventBusProxy` auto-tags every subscription with the subscribing mod's ID,
so that when a mod is unloaded, all of its subscriptions can be cleaned up at
once by ID. **Always use the proxy from mods** — never reach for the concrete
`ModEventBus` directly (you'll skip the tagging and leak handlers).

## 11. Pragmatic Renaming Rule

"Any system that we have actively changed needs to be rebranded, if it's still
the same package we leave it alone." So:
- Engine NuGet is `Modulus.Engine` ✅
- DLL stays `Stride.Engine.dll` (don't rename — breaks InternalsVisibleTo)
- New modding APIs are in `Modulus.Modding.Api` namespace ✅
- Stride's `Entity`, `EntityManager`, `SceneInstance` etc. keep their names ✅

## 12. Testing

- xUnit `[Fact]`s for unit tests.
- ALC `WeakReference` tests are unreliable in xUnit parallel runs. Use the
  100-cycle memory test as the authoritative leak test.
- Use `services.AddService<T>()` / `Services.GetService<T>()` for dependency
  injection; the host's `ServiceRegistry` is built in `Game()` ctor.
- For graphics-dependent code, use `Stride.Tests.Simple.slnf` filters; full
  Stride.Tests.sln requires a working GPU.

## 13. Memory Shortcuts

- Look for: `MEMORY.md` (chronological session notes) and `USER.md` (user
  preferences, pain points, hardware specs).
- Editor logs: `D:\Project-Modulus-Game-Engine\editor-*.log`,
  `pipeline-debug.log`. Read these directly — don't ask the user to paste.
- The 10-iteration blind-recompile loop is an anti-pattern. Tune live via
  `/api/v1/...` HTTP endpoints or settings dialog, then bake constants.

## 14. Discovered Anti-Patterns

| Anti-pattern | Why it's bad |
|---|---|
| "Fix → build → ask user to test" | User has explicitly said: don't. Launch editor/API, verify yourself. |
| Bundling multiple changes per commit | User: small, targeted, individually testable changes. |
| Re-registering `ModHost` in host app | Throws "Service is already registered" — use `Services.GetService<ModHost>()`. |
| Calling `Game.GameStarted` to create scene | Fires before scene loads — use `SyncScript`. |
| Using `scene.Entities` | Wrong — `SceneInstance` IS an `IReadOnlySet<Entity>` directly. |
| Skipping `RemoveStrideDlls` target | Mod components won't cast to engine's `EntityComponent`. |
| Skipping `PrivateAssets="all"` | Transitive `Stride.*.dll` pollutes mod bin → same type-identity failure. |
| Returning the shared assembly from ModLoadContext | Creates a second copy → type identity breaks. |
| Using `Material.New()` at runtime for visible geometry | Silently fails — EffectSystem can't compile shaders on the fly. Meshes appear in VisibilityGroup but never render. Use pre-compiled assets or ship shader bytecode in `shaders/` bundles. |
