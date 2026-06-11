# Stride Engine Modifications for Modulus Engine

> **Audience:** Contributors maintaining the Modulus Engine fork of [Stride](https://github.com/stride3d/stride).  
> **Purpose:** Document every change made to the upstream Stride codebase so that (a) merge conflicts are predictable and minimal, and (b) new contributors understand *why* each hook exists.

---

## Table of Contents

1. [Design Philosophy: The 1-Line Hook Pattern](#design-philosophy-the-1-line-hook-pattern)
2. [AssemblyRegistry.cs](#1-assemblyregistrycs)
3. [DataSerializerFactory.cs](#2-dataserializerfactorycs)
4. [TypeDescriptorFactory.cs](#3-typedescriptorfactorycs)
4. [Game.cs](#4-gamecs)
5. [HttpApi System](#5-httpapi-system)
   - 5.1 EngineHttpServer
   - 5.2 EditorHttpServer
   - 5.3 HttpApiSystem
6. [Route Handlers](#6-route-handlers)
   - Scene Routes
   - Asset Routes
   - Mod Routes
   - Editor Routes
   - Debug Routes
   - Render Routes
   - Status Routes
7. [Merge Conflict Guide](#merge-conflict-guide)

---

## Design Philosophy: The 1-Line Hook Pattern

Every modification to an upstream Stride file follows the **1-line hook pattern**. The rule is simple:

> **Inside a Stride source file, insert no more than one or two lines that delegate to external Modulus code. All real logic lives in separate Modulus-owned files.**

### Why

| Problem | Solution |
|---|---|
| Upstream Stride merges touch hundreds of files. Large in-file changes create constant merge conflicts. | A single-line call site rarely collides with an upstream diff on the same line. |
| Reviewers of the Stride fork need to understand at a glance what changed and why. | A line like `ModHooks.OnAssemblyUnregistered(assembly);` is self-documenting. |
| Testing in isolation is easier when the hook boundary is clean. | Modulus-side classes can be unit-tested without booting the engine. |

### Anatomy of a Hook

```csharp
// === MODULUS HOOK — do not expand ===
ModHooks.SomeMethod(args);
// === END MODULUS HOOK ===
```

The surrounding comment markers (`MODULUS HOOK`) are optional but recommended in files where the hook's purpose might not be obvious. In practice the method call alone is usually sufficient.

### Rules for Contributors

1. **Never add more than one logical statement inside a Stride file.** If you need two statements, wrap them in a single `ModHooks` method.
2. **Never modify existing Stride code** — only append or insert a single call.
3. **Keep the hook call on one line.** No multi-line arguments unless absolutely unavoidable.
4. **All Modulus logic lives in `Modulus.*` namespaces/projects,** not inside Stride assemblies.

---

## 1. AssemblyRegistry.cs

**File:** `Stride.Core/Serialization/AssemblyRegistry.cs` (or equivalent registration module)

### Change Summary

| What | Lines Added | Side |
|---|---|---|
| `Unregister(Assembly)` method | ~15 lines | Stride fork (internal helper) |
| `AssemblyUnregistered` event | 1 line (field) + 1 line (invoke) | Stride fork |

### `Unregister(Assembly)`

```csharp
public static void Unregister(Assembly assembly)
{
    if (assembly == null) throw new ArgumentNullException(nameof(assembly));

    lock (_lock)
    {
        _registeredAssemblies.Remove(assembly);
        // Clear related lookup caches
        _serializersByAssembly.Remove(assembly);
        _typesByAssembly.Remove(assembly);
    }

    AssemblyUnregistered?.Invoke(null, assembly);
}
```

This is the counterpart to the existing `Register(Assembly)`. When a mod is unloaded at runtime its assembly must be deregistered so that stale type references don't pollute the serializer and type-descriptor caches.

### `AssemblyUnregistered` Event

```csharp
public static event EventHandler<Assembly> AssemblyUnregistered;
```

Other subsystems subscribe to this to cascade their own cleanup:

| Subscriber | Action on Event |
|---|---|
| `DataSerializerFactory` | Calls `ClearAssemblySerializers(assembly)` |
| `TypeDescriptorFactory` | Calls `ClearAssemblyCache(assembly)` |
| `ModHost` | Logs unregistration for diagnostics |

### Hook in Upstream Code (1-line pattern)

Inside the existing `Register` flow or any assembly-load callback:

```csharp
AssemblyRegistered?.Invoke(null, assembly);   // existing line
// No additional line needed here — Unregister is called explicitly by ModHost.
```

The `Unregister` method is additive (new method, no modification to existing methods) and therefore creates **zero merge conflict surface** with upstream Stride.

---

## 2. DataSerializerFactory.cs

**File:** `Stride.Core/Serialization/DataSerializerFactory.cs`

### Change Summary

| What | Lines Added | Side |
|---|---|---|
| `ClearAssemblySerializers(Assembly)` method | ~12 lines | Stride fork |
| Alias collision override logic | ~8 lines | Stride fork |

### `ClearAssemblySerializers(Assembly)`

```csharp
public static void ClearAssemblySerializers(Assembly assembly)
{
    lock (_lock)
    {
        var keysToRemove = _serializerByType
            .Where(kv => kv.Value.Assembly == assembly)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _serializerByType.Remove(key);

        // Also purge the alias lookup
        var aliasKeys = _serializerByAlias
            .Where(kv => keysToRemove.Contains(kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var alias in aliasKeys)
            _serializerByAlias.Remove(alias);
    }
}
```

Called when `AssemblyRegistry.AssemblyUnregistered` fires. Ensures that serializers defined in a modded assembly are evicted so that reloaded types don't collide with stale entries.

### Alias Collision Override

When a mod assembly registers a serializer with an alias that already exists (e.g. a hot-reload scenario), the new serializer **silently replaces** the old one rather than throwing:

```csharp
// Inside existing serializer registration path:
if (_serializerByAlias.ContainsKey(alias))
{
    _serializerByAlias[alias] = serializerType;  // override, don't throw
}
```

This is a **one-line replacement** of what was previously a `throw` or `Debug.Assert`. Upstream Stride treats alias collisions as bugs; Modulus treats them as expected during mod reload cycles.

### 1-Line Hook

```csharp
// Upstream Stride file — the only Modulus addition:
AssemblyRegistry.AssemblyUnregistered += (_, asm) => ClearAssemblySerializers(asm);
```

This single subscription line is placed at the bottom of the static constructor. All cleanup logic is in `ClearAssemblySerializers`, which is a new additive method.

---

## 3. TypeDescriptorFactory.cs

**File:** `Stride.Core/Serialization/TypeDescriptorFactory.cs`

### Change Summary

| What | Lines Added | Side |
|---|---|---|
| `ClearAssemblyCache(Assembly)` method | ~10 lines | Stride fork |

### `ClearAssemblyCache(Assembly)`

```csharp
public static void ClearAssemblyCache(Assembly assembly)
{
    lock (_lock)
    {
        var keysToRemove = _typeDescriptors
            .Where(kv => kv.Key.Assembly == assembly)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _typeDescriptors.Remove(key);
    }
}
```

Parallel to the serializer cleanup above — removes `TypeDescriptor` entries that belong to an unloaded mod assembly.

### 1-Line Hook

```csharp
// Stride fork static constructor or module init:
AssemblyRegistry.AssemblyUnregistered += (_, asm) => ClearAssemblyCache(asm);
```

---

## 4. Game.cs

**File:** `Stride.Engine/Game.cs`

### Change Summary

| What | Lines Added | Side |
|---|---|---|
| `ModHost` initialization | 1 line | Stride fork (1-line hook) |
| `EnableStatePersistence` call | 1 line | Stride fork (1-line hook) |

### Where in the Game Lifecycle

```
Game.Initialize()
  ├── base.Initialize()          // existing Stride logic
  ├── LoadContent()              // existing Stride logic
  ├── ModHost.Initialize(this);  // ← MODULUS HOOK
  └── EnableStatePersistence();  // ← MODULUS HOOK
```

### `ModHost.Initialize(this)`

Creates the singleton `ModHost` instance that manages mod loading, unloading, and lifecycle. It receives a reference to the `Game` instance so it can access the scene system, content manager, and scripting services.

```csharp
// 1-line hook inside Game.Initialize():
ModHost.Initialize(this);
```

### `EnableStatePersistence()`

Activates the serialization bridge that snapshots and restores entity/component state across mod reloads. Without this call, hot-reloading a mod would lose all runtime state.

```csharp
// 1-line hook inside Game.Initialize():
StatePersistence.EnableStatePersistence();
```

### Merge Surface

Both lines are inserted **after** the existing `LoadContent()` call block. Because upstream Stride rarely touches the tail of `Initialize()`, merge conflicts are exceptionally rare.

---

## 5. HttpApi System

Three new files provide an embedded HTTP API that allows external tools (editors, CLI, CI) to interact with a running Modulus game at development time.

### 5.1 EngineHttpServer

**File:** `Stride.Http/EngineHttpServer.cs` *(new file)*

Lightweight HTTP listener that wraps `System.Net.HttpListener` and exposes a request-dispatch pipeline. Runs on **port 9876** by default (configurable via `Modulus:HttpPort`).

```csharp
public class EngineHttpServer : IDisposable
{
    private HttpListener _listener;
    private readonly int _port;

    public EngineHttpServer(int port = 9876) { _port = port; }

    public void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        // Spawns background thread for AcceptLoop
    }

    // ... AcceptLoop, routing, Dispose
}
```

### 5.2 EditorHttpServer

**File:** `Stride.Http/EditorHttpServer.cs` *(new file)*

Extends `EngineHttpServer` with editor-specific routes (asset manipulation, scene graph inspection, mod management). Shares the same port but adds a `/editor` prefix namespace.

### 5.3 HttpApiSystem

**File:** `Stride.Http/HttpApiSystem.cs` *(new file)*

Stride `GameSystem` that owns the server lifecycle — starts on `Initialize`, stops on `Destroy`. Registers all route handlers.

```csharp
public class HttpApiSystem : GameSystemBase
{
    public HttpApiSystem(IServiceRegistry registry) : base(registry)
    {
        Enabled = true;
        UpdateOrder = -1000; // run early
    }

    public override void Initialize()
    {
        base.Initialize();
        _server = new EngineHttpServer();
        RegisterRoutes();
        _server.Start();
    }

    private void RegisterRoutes()
    {
        // Delegates to individual route handler classes (see §6)
    }
}
```

### 1-Line Hook in Game.cs

```csharp
// Game.Initialize() or Game constructor:
Services.AddService(new HttpApiSystem(Services));
```

This registers the system as a game service so Stride's main loop invokes its `Update` and `Draw` automatically.

---

## 6. Route Handlers

All route handlers live under `Stride.Http/Routes/` (new directory). Each handler is a static class with methods decorated by route metadata (attribute-based or convention-based).

### Scene Routes

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/scene` | Returns the current scene graph as JSON |
| `GET` | `/api/scene/entities` | Lists all root entities |
| `GET` | `/api/scene/entity/{id}` | Returns a single entity's component data |
| `POST` | `/api/scene/entity/{id}/component` | Adds a component to an entity |
| `DELETE` | `/api/scene/entity/{id}` | Removes an entity from the scene |

### Asset Routes

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/assets` | Lists all loaded assets with type info |
| `GET` | `/api/assets/{id}` | Returns asset metadata and path |
| `POST` | `/api/assets/reload/{id}` | Hot-reloads a specific asset |
| `POST` | `/api/assets/reload-all` | Reloads all dirty assets |

### Mod Routes

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/mods` | Lists all loaded mods with status |
| `GET` | `/api/mods/{name}` | Returns details for a specific mod |
| `POST` | `/api/mods/load` | Loads a mod from a given DLL path |
| `POST` | `/api/mods/unload/{name}` | Unloads a mod and cleans up registrations |
| `POST` | `/api/mods/reload/{name}` | Unloads then loads a mod (hot-reload) |

### Editor Routes

| Method | Path | Description |
|---|---|---|
| `GET` | `/editor/api/properties/{entityId}` | Returns editable property tree for the editor |
| `POST` | `/editor/api/properties/{entityId}` | Applies property changes from the editor |
| `POST` | `/editor/api/screenshot` | Captures the current frame as PNG |
| `GET` | `/editor/api/selection` | Returns the currently selected entities |

### Debug Routes

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/debug/log` | Streams recent log entries (SSE or JSON) |
| `GET` | `/api/debug/profiler` | Returns frame timing and memory stats |
| `POST` | `/api/debug/breakpoint` | Sets a conditional breakpoint in a script |
| `DELETE` | `/api/debug/breakpoint/{id}` | Clears a breakpoint |

### Render Routes

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/render/info` | Returns GPU adapter, resolution, FPS |
| `POST` | `/api/render/settings` | Applies runtime render setting changes |
| `GET` | `/api/render/passes` | Lists active render passes |

### Status Routes

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/status` | Returns engine version, uptime, mod count |
| `GET` | `/api/status/health` | Simple health-check endpoint (200 OK) |
| `GET` | `/api/status/capabilities` | Returns feature flags and plugin list |

---

## Merge Conflict Guide

### When Upstream Stride Updates

1. **Check `AssemblyRegistry.cs`** — If Stride changes the `Register` method signature or internal data structures, update `Unregister` to match.
2. **Check `DataSerializerFactory.cs`** — If Stride changes how serializers are stored, update `ClearAssemblySerializers` to iterate the new collection.
3. **Check `Game.cs`** — If Stride restructures `Initialize()`, re-insert the two 1-line hooks at the appropriate lifecycle point (after `LoadContent`).
4. **All other files are additive** (`HttpApi`, route handlers, `ModHost`, `StatePersistence`) — they live in new files and never conflict.

### Conflict Probability Matrix

| File | Conflict Risk | Reason |
|---|---|---|
| `AssemblyRegistry.cs` | Low | New method + new event; no existing lines touched |
| `DataSerializerFactory.cs` | Low-Medium | Alias override is a single-line change to an existing branch |
| `TypeDescriptorFactory.cs` | Low | New method only |
| `Game.cs` | Low | Two 1-line insertions near end of `Initialize()` |
| `HttpApi*` (new files) | None | Additive, no upstream equivalent |
| Route handlers (new files) | None | Additive, no upstream equivalent |

### Best Practices

- **Rebase, don't merge.** When pulling upstream Stride changes, `git rebase` onto the new Stride `HEAD` so you can resolve conflicts incrementally.
- **Keep hooks atomic.** If a hook line conflicts, it's trivial to re-place one line versus resolving a 50-line block.
- **Run the test suite** after every rebase: `dotnet test Stride.sln` plus the Modulus-specific `dotnet test Modulus.Tests.sln`.

---

## Appendix: File Inventory

| File | Status | Modulus Namespace |
|---|---|---|
| `Stride.Core/Serialization/AssemblyRegistry.cs` | Modified (fork) | `Stride.Core.Serialization` |
| `Stride.Core/Serialization/DataSerializerFactory.cs` | Modified (fork) | `Stride.Core.Serialization` |
| `Stride.Core/Serialization/TypeDescriptorFactory.cs` | Modified (fork) | `Stride.Core.Serialization` |
| `Stride.Engine/Game.cs` | Modified (fork) | `Stride.Engine` |
| `Stride.Http/EngineHttpServer.cs` | **New** | `Stride.Http` |
| `Stride.Http/EditorHttpServer.cs` | **New** | `Stride.Http` |
| `Stride.Http/HttpApiSystem.cs` | **New** | `Stride.Http` |
| `Stride.Http/Routes/*.cs` | **New** | `Stride.Http.Routes` |

---

*Last updated: 2026-06-09 — Modulus Engine contributors.*
