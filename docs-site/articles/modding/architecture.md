# Modulus Engine — Modding System Architecture

> **Version:** 1.0  
> **Status:** Living Document  
> **Audience:** Engine developers, mod authors, platform integrators

---

## Table of Contents

1. [High-Level Overview](#1-high-level-overview)
2. [AssemblyLoadContext Isolation](#2-assemblyloadcontext-isolation)
3. [Mod Lifecycle](#3-mod-lifecycle)
4. [17-Step Cleanup Checklist for ALC Unload](#4-17-step-cleanup-checklist-for-alc-unload)
5. [Type Registration](#5-type-registration)
6. [System Registration](#6-system-registration)
7. [Content Resolution](#7-content-resolution)
8. [Shader Management](#8-shader-management)
9. [Orphan Component Handling](#9-orphan-component-handling)
10. [Crash Safety Model](#10-crash-safety-model)
11. [Event Bus](#11-event-bus)
12. [State Persistence](#12-state-persistence)

---

## 1. High-Level Overview

The Modulus Engine modding system is built on four core abstractions that form a layered architecture for safe, hot-reloadable mod support.

### Core Types

| Type | Responsibility |
|------|---------------|
| **`ModHost`** | Singleton orchestrator. Owns the mod lifecycle, owns all `ModLoadContext` instances, mediates between mods and the engine. |
| **`ModLoadContext`** | Per-mod isolation boundary. Wraps a .NET `AssemblyLoadContext` (ALC). Holds the mod's loaded assemblies, service registrations, and cleanup delegates. |
| **`ModPackage`** | On-disk representation of a mod. A directory or `.zip` containing the manifest, assemblies, assets, and shaders. |
| **`ModManifest`** | Declarative metadata (`mod.json`). Declares mod identity, version, dependencies, entry point assembly, content roots, and load order hints. |

### Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                          ModHost (Singleton)                        │
│                                                                     │
│  ┌──────────────┐  ┌──────────────┐       ┌──────────────┐         │
│  │ ModPackage A  │  │ ModPackage B │  ...  │ ModPackage N │         │
│  │  mod.json     │  │  mod.json    │       │  mod.json    │         │
│  │  dlls/assets  │  │  dlls/assets │       │  dlls/assets │         │
│  └──────┬───────┘  └──────┬───────┘       └──────┬───────┘         │
│         │                  │                      │                 │
│         ▼                  ▼                      ▼                 │
│  ┌──────────────┐  ┌──────────────┐       ┌──────────────┐         │
│  │ ModLoadCtx A  │  │ ModLoadCtx B │       │ ModLoadCtx N │         │
│  │  ┌─────────┐  │  │  ┌─────────┐ │       │  ┌─────────┐ │         │
│  │  │ ALC     │  │  │  │ ALC     │ │       │  │ ALC     │ │         │
│  │  │ (collect)│  │  │  │ (collect)│ │       │  │ (collect)│ │         │
│  │  └─────────┘  │  │  └─────────┘ │       │  └─────────┘ │         │
│  │  Registrations│  │  Registrations│       │  Registrations│        │
│  │  Subscriptions│  │  Subscriptions│       │  Subscriptions│        │
│  │  CleanupList  │  │  CleanupList  │       │  CleanupList  │        │
│  └──────────────┘  └──────────────┘       └──────────────┘         │
│                                                                     │
│  Shared: TypeRegistry │ EventBus │ ContentResolver │ ShaderManager  │
└─────────────────────────────────────────────────────────────────────┘
```

### Relationships

- **ModHost** creates one `ModLoadContext` per discovered `ModPackage`.
- **ModLoadContext** parses the `ModManifest` to determine entry points, dependencies, and asset roots.
- **ModManifest** is the contract between the mod author and the engine — it is validated before any assembly is loaded.

---

## 2. AssemblyLoadContext Isolation

Every mod runs inside its own **collectible** `AssemblyLoadContext`. This is the foundation of the hot-reload and crash-safety model.

### Design Principles

1. **Collectible ALCs** — Each mod's ALC is created with `isCollectible: true`, enabling full unload and GC of all mod types/assemblies at runtime.
2. **Parent Resolution** — Mod ALCs use the `Default` ALC as their parent. Shared engine contracts (`IMod`, `IEntityProcessor`, etc.) resolve from the parent, guaranteeing type identity across the boundary.
3. **Peer Isolation** — Mods cannot directly see each other's types. Cross-mod communication goes through engine-owned interfaces registered in the shared `TypeRegistry`.
4. **No Hard References** — Mods reference engine contracts (NuGet package or SDK assembly), never each other.

### ALC Resolution Chain

```
                         ┌──────────────────┐
                         │   Default ALC     │
                         │  (Engine + BCL)   │
                         │                   │
                         │  Engine.Contracts │
                         │  Engine.Core      │
                         │  Engine.ECS       │
                         └────────┬─────────┘
                                  │ parent resolution
                    ┌─────────────┼─────────────┐
                    │             │             │
                    ▼             ▼             ▼
             ┌──────────┐  ┌──────────┐  ┌──────────┐
             │ ModALC A  │  │ ModALC B  │  │ ModALC C  │
             │           │  │           │  │           │
             │ ModA.dll  │  │ ModB.dll  │  │ ModC.dll  │
             │ LibX.dll  │  │ LibY.dll  │  │ LibZ.dll  │
             └──────────┘  └──────────┘  └──────────┘
```

### Cross-ALC Type Identity

The critical invariant: **engine-facing types must always resolve from the parent ALC.**

```
// Mod code — lives in ModALC
public class MyProcessor : IEntityProcessor  // IEntityProcessor → Default ALC
{
    public void Process(Entity e) { ... }
}

// Engine code — lives in Default ALC
var processor = (IEntityProcessor)instance;  // ✅ Same type identity
```

If a mod bundles its own copy of `Engine.Contracts.dll`, the cast fails with `InvalidCastException` because the type identities diverge. The `ModLoadContext` constructor hooks `Resolving` to enforce that contract assemblies always resolve upward to the parent.

### Custom Resolution

```
ModLoadContext.Resolving handler:
  1. Check if assembly name matches a known engine contract → delegate to parent
  2. Check mod's local probing paths (mod directory, lib/ subfolder)
  3. Check peer mods only if explicit dependency declared in manifest (opt-in)
  4. Return null (trigger FileNotFoundException)
```

---

## 3. Mod Lifecycle

The mod lifecycle is a strict state machine with six phases.

```
┌───────────┐    ┌────────────┐    ┌──────────┐    ┌───────────────┐
│ Discovery │───▶│ Validation │───▶│ Loading  │───▶│ Initialization│
└───────────┘    └────────────┘    └──────────┘    └───────┬───────┘
                                                          │
                                                          ▼
                                                   ┌──────────┐    ┌───────────┐
                                                   │ Running  │───▶│ Unloading │
                                                   └──────────┘    └───────────┘
```

### Phase 1: Discovery

```
ModHost.DiscoverMods(searchPaths[])
  ├── Scan for mod.json files recursively
  ├── Parse each into a ModManifest (lightweight, no assembly load)
  ├── Build dependency graph
  └── Emit ModDiscovered events
```

- Searches configurable paths: game directory, `%APPDATA%/ModulusEngine/Mods/`, Steam Workshop cache.
- Only reads `mod.json` — no assemblies touched yet.

### Phase 2: Validation

```
ModHost.ValidateMod(manifest)
  ├── Check engine version compatibility (semver range)
  ├── Resolve all declared dependencies (must exist + compatible version)
  ├── Topological sort for load order (detect cycles → error)
  ├── Verify entry point assembly exists on disk
  ├── Verify declared asset roots exist
  └── Transition: Validated or Failed (with reason)
```

- Dependency cycles are a hard error.
- Missing optional dependencies produce a warning, not a failure.

### Phase 3: Loading

```
ModHost.LoadMod(package)
  ├── Create ModLoadContext (collectible ALC)
  │     ├── Set parent = Default ALC
  │     └── Register Resolving/Resolved handlers
  ├── Load entry point assembly via ALC.LoadFromAssemblyPath()
  ├── Scan for IMod implementor
  ├── Instantiate IMod (parameterless constructor or DI-resolved)
  └── Transition: Loaded (IMod instance held)
```

- Only the entry point assembly is loaded eagerly. Other assemblies load on demand.
- `IMod` is the mod's root contract.

```csharp
public interface IMod
{
    string Id { get; }
    string Version { get; }
    void Initialize(IModContext context);
    void Shutdown();
}
```

### Phase 4: Initialization

```
IMod.Initialize(context)
  ├── Register types (serializers, components, processors)
  ├── Register content paths
  ├── Register event subscriptions
  ├── Register cleanup delegates
  └── Engine validates registrations (no conflicts)
```

- The `IModContext` is the mod's API surface — the **only** way to interact with the engine.

### Phase 5: Running

- Normal engine operation. Mod code executes as part of the ECS pipeline, event handlers, content queries, etc.
- Mods can be individually paused (`Disabled` state) without unloading.

### Phase 6: Unloading

```
ModHost.UnloadMod(modId)
  ├── Invoke 17-step cleanup checklist (see §4)
  ├── Call IMod.Shutdown()
  ├── Null all references held by ModHost
  ├── Invoke ALC.Unload() (collectible)
  ├── Trigger GC (multiple generations)
  └── Verify WeakReference to ALC is dead
```

---

## 4. 17-Step Cleanup Checklist for ALC Unload

Before `AssemblyLoadContext.Unload()` is called, every possible root that could keep mod objects alive must be severed. Missing even one step causes the ALC to remain in memory — a "zombie mod."

| # | Step | Owner | What It Does |
|---|------|-------|-------------|
| 1 | **Flush event subscriptions** | `EventBus` | Remove all handlers registered by this mod. Iterate `ModLoadContext.Subscriptions[]` and unsubscribe each. |
| 2 | **Remove type registrations** | `TypeRegistry` | Remove all types registered by this mod (serializers, component types, processor types). |
| 3 | **Remove content path registrations** | `CompositeFileProviderService` | Remove the mod's file providers from the composite. |
| 4 | **Remove shader registrations** | `ModShaderManager` | Unregister all shader effects, release GPU resources. |
| 5 | **Remove entity processors** | `EntityProcessorManager` | Remove all `IEntityProcessor` instances from the pipeline. |
| 6 | **Null out orphan component class refs** | `OrphanComponentManager` | Replace `Type` references with string keys for serialization safety. |
| 7 | **Clear cached delegates** | `DelegateCache` | Null any `Delegate` or `Func<>`/`Action<>` references pointing into mod assemblies. |
| 8 | **Remove timer/scheduler callbacks** | `Scheduler` | Cancel all scheduled tasks owned by this mod. |
| 9 | **Close file handles** | `ModLoadContext` | Invoke mod-registered cleanup delegates (typically file/stream closures). |
| 10 | **Release pooled objects** | `ObjectPool` | Return or discard all objects from pools that were created by mod types. |
| 11 | **Detach UI elements** | `UIManager` | Remove any mod-registered UI panels, widgets, or HUD elements. |
| 12 | **Remove console commands** | `ConsoleCommandRegistry` | Deregister `/` commands registered by this mod. |
| 13 | **Clear localization entries** | `LocalizationManager` | Remove mod-contributed string tables. |
| 14 | **Remove network message handlers** | `NetworkManager` | Deregister custom packet handlers for multiplayer mods. |
| 15 | **Break circular references** | `ModLoadContext` | Run mod-registered "pre-unload" hooks that break known cycles. |
| 16 | **Call IMod.Shutdown()** | `ModHost` | Let the mod perform its own cleanup (order matters — after all registrations removed). |
| 17 | **Null ModLoadContext references** | `ModHost` | Remove the `ModLoadContext` from the host's dictionary, null the `IMod` instance reference. |

```
┌──────────────────────────────────────────────────────────────┐
│                  17-Step Cleanup Sequence                    │
│                                                              │
│  1. Events ──▶ 2. Types ──▶ 3. Content ──▶ 4. Shaders      │
│       │                                                     │
│  5. Processors ──▶ 6. Orphans ──▶ 7. Delegates ──▶ 8. Timer│
│       │                                                     │
│  9. Files ──▶ 10. Pools ──▶ 11. UI ──▶ 12. Commands        │
│       │                                                     │
│  13. Localization ──▶ 14. Network ──▶ 15. Cycles            │
│       │                                                     │
│  16. IMod.Shutdown() ──▶ 17. Null References                │
│                                                              │
│  ──▶ ALC.Unload() ──▶ GC.Collect(2, GCCollectionMode.Forced)│
│  ──▶ Verify WeakReference.IsAlive == false                   │
└──────────────────────────────────────────────────────────────┘
```

---

## 5. Type Registration

### DataSerializerFactory

The serialization system uses a factory pattern to decouple type identity from serialization logic.

```
┌─────────────────────────────────────────────────┐
│           DataSerializerFactory                  │
│                                                  │
│  Register<T>(IDataSerializer<T> serializer)     │
│  Unregister<T>()                                 │
│  GetSerializer(Type dataType) → IDataSerializer │
│                                                  │
│  Internals:                                      │
│    Dictionary<Type, IDataSerializer> _serializers│
│    Dictionary<Guid, Type> _guidToType            │
└─────────────────────────────────────────────────┘
```

- Mods register custom serializers during `Initialize()`.
- Each serializer is tagged with a **stable GUID** for save-file compatibility.
- On mod unload, all serializers owned by that mod are removed (step 2 of cleanup).

### AssemblyRegistry

Tracks all loaded assemblies for type scanning and reflection.

```
┌──────────────────────────────────────────────────────┐
│              AssemblyRegistry                         │
│                                                       │
│  Register(Assembly asm, ModLoadContext owner)         │
│  GetTypes<T>() → IEnumerable<Type>                   │
│  GetOwner(Type t) → ModLoadContext                    │
│                                                       │
│  Internals:                                           │
│    Dictionary<Assembly, ModLoadContext> _ownership    │
│    Dictionary<Type, ModLoadContext> _typeOwnership    │
└──────────────────────────────────────────────────────┘
```

- Every type scanned from a mod assembly is tagged with its owning `ModLoadContext`.
- This enables bulk cleanup — "remove all types from mod X."

---

## 6. System Registration

### EntityProcessors

Mods contribute to the ECS pipeline by registering `IEntityProcessor` implementations.

```csharp
public interface IEntityProcessor
{
    int Order { get; }          // Execution priority (lower = earlier)
    void Process(Span<Entity> entities, float deltaTime);
}
```

### DefaultEntityComponentProcessor Attribute

For common cases, the engine provides a generic processor that auto-iterates entities with specific components:

```csharp
[DefaultEntityComponentProcessor(typeof(HealthComponent))]
public struct HealthDecaySystem : IComponentProcessor<HealthComponent>
{
    public void Process(ref HealthComponent component, float deltaTime)
    {
        component.Current -= component.DecayRate * deltaTime;
    }
}
```

The attribute causes the engine to generate an optimized archetype query at registration time, avoiding per-frame reflection.

### Registration Flow

```
Mod.Initialize(context)
  │
  ├── context.RegisterProcessor<HealthDecaySystem>()
  │     │
  │     ├── Scan for [DefaultEntityComponentProcessor]
  │     ├── Build archetype filter: Has<HealthComponent>
  │     ├── Insert into ProcessorPipeline at Order position
  │     └── Tag registration with ModLoadContext (for cleanup)
  │
  └── context.RegisterProcessor(new CustomAIProcessor())
        └── (same pipeline insertion, manual archetype control)
```

---

## 7. Content Resolution

### CompositeFileProviderService

Content (textures, models, data files) is resolved through a layered file provider system.

```
┌───────────────────────────────────────────────────────────┐
│          CompositeFileProviderService                      │
│                                                            │
│  Providers (ordered, first match wins):                    │
│    [0] OverrideProvider    (user overrides, highest prio)  │
│    [1] ModProvider A       (mod A's content root)          │
│    [2] ModProvider B       (mod B's content root)          │
│    [3] ...                                                │
│    [N] BaseGameProvider    (vanilla assets, lowest prio)   │
│                                                            │
│  GetFile(guid) → Stream                                    │
│  GetFile(virtualPath) → Stream                             │
│  EnumerateFiles(filter) → IEnumerable<ContentEntry>        │
└───────────────────────────────────────────────────────────┘
```

### GUID-Aware Pipeline

Every content asset is identified by a **GUID** (stable across renames/moves) alongside a human-readable virtual path.

```
Content Request:
  1. Lookup by GUID in global index (O(1) hash)
  2. If not found, scan providers in order by virtual path
  3. First provider that returns a stream wins
  4. Stream is wrapped in a ContentHandle (tracks lifetime for unload)
```

```
┌─────────────┐     ┌──────────────┐     ┌─────────────────┐
│ Game Code    │────▶│ ContentSvc   │────▶│ Provider[0..N]  │
│ Get(guid)    │     │ Global Index │     │ First-match     │
└─────────────┘     └──────────────┘     └────────┬────────┘
                                                   │
                                          ┌────────▼────────┐
                                          │  FileStream /   │
                                          │  ZipStream /    │
                                          │  MemoryMapped   │
                                          └─────────────────┘
```

- Mod content providers are removed during cleanup step 3.
- Content handles opened by mod code are tracked and force-closed during cleanup step 9.

---

## 8. Shader Management

### ModShaderManager

Mods can provide custom shader effects for rendering.

```
┌──────────────────────────────────────────────────────────┐
│              ModShaderManager                             │
│                                                           │
│  RegisterEffect(name, byte[], ModLoadContext owner)       │
│  UnregisterByOwner(ModLoadContext owner)                  │
│  GetEffect(name) → CompiledEffect                        │
│                                                           │
│  Pipeline:                                                │
│    .hlsl/.glsl source ──▶ Compile ──▶ Cache ──▶ Bind     │
│                                                           │
│  On unload:                                               │
│    1. Unbind from EffectSystem                            │
│    2. Release GPU resources                               │
│    3. Remove from cache                                   │
└──────────────────────────────────────────────────────────┘
```

### EffectSystem Integration

```
┌────────────────┐     ┌───────────────────┐     ┌──────────────┐
│ ModShaderMgr   │────▶│ EffectSystem      │────▶│ GPU Backend  │
│ (registration) │     │ (compilation +    │     │ (D3D/Vulkan/ │
│                │     │  state management)│     │  OpenGL)     │
└────────────────┘     └───────────────────┘     └──────────────┘
```

- Shaders are compiled lazily on first use.
- Compilation errors transition the mod to `Errored` state (see §10) but don't crash the engine.
- On unload, GPU resources are released before the ALC is unloaded to prevent dangling native pointers.

---

## 9. Orphan Component Handling

When a mod that contributed custom ECS components is removed, existing entity data references types that no longer exist. The **OrphanComponent** system preserves save compatibility.

### Problem

```
Entity #42:
  HealthComponent    (engine)     → OK
  FrostAuraComponent (ModA)      → ModA unloaded → Type gone!
```

### Solution: OrphanComponent Wrapper

```
┌──────────────────────────────────────────────────────────────┐
│                    OrphanComponent                            │
│                                                               │
│  string OriginalTypeId  (GUID of the component type)          │
│  string OriginalModId   (mod that owned the type)             │
│  byte[] RawData         (serialized component bytes)          │
│  string SerializedJson  (human-readable fallback)             │
│                                                               │
│  Behavior:                                                    │
│    - On save: serializes normally (preserves raw bytes)       │
│    - On load: if mod present → deserialize normally           │
│              if mod absent  → wrap in OrphanComponent         │
│    - On mod re-enable: unwrap OrphanComponents back to        │
│                        real typed components                   │
└──────────────────────────────────────────────────────────────┘
```

### Lifecycle

```
ModA unloaded
  │
  ├── EntityArchetype[FrostAuraComponent] marked "orphaned"
  ├── New queries exclude orphaned archetypes
  ├── Existing FrostAuraComponent data → wrapped in OrphanComponent
  └── World serialization writes OrphanComponent with raw bytes

ModA re-loaded
  │
  ├── TypeRegistry has FrostAuraComponent again
  ├── OrphanComponentManager scans for matching orphans
  ├── Deserialize raw bytes → FrostAuraComponent
  └── Unwrap: OrphanComponent → typed component (entity is whole again)
```

---

## 10. Crash Safety Model

### ModExceptionHandler

All mod entry points are wrapped in structured exception handling. A mod crash **never** takes down the engine.

```
┌──────────────────────────────────────────────────────────────┐
│                  ModExceptionHandler                          │
│                                                               │
│  Execute(modId, Action action):                               │
│    try { action(); }                                          │
│    catch (Exception ex) {                                     │
│      Log.Error($"Mod {modId} crashed: {ex}");                │
│      TransitionModState(modId, Errored);                     │
│      RecordCrash(modId, ex);                                  │
│      if (crashCount > threshold) → AutoDisable(modId);       │
│    }                                                          │
│                                                               │
│  Execute<T>(modId, Func<T> func, T fallback):                 │
│    try { return func(); }                                     │
│    catch (Exception ex) { ... return fallback; }              │
└──────────────────────────────────────────────────────────────┘
```

### Mod States

```
                    ┌──────────┐
          ┌────────│ Disabled │◄────────┐
          │        └──────────┘         │
          │ (user/disable)              │ (auto or user)
          ▼                             │
┌──────────┐    ┌──────────┐    ┌──────┴──────┐
│  Loaded  │───▶│ Errored  │───▶│  Disabled   │
└──────────┘    └──────────┘    └─────────────┘
     │               ▲
     │               │ (exception caught)
     │               │
     └───────────────┘
      (retry after fix)
```

| State | Description |
|-------|-------------|
| **Loaded** | Normal operation. All systems active. |
| **Disabled** | Intentionally or automatically disabled. No systems execute. User can re-enable. |
| **Errored** | Caught an exception. Systems paused. Crash report recorded. Auto-disables after N crashes. |

### Crash Thresholds

- **First crash**: Log warning, remain in `Errored` state, pause mod systems.
- **Second crash (same mod, same session)**: Auto-transition to `Disabled`.
- **Persistent across sessions**: Crash count stored in `ModStateStore` (see §12).

---

## 11. Event Bus

### Design

The event bus enables loose coupling between mods and the engine.

```
┌───────────────────────────────────────────────────────────────┐
│                       EventBus                                │
│                                                                │
│  Subscribe<TEvent>(ModLoadContext owner, Action<TEvent> handler)│
│  Unsubscribe(ModLoadContext owner)  // bulk unsubscribe        │
│  Publish<TEvent>(TEvent evt)                                  │
│                                                                │
│  Internals:                                                    │
│    Dictionary<Type, List<Handler>> _handlers                   │
│    Dictionary<ModLoadContext, List<Subscription>> _ownership   │
│                                                                │
│  Thread safety: Publish is lock-free (copy-on-write snapshot)  │
└───────────────────────────────────────────────────────────────┘
```

### Per-Mod Subscription Tracking

Every `Subscribe` call records:
- The `ModLoadContext` (owner)
- The event type
- The delegate reference

This enables **bulk cleanup** during mod unload (step 1 of the 17-step checklist):

```
EventBus.Unsubscribe(modLoadContext)
  └── For each subscription owned by this context:
        └── Remove from _handlers[eventType]
```

### Auto-Cleanup Guarantee

```
┌─────────────┐    Subscribe()    ┌───────────┐
│  Mod Code   │──────────────────▶│ EventBus   │
└─────────────┘                   │            │
                                  │ ┌────────┐ │
                                  │ │Track   │ │
                                  │ │Owner   │ │
                                  │ └────────┘ │
                                  └─────┬──────┘
                                        │
                               UnloadMod() triggers
                                        │
                                        ▼
                                  ┌───────────┐
                                  │ Auto-     │
                                  │ cleanup   │
                                  │ (step 1)  │
                                  └───────────┘
```

---

## 12. State Persistence

### ModStateStore

Per-mod persistent state is stored in the user's application data directory.

**Location:** `%APPDATA%/ModulusEngine/`

```
%APPDATA%/ModulusEngine/
  ├── mod-states.json          # Global mod enable/disable/errored state
  ├── crash-reports/
  │   ├── ModA_2025-01-15.log
  │   └── ModB_2025-01-16.log
  ├── mods/
  │   ├── ModA/
  │   │   ├── state.json       # Mod-specific persistent data
  │   │   └── settings.json    # User-configurable mod settings
  │   └── ModB/
  │       └── state.json
  └── config.json              # Global modding config (search paths, etc.)
```

### mod-states.json

```json
{
  "mods": {
    "com.example.frostmod": {
      "state": "Loaded",
      "crashCount": 0,
      "lastLoaded": "2025-01-15T10:30:00Z",
      "version": "1.2.0"
    },
    "com.example.aimod": {
      "state": "Disabled",
      "crashCount": 3,
      "lastLoaded": "2025-01-14T08:00:00Z",
      "autoDisabledReason": "Crash threshold exceeded",
      "version": "0.9.1"
    }
  }
}
```

### Mod State Lifecycle with Persistence

```
Engine Start
  │
  ├── Load mod-states.json
  ├── For each mod:
  │     ├── If state == "Loaded"  → normal lifecycle (§3)
  │     ├── If state == "Disabled" → skip, show in UI as disabled
  │     └── If state == "Errored"  → retry loading, keep crash count
  │
  ... (engine runs) ...
  │
  ├── Mod state changes (load/unload/error/disable)
  │     └── Immediately persist to mod-states.json (atomic write)
  │
Engine Shutdown
  └── Final persist of all states
```

### Mod Settings

Mods can persist user-facing settings through the `IModContext.Settings` API:

```csharp
// In IMod.Initialize:
var settings = context.Settings.Get<MyModSettings>();
// Returns deserialized settings or defaults

// On change:
context.Settings.Save(mySettings);
// Writes to %APPDATA%/ModulusEngine/mods/{modId}/settings.json
```

---

## Appendix A: Thread Safety Summary

| Resource | Strategy |
|----------|----------|
| EventBus.Publish | Copy-on-write snapshot of handler list |
| TypeRegistry | Read-write lock; writes only during init/unload |
| ContentResolver | Immutable provider list; swap on mod load/unload |
| ECS Pipeline | Mod processors run on main thread; batch-locked |
| ModStateStore | Atomic file writes (write-to-temp + rename) |

## Appendix B: Extension Points for Mod Authors

| Extension Point | Interface | Registration |
|----------------|-----------|-------------|
| Custom component | `struct` with `[Component]` | `context.RegisterComponent<T>()` |
| Entity processor | `IEntityProcessor` | `context.RegisterProcessor<T>()` |
| Data serializer | `IDataSerializer<T>` | `context.RegisterSerializer<T>()` |
| Event handler | `Action<TEvent>` | `context.Subscribe<TEvent>(handler)` |
| Content provider | `IFileProvider` | `context.RegisterContent(path)` |
| Shader effect | HLSL/GLSL source | `context.RegisterShader(name, source)` |
| Console command | `IConsoleCommand` | `context.RegisterCommand<T>()` |
| UI panel | `IUIPanel` | `context.RegisterUI<T>()` |

---

*End of Architecture Document*
