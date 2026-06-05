# Modulus Engine: Stride Fork & Modding Layer — Final Plan

## Vision

A modding-focused game engine built on Stride, enabling hot-swappable, cross-game
mods via AssemblyLoadContext isolation. Two layers of abstraction:
1. **Game developers** — use Stride's full API unchanged
2. **Gamers (mod users)** — install/manage mods via simple UI

## Core Principles

- Keep everything Stride provides — only modify what's necessary for modding
- Don't break what works — assume our changes are inferior to Stride's
- Service-based replacement for swappable systems (Physics, Audio, Navigation, Rendering) — deferred to separate milestones after modding foundation works
- Flexible API versioning — mods rarely break; version bumps minimize incompatibility
- Cross-game mods are the unique selling point
- No sandboxing in v1 — same model as NeoForge

## All Decisions

| Decision | Answer |
|---|---|
| **Base** | Fork full Stride repo (~120+ projects) |
| **Editor** | Keep Game Studio (WPF), extend with mod plugin |
| **Physics/Audio/Navigation** | Make replaceable via services (deferred — separate milestone after modding foundation works) |
| **Rendering** | Make replaceable via services (deferred — separate milestone) |
| **ECS** | Leave as-is, too foundational |
| **Serialization** | Add thin registration hooks |
| **ContentManager** | Add multi-source resolution |
| **Asset pipeline** | Pre-compiled mods initially |
| **API versioning** | Flexible — mods rarely break; version bumps minimize incompatibility |
| **Cross-game modding** | Mandatory |
| **Sandboxing** | Not in v1 |
| **Native dependencies** | Mods are managed C# only — no native DLLs in mods |
| **Load order** | Deterministic via topological sort + cycle detection |
| **ABI stability** | `Modulus.Modding.Api` is the stable ABI; all other assemblies are internal |
| **Editor hot-reload** | Mods load at runtime only; editor reload does NOT trigger mod reload |
| **Crash safety** | Exceptions caught, logged, mod disabled; game never crashes from a mod |
| **Fork management** | Major version syncs, main+dev branches |
| **MCP server** | Phase 2, C#, HTTP API, dotnet tool |
| **MCP repo** | `ModulusEngine.MCPServer` (separate repo) |
| **Documentation** | HTML docs via DocFX, offline + online |
| **Test mods** | Component adder first, then system replacer, then cross-game |

---

## Phase 0: Fork Setup (Single PR)

### Actions

1. Fork `https://github.com/stride3d/stride` → `Modulus-Engine/modulus`
2. Clone locally, set up remotes
3. Build full solution
4. Run existing tests
5. Verify Game Studio works

### Commands

```bash
git clone https://github.com/stride3d/stride.git
cd stride
git remote add origin https://github.com/Modulus-Engine/modulus.git
git remote add upstream https://github.com/stride3d/stride.git
git checkout -b dev
git push -u origin dev
dotnet build build/Stride.sln
dotnet test build/Stride.Tests.Simple.slnf
```

### Acceptance Criteria

- [ ] Full repo builds with 0 errors
- [ ] All existing tests pass
- [ ] Game Studio launches and renders viewport

---

## Phase 1: In-Engine HTTP API

### Overview

The MCP server (Phase 2) calls HTTP endpoints on the engine. This phase builds the engine-side server. Stride has no built-in HTTP server, but it does have a TCP-based `ConnectionRouter` for debugging. We extend this with a simple embedded HTTP listener that exposes engine state for agent control.

### Architecture

```
Agent/IDE (Claude, VS Code, Cursor)
    │
    ▼
ModulusEngine.MCPServer (Phase 2 — separate process)
    │
    ▼ HTTP (localhost:9876)
Engine HTTP API Server (this phase — embedded in Stride Engine)
    │
    ▼
Stride Engine (ECS, ContentManager, Scene, Editor)
```

### API Surface

| Method | Endpoint | Purpose | v1 Status |
|---|---|---|---|
| GET | `/api/v1/status` | Engine health, project, renderer info | Implemented |
| GET | `/api/v1/scene/entities` | List entities in current scene | Implemented |
| GET | `/api/v1/scene/entities/{id}` | Get entity details (components, transform) | Implemented |
| POST | `/api/v1/scene/entities` | Create new entity | Implemented |
| DELETE | `/api/v1/scene/entities/{id}` | Delete entity | Implemented |
| GET | `/api/v1/scene/scenes` | List scenes in project | Implemented |
| POST | `/api/v1/scene/scenes` | Create new scene | Implemented |
| GET | `/api/v1/asset/list` | List assets by type | Implemented |
| POST | `/api/v1/asset/import` | Import asset file | Implemented |
| POST | `/api/v1/asset/build` | Build asset pipeline | Implemented |
| POST | `/api/v1/mod/install` | Install .modpkg | **501 stub — pending Phase 3** |
| POST | `/api/v1/mod/enable/{id}` | Enable mod | **501 stub — pending Phase 3** |
| POST | `/api/v1/mod/disable/{id}` | Disable mod | **501 stub — pending Phase 3** |
| POST | `/api/v1/mod/reload/{id}` | Hot-reload mod | **501 stub — pending Phase 4** |
| GET | `/api/v1/mod/list` | List installed mods | **501 stub — pending Phase 4** |
| GET | `/api/v1/editor/status` | Editor window status | Implemented |
| POST | `/api/v1/editor/launch` | Launch editor | Implemented |
| POST | `/api/v1/editor/screenshot` | Capture viewport screenshot | Implemented |
| POST | `/api/v1/render/shader/compile` | Compile shader | **501 stub — pending Phase 4** |
| GET | `/api/v1/render/shaders` | List compiled shaders | **501 stub — pending Phase 4** |
| GET | `/api/v1/debug/logs` | Get recent log entries | Implemented |
| GET | `/api/v1/debug/console` | Get console output | Implemented |
| GET | `/api/debug/validation` | Get Vulkan validation errors |

### Implementation

**Files to create:**
- `sources/engine/Stride.Engine/HttpApi/EngineHttpServer.cs` — HTTP listener, routing, request handling
- `sources/engine/Stride.Engine/HttpApi/Routes/StatusRoutes.cs` — /api/status handler
- `sources/engine/Stride.Engine/HttpApi/Routes/SceneRoutes.cs` — /api/scene/* handlers
- `sources/engine/Stride.Engine/HttpApi/Routes/AssetRoutes.cs` — /api/asset/* handlers
- `sources/engine/Stride.Engine/HttpApi/Routes/ModRoutes.cs` — /api/mod/* handlers
- `sources/engine/Stride.Engine/HttpApi/Routes/EditorRoutes.cs` — /api/editor/* handlers
- `sources/engine/Stride.Engine/HttpApi/Routes/RenderRoutes.cs` — /api/render/* handlers
- `sources/engine/Stride.Engine/HttpApi/Routes/DebugRoutes.cs` — /api/debug/* handlers

**How it works:**
1. Engine starts, HTTP server listens on `http://localhost:9876`
2. Routes are registered as simple handlers
3. Handlers access engine state (ECS, ContentManager, Scene) via injected services
4. All responses are JSON
5. Server runs on a background thread, non-blocking

**Implementation Note: Asset Build vs. Pre-Compiled**

The decisions table mandates "Pre-compiled mods initially." The `/api/asset/build` endpoint does NOT attempt in-process compilation — Stride's asset pipeline requires `AssetCompiler.exe` as an external process. Attempting in-process compilation would introduce threading conflicts with the running game instance.

```csharp
// /api/asset/build implementation: process wrapper, not in-process compiler
public async Task<object> BuildAsset(HttpListenerRequest req)
{
    var assetPath = GetAssetPath(req);
    var result = await Process.Start(new ProcessStartInfo
    {
        FileName = "AssetCompiler.exe",
        Arguments = $"--build \"{assetPath}\" --platform windows",
        WorkingDirectory = _gameProjectPath,  // MUST be locked to game project root
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    }).WaitForExitAsync();

    return new { success = result.ExitCode == 0, output = result.StandardOutput.ReadToEnd() };
}
```

**Critical: Working directory.** `AssetCompiler.exe` relies on relative paths and temporary cache directories (`cache/`, `obj/` sub-folders). The `ProcessStartInfo.WorkingDirectory` must be explicitly locked to the game project root directory. Without this, the compiler drops outputs in the wrong location or collides with active file streams from the running engine.

The `/api/asset/import` endpoint similarly delegates to Stride's existing import tools. In v1, assets are pre-compiled by mod developers before packaging into `.modpkg` — these endpoints exist for developer convenience, not runtime mod loading.

**Key design decisions:**
- No external dependencies — use `System.Net.HttpListener` (built into .NET)
- No authentication in v1 — localhost-only
- Minimal routing — simple path matching, no middleware framework
- Singleton per engine instance

**Example implementation skeleton:**
```csharp
public class EngineHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly IServiceRegistry _services;
    private readonly Dictionary<string, Func<HttpListenerRequest, Task<object>>> _routes = new();

    public EngineHttpServer(IServiceRegistry services, int port = 9876)
    {
        _services = services;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void RegisterRoute(string method, string path, Func<HttpListenerRequest, Task<object>> handler)
    {
        // Match by prefix for parameterized routes:
        // "/api/v1/scene/entities/{id}" → matches "/api/v1/scene/entities/abc123"
        // Extract {id} from the URL tail and pass as query parameter
        _routes[$"{method} {path}"] = handler;
    }

    private string? MatchRoute(string method, string url, out Dictionary<string, string> pathParams)
    {
        pathParams = new();
        foreach (var (key, handler) in _routes)
        {
            var parts = key.Split(' ', 2);
            if (parts[0] != method) continue;
            if (TryMatchTemplate(parts[1], url, pathParams)) return key;
        }
        return null;
    }

    private bool TryMatchTemplate(string template, string url, Dictionary<string, string> params)
    {
        var tSegs = template.Split('/');
        var uSegs = url.Split('/');
        if (tSegs.Length != uSegs.Length) return false;
        for (int i = 0; i < tSegs.Length; i++)
        {
            if (tSegs[i].StartsWith("{") && tSegs[i].EndsWith("}"))
                params[tSegs[i][1..^1]] = uSegs[i];
            else if (tSegs[i] != uSegs[i])
                return false;
        }
        return true;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listener.Start();
        while (!ct.IsCancellationRequested)
        {
            var ctx = await _listener.GetContextAsync();
            _ = Task.Run(() => HandleRequestAsync(ctx));
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            var match = MatchRoute(ctx.Request.HttpMethod,
                ctx.Request.Url!.AbsolutePath, out var pathParams);
            if (match != null && _routes.TryGetValue(match, out var handler))
            {
                var result = await handler(ctx.Request);
                var json = JsonSerializer.Serialize(result);
                ctx.Response.ContentType = "application/json";
                using var writer = new StreamWriter(ctx.Response.OutputStream);
                writer.Write(json);
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[HttpApi] {ctx.Request.HttpMethod} {ctx.Request.Url} failed: {ex}");
            ctx.Response.StatusCode = 500;
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    public void Dispose() => _listener.Stop();
}
```

**Integration with Stride game loop:**
```csharp
// In Game.cs or a custom GameSystem
public class HttpApiSystem : GameSystemBase
{
    private readonly EngineHttpServer _server;

    public HttpApiSystem(IServiceRegistry registry)
        : base(registry)
    {
        _server = new EngineHttpServer(registry, 9876);
        // Register routes
        _server.RegisterRoute("GET", "/api/status", StatusRoutes.GetStatus);
        _server.RegisterRoute("GET", "/api/scene/entities", SceneRoutes.GetEntities);
        // ... all other routes
    }

    protected override void LoadContent()
    {
        _ = _server.StartAsync(CancellationToken.None);
    }

    public override void Update(GameTime gameTime)
    {
        // HTTP server runs on background threads, no per-frame work needed
    }
}
```

### Test

**Unit test:**
```csharp
[Test]
public async Task StatusEndpoint_ReturnsEngineInfo()
{
    var server = new EngineHttpServer(services, 19876); // Test port
    server.RegisterRoute("GET", "/api/status", r => Task.FromResult<object>(new { initialized = true }));
    await server.StartAsync(cts.Token);

    var client = new HttpClient();
    var response = await client.GetStringAsync("http://localhost:19876/api/status");
    var json = JsonSerializer.Deserialize<JsonElement>(response);

    Assert.True(json.GetProperty("initialized").GetBoolean());
}
```

**Integration test:** Start engine with HTTP API, call `/api/status`, verify response.
**Smoke test:** Launch editor, call `/api/status`, verify renderer name shows.

### Implementation Note: Screenshot Capture

`/api/editor/screenshot` reads pixels from the GPU backbuffer. Doing this synchronously on the game thread causes a visible hitch (stall-the-world GPU readback). **Handle readbacks asynchronously:**

```csharp
// Enqueue a readback command on the game thread
public void RequestScreenshot()
{
    var tcs = new TaskCompletionSource<byte[]>();
    _pendingWork.Enqueue(() =>
    {
        // Record readback to staging buffer on command list
        _commandList.CopyTextureToBuffer(backbuffer, stagingBuffer);
        // Submit and signal fence
        _device.SubmitCommandList(_commandList);
        _device.SignalFence(_readbackFence);
        // Resume on background thread when GPU is done
        Task.Run(() =>
        {
            _device.WaitForFence(_readbackFence);
            tcs.SetResult(ReadStagingBuffer(stagingBuffer));
        });
    });
    return tcs.Task;  // returns immediately, doesn't block game loop
}
```

**Graphics context safety:** Stride requires `CommandList` recording during `Draw`, not `Update`. The unified queue design uses two queues — one drained in `Update` (ECS work), one in `Draw` (GPU work).

```csharp
// In HttpApiSystem — two queues drained on different phases:
private ConcurrentQueue<WorkItem> _updateQueue = new();  // ECS reads/writes (Update)
private ConcurrentQueue<WorkItem> _drawQueue = new();    // GPU commands (Draw)

private class WorkItem
{
    public Action Action { get; set; } = null!;
    public TaskCompletionSource? Completion { get; set; }  // null = fire-and-forget
}

// Route handler: enqueue to Update, wait with timeout
public Task<object> CreateEntity(HttpListenerRequest req)
{
    var tcs = new TaskCompletionSource<object>();
    _updateQueue.Enqueue(new WorkItem
    {
        Action = () =>
        {
            var entity = new Entity { Name = req.QueryString["name"] };
            SceneManager.CurrentScene.Entities.Add(entity);
            tcs.SetResult(new { id = entity.Id });
        },
        Completion = tcs
    });
    return tcs.Task.WithTimeout(TimeSpan.FromSeconds(5),
        () => new { error = "Engine busy, try again" });
}

// Screenshot is queued for Draw
public void RequestScreenshot()
{
    _drawQueue.Enqueue(new WorkItem { Action = () => { /* record readback here */ } });
}

// Drained during HttpApiSystem.Update()
public override void Update(GameTime gameTime)
{
    while (_updateQueue.TryDequeue(out var item)) item.Action();
}

// Drained during HttpApiSystem.Draw()
public override void Draw(RenderContext context)
{
    while (_drawQueue.TryDequeue(out var item)) item.Action();
}
```

**Deadlock prevention:** The route handler awaits `tcs.Task` on the HTTP background thread while the game thread drains the queue. If the game thread is paused (loading a scene, blocked on I/O), that HTTP connection hangs indefinitely. The `.WithTimeout()` extension ensures the API returns a 504 after 5 seconds rather than hanging forever.

**Thread-safe reads:** GET requests are just as dangerous as POSTs. Stride's ECS and Scene Graph are not thread-safe for concurrent read/write operations. If an HTTP background thread enumerates `SceneManager.CurrentScene.Entities` while the game loop is mid-frame mutating transforms or structural arrays, the engine crashes with collection modification exceptions or silent memory corruption. **ALL requests — GET and POST — must marshal work onto the game thread:**

```csharp
// GET route handler — marshaled to game thread, same as POST
public Task<object> GetEntities(HttpListenerRequest req)
{
    var tcs = new TaskCompletionSource<object>();
    _updateQueue.Enqueue(new WorkItem
    {
        Action = () =>
        {
            // Now safe — running on game thread during Update()
            var entities = SceneManager.CurrentScene.Entities
                .Select(e => new { e.Id, e.Name, components = e.Components.Count })
                .ToList();
            tcs.SetResult(new { entities });
        },
        Completion = tcs
    });
    return tcs.Task.WithTimeout(TimeSpan.FromSeconds(5),
        () => new { error = "Engine busy, try again" });
}
```

### Acceptance Criteria

- [ ] Engine starts HTTP server on port 9876
- [ ] All API endpoints respond correctly
- [ ] GET endpoints return valid JSON
- [ ] POST endpoints accept JSON body
- [ ] Engine shutdown cleans up HTTP server
- [ ] Server doesn't block game loop

---

## Phase 2: MCP Server (Separate Repo: `ModulusEngine.MCPServer`)

### Overview

**Language:** C# (.NET 10, `ModelContextProtocol` NuGet)
**Transport:** HTTP (calls engine's existing HTTP API)
**Distribution:** `dotnet tool install -g ModulusEngine.MCPServer` from NuGet.org

**v1 Scope (Slimmed):** 30+ tools is ambitious. v1 should ship ~15 tools that prove connectivity:
- `build_*`, `test_*` (3 tools) — essential for CI/development
- `scene_*` (4 tools — get_entities, create_entity, delete_entity, set_transform) — core scene workflow
- `editor_*` (3 tools — get_status, take_screenshot, set_camera) — visual verification
- `debug_*` (2 tools — get_logs, get_console) — debugging
- `mod_*` (3 tools — list_mods, install_mod, enable_mod) — modding MVP

Remaining tools (`asset_*`, `render_*`, `component_*`, advanced `mod_*`) added in v2+ after proving the core workflow.

### File Structure

```
ModulusEngine.MCPServer/
├── Program.cs                              # Host setup, MCP server registration
├── ModulusEngine.MCPServer.csproj          # PackAsTool=true, ToolCommandName=modulus-mcp
├── Bridge/
│   ├── EngineBridge.cs                    # HTTP client to engine API
│   └── HTTPClient.cs                      # HttpClient wrapper
├── Tools/
│   ├── BuildTools.cs                      # build_project, test_project, clean_build
│   ├── ProjectTools.cs                    # create_project, list_projects, open_project
│   ├── SceneTools.cs                      # list_scenes, create_scene, get_entities, create_entity, delete_entity
│   ├── ComponentTools.cs                  # add_component, remove_component, get_components, set_property
│   ├── AssetTools.cs                      # list_assets, import_asset, build_assets
│   ├── ModTools.cs                        # list_mods, install_mod, enable_mod, disable_mod, reload_mod, create_mod
│   ├── EditorTools.cs                     # launch_editor, take_screenshot, get_status
│   ├── RenderTools.cs                     # list_shaders, compile_shader
│   └── DebugTools.cs                      # get_logs, get_console
├── Resources/
│   ├── SceneResources.cs                  # scene://entities/{scene}
│   ├── AssetResources.cs                  # asset://list/{type}
│   └── ShaderResources.cs                 # shader://source/{name}
└── Docs/
    └── (HTML docs via DocFX)
```

### Tool Specification

```csharp
[McpServerToolType]
public class SceneTools
{
    [McpServerTool, Description("Get all entities in the current scene. Returns JSON array.")]
    public async Task<string> GetEntities(
        [Description("Optional tag filter")] string? tag = null)
    {
        // Calls: GET http://localhost:9876/api/scene/entities?tag=...
    }

    [McpServerTool, Description("Create a new entity with optional transform.")]
    public async Task<string> CreateEntity(
        [Description("Name for the new entity")] string name,
        [Description("Initial X position")] double x = 0.0,
        [Description("Initial Y position")] double y = 0.0,
        [Description("Initial Z position")] double z = 0.0,
        [Description("Optional parent entity ID")] string? parentId = null)
    {
        // Calls: POST http://localhost:9876/api/scene/entities
    }
}
```

### Complete Tool List

| Category | Tools |
|---|---|
| **build_*** | `build_project`, `test_project`, `clean_build` |
| **project_*** | `create_project`, `list_projects`, `open_project` |
| **scene_*** | `list_scenes`, `create_scene`, `get_entities`, `create_entity`, `delete_entity`, `set_transform` |
| **component_*** | `add_component`, `remove_component`, `get_components`, `set_property` |
| **asset_*** | `list_assets`, `import_asset`, `build_assets`, `get_asset_info` |
| **mod_*** | `list_mods`, `install_mod`, `enable_mod`, `disable_mod`, `reload_mod`, `create_mod` |
| **editor_*** | `launch_editor`, `take_screenshot`, `get_status`, `set_camera` |
| **render_*** | `list_shaders`, `compile_shader`, `get_validation_errors` |
| **debug_*** | `get_logs`, `get_console`, `get_diagnostics` |

### Resources (Read-Only Context)

| URI Scheme | Purpose |
|---|---|
| `scene://entities/{sceneName}` | Entity list for a scene |
| `scene://components/{entityId}` | Components on an entity |
| `asset://list/{type}` | Assets by type |
| `shader://source/{name}` | Shader source code |
| `config://engine/settings` | Engine configuration |
| `editor://selection` | Currently selected entities |

### Acceptance Criteria

- [ ] `modulus-mcp` starts and connects to engine
- [ ] All tools callable via MCP client
- [ ] All resources resolvable
- [ ] Error messages clear and helpful

---

## Phase 3: Modding Foundation

### Key Insight

Stride's resolution chains funnel through `FileProvider` and `AssemblyRegistry`.
The existing `DataSerializerFactory.RegisterSerializationAssembly` already does
most of the work. We add thin wrappers, not replacements.

### 3.1 Mod Assembly Loading

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModHost.cs` — mod lifecycle manager
- `sources/engine/Stride.Engine/Modding/ModLoadContext.cs` — collectible ALC per mod
- `sources/engine/Stride.Engine/Modding/ModPackage.cs` — represents a loaded mod
- `sources/engine/Stride.Engine/Modding/ModManifest.cs` — parses mod.json
- `sources/engine/Stride.Engine/Modding/ModDiscovery.cs` — scans mods/ directory
- `sources/engine/Stride.Engine/Modding/ModValidator.cs` — validates mod before loading

**How it works:**
1. ModHost scans `mods/` directory for mod packages
2. Each mod gets its own AssemblyLoadContext (collectible)
3. Mod assemblies are loaded into the ALC
4. Mod types are discovered via reflection
5. On unload, ALC is unloaded, types are garbage collected

**Cross-ALC type identity:** If Mod A depends on `Modulus.StandardLibrary`, Mod A's ALC must NOT load its own copy of `StandardLibrary.dll`. Doing so creates two distinct runtime types with identical bytecode — the engine rejects them as type mismatches. `ModLoadContext` implements a parent/peer resolution policy:

```csharp
protected override Assembly? Load(AssemblyName assemblyName)
{
    // Route core API to default context (engine's ALC)
    if (assemblyName.Name == "Modulus.Modding.Api")
        return null; // fallback to default load behavior

    // Route shared dependencies to their active ALCs (preserves type identity)
    if (_host.TryGetLoadedSharedAssembly(assemblyName.Name, out var sharedAssembly))
        return sharedAssembly;

    return null; // standard isolation for mod-private assemblies
}
```

**Test:** Unit test — load mod assembly, verify types accessible, unload, verify types gone.

### 3.2 Mod Type Registration

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModTypeRegistry.cs` — registers mod types

**Files to modify:**
- `sources/core/Stride.Core/Reflection/AssemblyRegistry.cs` — add `Unregister(Assembly)` method
- `sources/core/Stride.Core/Serialization/DataSerializerFactory.cs` — expose alias collision override

**How it works:**
1. When mod loads, scan for EntityComponent subclasses
2. Register with DataSerializerFactory for serialization
3. Register with AssemblyRegistry for type resolution
4. On unregister, remove registrations

**Test:** Unit test — load mod with custom component, verify serialization round-trip.

### 3.3 Mod System Registration

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModSystemRegistry.cs` — registers mod systems

**How it works:**
1. When mod loads, scan for EntityProcessor subclasses
2. Register with EntityManager via `[DefaultEntityComponentProcessor]` attribute
3. Systems start processing automatically
4. On unregister, systems stop and are removed

**Test:** Unit test — load mod with custom system, verify Update() called.

### 3.4 Multi-Source Content Manager

**Files to create:**
- `sources/core/Stride.Core.Serialization/Modding/CompositeFileProviderService.cs` — multi-source file provider
- `sources/engine/Stride.Engine/Modding/ModContentManager.cs` — content resolution wrapper

**How it works:**
1. Each mod gets its own DatabaseFileProvider
2. CompositeFileProviderService fans out `FileExists`/`OpenStream` to all providers
3. Resolution order: mod providers first, then game provider
4. Mods can override game assets by providing assets with the same URL

**Test:** Unit test — load asset from mod, verify resolution. Integration test — load model from mod, add to scene, verify it renders.

### Acceptance Criteria

- [ ] Mod assemblies load via AssemblyLoadContext
- [ ] Mod component types register with serializer
- [ ] Mod systems register with ECS
- [ ] Mod assets load and render
- [ ] All registrations clean up on unload
- [ ] `IModEventBus` interface defined (used in Phase 4 but required before ModEventBus implementation)

---

## Phase 4: Mod Lifecycle

### 4.1 Mod Lifecycle Management

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModLifecycleManager.cs` — tracks and cleans up mod resources
- `sources/engine/Stride.Engine/Modding/ModScope.cs` — per-mod resource tracking
- `sources/engine/Stride.Engine/Modding/ModReloadResult.cs` — enum: Success, RequiresRestart, TemporarilyDisabled

**How it works:**
1. Track all entities created by a mod
2. Track all event subscriptions
3. Track all component instances
4. On unload: destroy entities, unsubscribe events, remove components, unload ALC

**Test:** Unit test — load mod, create entities, subscribe events, unload mod, verify cleanup. Memory test — load/unload 100 times, no leaks.

### 4.1.1 ALC Unload — The Hardest Technical Problem

**This is the single hardest technical problem in the entire project.**

A collectible `AssemblyLoadContext` can only be garbage collected after `Unload()` when **zero strong references** exist from outside the ALC to types defined inside it. This includes:

- **Reflection caches** — `Assembly.GetTypes()`, `Type.GetMethods()`, cached `MethodInfo` objects
- **Type dictionaries** — Stride's `AssemblyRegistry.AssemblyNameToAssembly`, `DataSerializerFactory.AvailableAssemblySerializers`
- **Event handler delegates** — any event subscription where the handler is a mod type
- **Serializable component instances** — any `EntityComponent` from a mod still in the scene
- **Static field references** — any static field in engine code that holds a mod object

**The GC does NOT collect immediately after `Unload()`.** The GC is non-deterministic. After calling `Unload()`, you must:

1. **Null all strong references** from outside the ALC to mod types
2. **Call `GC.Collect()` and `GC.WaitForPendingFinalizers()`** explicitly — possibly multiple times
3. **Verify with `GC.GetTotalMemory()` before/after** to detect leaks
4. **Use `WeakReference` to track** whether the ALC was actually collected

**Explicit cleanup checklist (per mod unload):**
```
[] Cancel all running MicroThreads from the mod's assembly (query ScriptSystem, force-cancel)
[] Unregister from DataSerializerFactory (UnregisterSerializationAssembly)
[] Unregister from AssemblyRegistry (UnregisterAssembly)
[] Remove all mod EntityProcessor instances from EntityManager
[] Destroy all entities with mod components
[] Null all cached MethodInfo/PropertyInfo references to mod types
[] Unsubscribe all event handlers from mod types
[] Call mod.Dispose() on all IMod instances
[] Null any static references to mod types
[] Call AssemblyLoadContext.Unload()
[] Null the ModLoadContext reference itself
[] Call GC.Collect() + GC.WaitForPendingFinalizers()
[] Call GC.Collect() again (some types need two GC cycles)
[] Verify WeakReference to ALC is dead
```

**MicroThread trap:** Stride's `ScriptSystem` uses an internal pooling mechanism called MicroThreads for asynchronous behaviors, background waits, and system events. If a mod initializes a micro-thread that remains alive, suspended, or awaiting an engine signal, that thread's execution stack **maintains a strong reference to the mod's types**, preventing ALC collection. Before unload, query `ScriptSystem.Scheduler` for all running `SchedulerEntry` instances that reference types in the mod's assembly, and call `.Cancel()` on each.

**Implementation note:** `MicroThreadScheduler.RunningEntries` is not exposed publicly in Stride's API. This requires one of: (a) a one-line Stride source modification to expose the field as `public`, (b) reflection to access the private field (`typeof(MicroThreadScheduler).GetField("microThreads", BindingFlags.NonPublic | BindingFlags.Instance)`), or (c) tracking mod-initiated micro-threads at creation time via a wrapper around `Scheduler.Add()`. Option (a) is preferred for maintainability.

**Continuation state trap:** Simply canceling running micro-thread entries can leave dangling `TaskCompletionSource` objects or async state machine contexts allocated on the engine's scheduler if the script was `await`-ing an external trigger. Complement cancellation with an engine-side sweep of Stride's `CustomUpdate` and `TransformProcessor` systems to drop any delegates originating from the mod's assembly:

```csharp
private void SweepModDelegates(Assembly modAssembly)
{
    var scheduler = _services.GetService<ScriptSystem>().Scheduler;
    foreach (var entry in scheduler.RunningEntries)
    {
        if (entry.Action?.Method?.DeclaringType?.Assembly == modAssembly)
            entry.Cancel();
    }
    // Also sweep entity processor delegates cached by EntityManager
    var entityManager = _services.GetService<SceneSystem>().SceneInstance;
    foreach (var proc in entityManager.Processors)
    {
        if (proc.GetType().Assembly == modAssembly)
            entityManager.Processors.Remove(proc);
    }
}
```

```csharp
// In ModLifecycleManager:
private void CancelModMicroThreads(Assembly modAssembly)
{
    var scriptSystem = _services.GetService<ScriptSystem>();
    var scheduler = scriptSystem.Scheduler;
    foreach (var entry in scheduler.RunningEntries)
    {
        if (entry.Action?.Method?.DeclaringType?.Assembly == modAssembly)
            entry.Cancel();
    }
}
```

**A single leaked reference means the ALC never collects** — that DLL stays loaded in memory forever. Unload + reload 100 times = 100 copies of the DLL in memory if cleanup is incomplete.

**Testing strategy for ALC leaks:**
```csharp
[Test]
public void ModUnload_CleanlyCollects()
{
    var weakRef = LoadAndUnloadMod();
    
    // Force full GC
    for (int i = 0; i < 3; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
    
    Assert.Null(weakRef.Target, "ALC was not collected — leaked reference exists");
}

[Test]
public void ModLoadUnload_100Times_ConstantMemory()
{
    var beforeMemory = GC.GetTotalMemory(true);
    
    for (int i = 0; i < 100; i++)
    {
        LoadAndUnloadMod();
    }
    
    // Force full GC
    for (int i = 0; i < 3; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
    
    var afterMemory = GC.GetTotalMemory(true);
    var growth = afterMemory - beforeMemory;
    
    Assert.Less(growth, 10 * 1024 * 1024, $"Memory grew by {growth / 1024 / 1024}MB after 100 cycles");
}
```

### 4.1.2 Stride's Internal Reflection Caches

Stride heavily relies on internal caching for performance. `Stride.Core.Reflection.TypeDescriptorFactory` and internal serializer registries cache type metadata aggressively and don't drop references to unloaded types automatically. **Even if our cleanup checklist is perfect, Stride's own caches will hold ALC references.**

**Files to modify:**
- `sources/core/Stride.Core/Reflection/TypeDescriptorFactory.cs` — expose `ClearCache(Type)` or `ClearAssemblyCache(Assembly)`
- `sources/core/Stride.Core/Serialization/DataSerializerFactory.cs` — expose `ClearAssemblySerializers(Assembly)`

**Pattern:** Add public cache-clearing hooks that our `ModLifecycleManager` calls during unload:
```csharp
// In TypeDescriptorFactory (Stride.Core)
public void ClearAssemblyCache(Assembly assembly)
{
    foreach (var type in assembly.GetTypes())
        typeDescriptors.Remove(type);  // drop cached descriptors
}

// In DataSerializerFactory (Stride.Core)
public static void ClearAssemblySerializers(Assembly assembly)
{
    lock (Lock)
    {
        Version++;
        AvailableAssemblySerializers.Remove(assembly);
        // Rebuild DataSerializersPerProfile and DataContractAliasMapping
        // (same logic as UnregisterSerializationAssembly but callable externally)
        RebuildFromAvailableSerializers();
    }
}
```

**Test:** Unit test — load mod, trigger cache clear, verify `TypeDescriptorFactory` no longer holds references to mod types.

### 4.1.3 ALC Unload is Asynchronous

`AssemblyLoadContext.Unload()` is not synchronous — the actual unload happens on a background GC thread. For unit tests, wrap the test in a `[MethodImpl(MethodImplOptions.NoInlining)]` helper to ensure local variables referencing the ALC are cleared before the GC runs:

```csharp
[MethodImpl(MethodImplOptions.NoInlining)]
private static WeakReference LoadAndUnloadMod()
{
    var host = new ModHost();
    host.LoadMod("tests/mods/test-mod");
    host.UnloadMod("test-mod");
    return new WeakReference(host.GetModContext("test-mod"));
    // After return, no local variables hold ALC references — GC can collect
}
```

### 4.2 Mod State Persistence

**Files to create:**
- `sources/engine/Stride.Engine/Modding/IModSerializable.cs` — interface for mod state

**How it works:**
1. Mods implement IModSerializable to define save/load behavior
2. On save, ModHost calls each mod's Save()
3. On load, ModHost calls each mod's Load()
4. State is stored per-mod in a standard location

**Storage details:**
- Location: `%APPDATA%/ModulusEngine/mod-states/{modId}/state.dat`
- Format: binary (mod controls serialization via `IModSerializable`)
- Naming: one file per mod, keyed by mod ID
- Mod uninstall: state file is NOT deleted (user can reinstall and restore state)
- Mod upgrade: old state is loaded by new mod version; mods implement migration in `Load()`
- Version tag: `IMod.Load()` receives the mod version that wrote the state, enabling migration

**Test:** Unit test — save mod state, reload mod, verify state restored. Integration test — save state with v1.0 mod, upgrade to v1.1, verify state migrated.

### 4.3 Mod Event Bus

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModEventBus.cs` — inter-mod communication

**How it works:**
1. Mods subscribe to typed events
2. Mods publish events
3. Other subscribed mods receive events
4. Events are scoped to mod lifecycle

**Test:** Unit test — mod A publishes, mod B receives.

### 4.4 Crash-Safety Model

**Rule:** Exceptions in mod code are caught, logged, and the mod is disabled. The game never crashes from a mod.

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModExceptionHandler.cs` — wraps mod execution in try/catch

**Behavior by context:**

| Context | Exception Handling |
|---|---|
| Mod processor `Update()` | Catch exception, log, disable processor, continue game |
| Mod component method | Catch exception, log, disable component, continue game |
| Mod event handler | Catch exception, log, disable handler, continue game |
| Mod `Initialize()` | Catch exception, log, reject mod (don't load) |
| Mod `OnEnabled()`/`OnDisabled()` | Catch exception, log, mark mod as errored |

**Mod states:**
- `Loaded` — mod is loaded and enabled
- `Disabled` — mod is disabled by user
- `Errored` — mod hit an exception, disabled automatically, user can re-enable after fixing

**Implementation pattern:**
```csharp
public class ModExceptionHandler
{
    public void ExecuteModCode(IMod mod, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.Error($"Mod '{mod.Id}' threw exception: {ex.Message}");
            ModHost.DisableMod(mod.Id, reason: ModDisableReason.Error);
        }
    }
}
```

**Test:** Unit test — mod throws exception in processor, verify game continues, verify mod is disabled, verify mod can be re-enabled.

### 4.5 Orphan Component Handling

**Problem:** When a player saves a game containing custom components from Mod A, then uninstalls Mod A, Stride's deserializer encounters unknown type signatures and either fails the entire load or silently drops components — corrupting the save.

**Solution:** `OrphanComponent` — a generic wrapper that preserves unknown component data.

**Files to create:**
- `sources/engine/Stride.Engine/Modding/OrphanComponent.cs`

```csharp
[DataContract("OrphanComponent")]
public class OrphanComponent : EntityComponent
{
    public string OriginalTypeName { get; set; }
    public string ModId { get; set; }
    public byte[] RawData { get; set; }  // original serialized blob
}
```

**Behavior:**
1. Deserializer encounters unknown type → maps to `OrphanComponent`, preserves raw data
2. Game loads with orphaned components visible in inspector as "Missing: RotatingComponent (from mod X)"
3. User can save — orphan data is preserved, not lost
4. If mod is re-enabled: `OrphanComponent` is re-hydrated back into the original component type
5. If user saves without mod: orphan remains, game continues

### 4.6 GUID-Aware Content Pipeline

**Problem:** Stride's asset pipeline compiles assets into bundles (`.sdbundle`) with internal GUID mappings. A multi-source file provider that only evaluates raw file paths will miss assets referenced inside scenes by their compiled GUID.

**Solution:** Extend `CompositeFileProviderService` to also patch Stride's runtime `AssetManager` GUID lookup dictionary, not just the file stream router.

**How it works:**
1. Each mod's `.modpkg` contains a `asset-guids.json` manifest mapping virtual asset paths to their compiled GUIDs
2. On mod load, `CompositeFileProviderService` injects these GUIDs into Stride's `ContentManager` object database
3. Scene references to mod assets resolve correctly through the patched GUID registry
4. On mod unload, GUIDs are removed

**GUID collision check:** If a modder clones a game asset as a baseline and preserves its GUID, the `CompositeFileProviderService` would silently overwrite the host game's asset resolution. `ModValidator` (Phase 6.3) must cross-reference incoming `asset-guids.json` against the game's active GUID map during installation. If a collision is detected:

> `CRITICAL: Mod '{ModId}' attempts to overwrite a base game asset GUID '{CollisionGuid}'. Mod loading aborted.`

### 4.7 Shader Extraction from Mods

**Problem:** Mods may include custom shaders (SDSL/HLSL) for custom materials or rendering effects. If these shaders aren't extracted and compiled, the mod's materials won't render.

**Solution:** The `.modpkg` format includes a `shaders/` directory. On mod load, shaders are compiled via Stride's effect system and registered with the `EffectSystem`.

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModShaderManager.cs`

**How it works:**
1. Mod packages include compiled shader bytecode in `shaders/` (pre-compiled by mod developer)
2. On mod load, `ModShaderManager` registers shaders with `EffectSystem`
3. Materials referencing mod shaders resolve correctly
4. On mod unload, shaders are unregistered

**Fallback:** If a mod material references a shader that isn't available (e.g., Game B loads a mod built for Game A which uses a custom shader), the material renders with a default PBR shader. This ensures the mesh is visible even if the custom shader is missing.

```csharp
public class ModShaderManager
{
    public void RegisterModShaders(ModPackage mod)
    {
        foreach (var shaderFile in mod.Manifest.Shaders)
        {
            var bytecode = mod.ReadFile(shaderFile.Path);
            _effectSystem.RegisterShader(shaderFile.Name, bytecode);
        }
    }

    public void UnregisterModShaders(ModPackage mod)
    {
        foreach (var shaderFile in mod.Manifest.Shaders)
            _effectSystem.UnregisterShader(shaderFile.Name);
    }
}
```

**Manifest update:** `mod.json` gains a `shaders` field:
```json
{
  "shaders": [
    { "name": "CustomPBR", "path": "shaders/custom-pbr.sdbundle" }
  ]
}
```

```json

```json
// asset-guids.json (in .modpkg root)
{
  "mappings": [
    { "virtualPath": "models/weapons/sword", "guid": "a1b2c3d4-e5f6-7890-abcd-ef1234567890" },
    { "virtualPath": "textures/environments/grass", "guid": "b2c3d4e5-f6a7-8901-bcde-f12345678901" }
  ]
}
```

### Acceptance Criteria

- [ ] Mod resources clean up on unload
- [ ] Mod state persists across sessions
- [ ] Inter-mod communication works
- [ ] No memory leaks on repeated load/unload
- [ ] Exceptions in mod code never crash the game
- [ ] Disabled mods can be re-enabled after fixing
- [ ] Custom shaders from mods compile and render correctly
- [ ] Missing shaders fall back to default PBR shader
- [ ] GUID-aware content pipeline resolves mod assets correctly
- [ ] GUID collision check rejects conflicting mods

---

## Phase 5: Cross-Game API (The Unique Selling Point)

### 5.1 Modulus.Modding.Api

**Files to create:**
- `sources/engine/Stride.Engine/Modding/Api/IMod.cs` — mod entry point
- `sources/engine/Stride.Engine/Modding/Api/IModContext.cs` — mod access to engine
- `sources/engine/Stride.Engine/Modding/Api/IModSystem.cs` — mod system base
- `sources/engine/Stride.Engine/Modding/Api/IModComponent.cs` — mod component base
- `sources/engine/Stride.Engine/Modding/Api/IModSerializable.cs` — state save/load
- `sources/engine/Stride.Engine/Modding/Api/IModEventBus.cs` — inter-mod communication

**Interface definitions:**

```csharp
namespace Modulus.Modding.Api;

public interface IMod
{
    string Id { get; }
    string Name { get; }
    Version Version { get; }
    Version MinApiVersion { get; }
    void Initialize(IModContext context);
    void OnEnabled();
    void OnDisabled();
}

public interface IModContext
{
    IEntityManager Entities { get; }
    IContentManager Content { get; }
    ILogger Logger { get; }
    IModEventBus Events { get; }
    IServiceRegistry Services { get; }
}

public interface IModComponent { /* standalone — no Stride dependency */ }
public interface IModSystem { int Priority { get; } /* standalone — no Stride dependency */ }
public interface IModSerializable { void Save(Stream stream); void Load(Stream stream); }
public interface IModEventBus
{
    void Subscribe<T>(Action<T> handler);
    void Unsubscribe<T>(Action<T> handler);
    void Publish<T>(T evt);
}
```

**Internal adapters bridge standalone interfaces to Stride types.** `IModComponent` and `IModSystem` are standalone — they don't inherit from `IEntityComponent` or `IEntityProcessor`. Internally, the engine wraps them in adapter classes that implement Stride's interfaces:

```csharp
// Internal adapter — not exposed to mods
internal class ModComponentAdapter : EntityComponent
{
    private readonly IModComponent _modComponent;
    public ModComponentAdapter(IModComponent modComponent) { _modComponent = modComponent; }
}

internal class ModProcessorAdapter<T> : EntityProcessor<ModComponentAdapter>
    where T : IModSystem
{
    private readonly T _modSystem;
    public ModProcessorAdapter(T modSystem) { _modSystem = modSystem; }
    public int Priority => _modSystem.Priority;
    public override void Update(GameTime time) { _modSystem.Update(time); }
}
```

This pattern keeps `Modulus.Modding.Api` Stride-agnostic — mods never reference `Stride.Engine` to implement `IModComponent` or `IModSystem`. The adapters are engine internals.

**Key design:**
- Mods target `Modulus.Modding.Api`, not Stride directly
- Games built on the engine also target these APIs
- This abstraction layer enables cross-game compatibility

**ABI Stability Rules:**

`Modulus.Modding.Api` is the **stable ABI**. All other assemblies are internal and not guaranteed stable.

| Assembly | Stability | Notes |
|---|---|---|
| `Modulus.Modding.Api` | **Stable** | Same types, same signatures, same behavior across minor/patch versions |
| `Stride.Core` | Internal | May change without notice |
| `Stride.Engine` | Internal | May change without notice |
| `Stride.Graphics` | Internal | May change without notice |
| `Stride.Rendering` | Internal | May change without notice |
| All other Stride assemblies | Internal | May change without notice |

**Rules:**
- Mods MUST reference only `Modulus.Modding.Api` for guaranteed stability
- If a mod references internal assemblies directly, it works but may break on engine updates
- This is the same model as NeoForge — mods target the Forge API, not Minecraft internals
- The `Modulus.Modding.Api` version follows semantic versioning (same as engine version)

**Test:** Unit test — mod compiled against API loads in different game projects.

### 5.2 Shared Component Types

**Decision:** Shared gameplay components (Health, Inventory, Dialogue) are NOT baked into the engine. They ship as a separate, versioned "standard library" mod.

**Reasoning:** Gameplay concepts in the engine's stable API must be maintained forever. Shipping them as a mod means:
- They can be versioned independently of the engine
- Games can choose which standard library version to support
- Mods can depend on a specific standard library version
- The engine API surface stays minimal and stable

**Files to create:**
- `sources/engine/Stride.Engine/Modding/Components/` — contains only `IModComponent` (interface, no concrete gameplay types)

**Standard Library Mod (separate package):**
- `Modulus.StandardLibrary` — ships as a .modpkg, not compiled into the engine
- Contains: `HealthComponent`, `InventoryComponent`, `DialogueComponent`, etc.
- Versioned independently: `Modulus.StandardLibrary v1.0.0`
- Mods declare dependency: `{ "id": "modulus.standard-library", "minVersion": "1.0.0" }`
- Games bundle the standard library they want to support
- Modders can create their own standard libraries for specific game genres

**Test:** Unit test — mod using `Modulus.StandardLibrary v1.0` HealthComponent loads in Game A and Game B.

**Standard Library v1 components (basic):**
- `HealthComponent` — MaxHealth, CurrentHealth, IsDead
- `InventoryComponent` — Items list, Capacity
- `DialogueComponent` — Dialogue tree data
- `WeaponComponent` — Damage, Range, AttackSpeed
- `TransformComponent` — Position, Rotation, Scale (wrapper around engine transform)

**Standard Library v2+ expansion (future):** These save developers time by providing ready-made gameplay systems:
- `CharacterControllerComponent` — movement, jumping, collision
- `AIPathComponent` — navmesh integration, pathfinding
- `QuestComponent` — quest state, objectives, rewards
- `CraftingComponent` — recipes, inventory integration
- `TradingComponent` — buy/sell, currency

The v1 components are minimal interfaces. v2+ components include full implementations that developers can use or override.

### 5.2.1 Translation Mods

**Problem:** A sword from Skyrim has a `WeaponComponent` with `damage=10, skill="one-handed"`. Fallout's `WeaponComponent` has `damage=15, range=2.0, ammoType="energy"`. These are incompatible.

**Solution:** Translation mods — lightweight mods that map one game's component format to another.

**How it works:**
1. A modder creates a "Skyrim-to-Fallout Translation" mod
2. This mod contains adapter components that convert Skyrim's `WeaponComponent` to Fallout's format
3. Gamers install the translation mod alongside the Skyrim sword mod
4. The sword works in Fallout with appropriate stats

**Translation mod manifest:**
```json
{
  "id": "com.example.skyrim-to-fallout",
  "name": "Skyrim to Fallout Translation",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "type": "translation",
  "sourceGame": "skyrim",
  "targetGame": "fallout",
  "adapters": [
    {
      "sourceType": "Skyrim.WeaponComponent",
      "targetType": "Fallout.WeaponComponent",
      "adapter": "Adapters.WeaponAdapter"
    }
  ]
}
```

**Adapter interface (in Standard Library):**
```csharp
public interface IComponentAdapter<TSource, TTarget>
    where TSource : IModComponent
    where TTarget : IModComponent
{
    TTarget Convert(TSource source);
}
```

This makes cross-game conversion a structured process rather than ad-hoc hacking.

### 5.2.2 Developer Best Practices for Moddable Games

To maximize cross-game compatibility, games built on Modulus should follow these practices:

**Asset conventions:**
- Use standard PBR material parameters (albedo, normal, roughness, metallic)
- Use standard mesh formats (glTF preferred)
- Document custom shader parameters in the game's modding guide
- Provide a "default PBR" shader that renders any mesh with standard materials

**Component conventions:**
- Use Standard Library components when possible (`HealthComponent`, `WeaponComponent`)
- If custom components are needed, document their fields clearly
- Provide adapter interfaces for converting Standard Library types to custom types

**Architecture conventions:**
- Keep game logic in ECS systems, not in component constructors (constructor purity rule)
- Use services for game-specific functionality (physics, audio, AI)
- Document which services mods can access and what they provide

**Documentation conventions:**
- Publish a modding guide for each game
- Document all custom component types and their fields
- Provide example mods that demonstrate common patterns
- List compatible Standard Library versions

### 5.3 Flexible API Versioning

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModCompatibility.cs`

**Rules:**
- **Always try, unless explicitly unsafe.** Minor/patch mismatches load silently. Major mismatches reject by default.
- Major version bump (1.0.0 → 2.0.0): **Rejected by default** — major API changes risk save corruption and native access violations. Override: `--allow-outdated-mods` CLI flag or `allowOutdatedMods: true` in config.
- Major version downgrade (2.0.0 → 1.0.0): Fail if using newer API features, attempt to load if not explicitly incompatible.
- Minor version bump (1.0.0 → 1.1.0): Adds features. Older mods still work silently.
- Patch version bump (1.0.0 → 1.0.1): Bug fixes. All mods still work.
- Mod declares `apiVersion`: minimum API version required.
- Game declares supported API versions.
- Graceful degradation: missing optional APIs logged, mod continues.

**Test:** Unit test — mod targeting API 1.0 loads in game with API 1.1. Mod targeting API 2.0 warned with clear error.

### Acceptance Criteria

- [ ] Mods target Modulus.Modding.Api
- [ ] Same mod works in multiple games
- [ ] API versioning is flexible
- [ ] Clear error messages for version mismatches

---

## Phase 6: Mod Packaging & Distribution

### 6.1 .modpkg Format

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModPackageManager.cs`

**Format:** ZIP archive:
```
mymod.modpkg/
├── mod.json
├── assemblies/MyMod.dll
├── assets/...
├── data/config.json (optional)
└── asset-guids.json (for GUID-aware asset resolution)
```

**Compilation workflow:** Mod development uses external build tools, not in-editor compilation. The workflow is:
1. Mod developer scaffolds a mod via `dotnet new modulus-mod -n MyMod`
2. Writes code and assets in their IDE (VS Code, Rider, etc.)
3. Builds the DLL: `dotnet build`
4. Runs assets through Stride's asset compiler: `AssetCompiler.exe --project MyMod/`
5. Packages into `.modpkg`: `modulus pack MyMod/`
6. Installs into game: copies `.modpkg` to `mods/` or clicks "Install Mod" in Game Studio

Game Studio does NOT compile mod code. Templates and CLI tools handle the build pipeline. In-editor compilation would require hosting Roslyn/MSBuild inside the editor process, which introduces complexity far exceeding the benefit.

**Test:** Unit test — create .modpkg, install it, load it.

### 6.2 Mod Discovery

**Files to create:**
- (Already created in Phase 3: ModDiscovery.cs)

**How it works:**
1. Scan `mods/` directory for .modpkg files and subdirectories
2. Parse mod.json manifests
3. Validate dependencies and compatibility
4. Return list of available mods

**Test:** Unit test — create multiple mod packages, verify discovery finds all.

### 6.3 Mod Validation

**Files to create:**
- (Already created in Phase 3: ModValidator.cs)

**Validation rules:**
- `id`: kebab-case, unique, required
- `version`: semver, required
- `apiVersion`: semver, required
- `entryPoint`: fully-qualified class name, optional
- `dependencies`: array of {id, minVersion, optional?}, optional — each entry requires `id` and `minVersion`; `optional: true` means the mod loads even if the dependency is missing
- `rejectFutureVersions`: bool, optional (default: false) — if true, reject mod on major API version mismatch instead of warning
- `components`: array of {type, processor}, optional
- `systems`: array of {type, priority}, optional
- `assets`: array of paths, optional
- `loadOrder`: int, optional (default 100)
- `type`: "standard" | "patch" | "data", required
  - `standard`: a regular gameplay mod (code + assets)
  - `patch`: modifies an existing mod's behavior (applied after the target mod loads; requires `dependencies` pointing to the target mod)
  - `data`: data-only mod (no code, no assemblies — just assets and JSON configs)

**Test:** Unit test — invalid mod rejected with clear error. Valid mod passes.

### 6.4 Deterministic Load Order

**Problem:** With many mods and dependencies, load order must be predictable and correct.

**Solution:** Dependency graph resolution with topological sort and cycle detection.

**Algorithm:**
1. Parse all mod manifests
2. Build dependency graph from `dependencies` arrays
3. Detect cycles using DFS (Tarjan's algorithm)
4. Topological sort for load order
5. Use `loadOrder` as tie-breaker when dependencies are equal
6. Report cycles clearly: "Circular dependency: A → B → C → A"

**Files to create:**
- `sources/engine/Stride.Engine/Modding/ModLoadOrderResolver.cs`

**Implementation:**
```csharp
public class ModLoadOrderResolver
{
    public List<ModPackage> ResolveLoadOrder(IEnumerable<ModPackage> mods)
    {
        // 1. Build adjacency list from dependencies
        // 2. Detect cycles (DFS with coloring: White→Gray→Black)
        // 3. Topological sort (Kahn's algorithm or DFS-based)
        // 4. Tie-break by loadOrder field
        // 5. Return sorted list or throw on cycle
    }

    public List<List<ModPackage>> DetectCycles(IEnumerable<ModPackage> mods)
    {
        // Returns list of cycles found (each cycle is a list of mod IDs)
    }
}
```

**Edge cases:**
- Missing dependency → mod not loaded, error: "Requires mod X version Y"
- Circular dependency → all mods in cycle not loaded, error: "Circular dependency: A → B → C → A"
- Conflicting versions → mod not loaded, error: "Mod A requires X v1.0, Mod B requires X v2.0"
- Optional dependencies → mod loads even if optional dependency missing, warning logged

**Test:** Unit test — complex dependency graph resolves correctly. Cycle detected and reported. Missing dependency detected and reported.

### Mod Manifest Schema

```json
{
  "id": "com.example.mymod",
  "name": "My Mod",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "author": "Author Name",
  "description": "What the mod does",
  "entryPoint": "MyMod.MyModModule, MyMod",
  "dependencies": [
    { "id": "com.example.othermod", "minVersion": "1.0.0" },
    { "id": "com.example.optionalmod", "minVersion": "2.0.0", "optional": true }
  ],
  "rejectFutureVersions": false,
  "components": [
    { "type": "MyMod.Components.HealthComponent", "processor": "MyMod.Systems.HealthProcessor" }
  ],
  "systems": [
    { "type": "MyMod.Systems.MySystem", "priority": 100 }
  ],
  "assets": [
    "models/cube.sdmodel",
    "textures/wood.png"
  ],
  "loadOrder": 100,
  "tags": ["gameplay", "health"],
  "type": "standard"
}
```

### Acceptance Criteria

- [ ] .modpkg format works
- [ ] Mod discovery finds installed mods
- [ ] Mod validation catches errors
- [ ] Clear error messages for invalid mods

---

## Phase 7: Editor Integration

### 7.1 Mod Management Plugin

**Files to create:**
- `sources/editor/Stride.Modding.Editor/ModdingPlugin.cs` — extends StrideAssetsPlugin
- `sources/editor/Stride.Modding.Editor/ModManagerPanel.xaml` — AvalonDock panel
- `sources/editor/Stride.Modding.Editor/ModManagerViewModel.cs` — MVVM ViewModel

**Features:**
- Mod list panel (shows installed mods with enable/disable toggles)
- Mod details panel (manifest info, dependencies, components)
- Mod console (log output from mods)
- "Install Mod" button (browse for .modpkg)
- "Reload Mod" button (hot-reload)

**Extension points used:**
- `AssetsPlugin.RegisterPlugin()` for plugin registration
- `LayoutAnchorable` for custom panel in AvalonDock
- `ITemplateProvider` for custom property templates

**Test:** Smoke test — Game Studio launches with mod panel visible.

### 7.2 Mod Property Grid Integration

**Implementation:**
- Register `NodePresenterUpdater` for mod components
- Register `ITemplateProvider` for custom property templates

**Test:** Add mod component to entity, verify properties display in grid.

### 7.3 Mod Asset Browser Integration

**Implementation:**
- Register mod packages via `session.SuggestedPackages.Add()`

**Test:** Load mod, verify assets appear in Solution Explorer.

### 7.4 Editor Hot-Reload Policy

**Decision:** Mods load at runtime only. Editor reload does NOT trigger mod reload.

**Reasoning:**
- Stride Game Studio (modern .NET 6/8+) uses `AssemblyLoadContext` exclusively for code isolation — `AppDomain` unloading is a legacy .NET Framework concept, not applicable here
- Mod ALCs are separate from the editor's ALC
- Mixing them creates complexity and instability
- Mods are loaded when the game starts, unloaded when the game stops

**Rules:**
- Editor time (no game running): mods are NOT loaded
- Game starts: mods are loaded via ModHost
- Game stops: mods are unloaded
- Editor file change triggers editor hot-reload (existing behavior) — does NOT affect mods
- Mod file change: mod is NOT automatically reloaded (user clicks "Reload Mod" in panel)
- "Reload Mod" button: unloads mod ALC, reloads mod ALC, re-registers types

**Design-time reflection paradox:** Phase 7.2/7.3 require mod components to show in the property grid and asset browser. But Stride's property grid relies on type reflection — if the mod assembly isn't loaded into Game Studio's process, components show as raw XML errors. **Solution: Editor-Specific Metadata ALC.**

When a project is open in Game Studio, a lightweight, execution-free ALC loads mod assemblies purely to harvest type descriptors, asset compile definitions, and property templates. This ALC:
- Never executes `IMod.Initialize()`, `Update()`, or any mod code
- Only provides reflection metadata (types, properties, attributes, asset lists)
- Is discarded when the project closes
- Is separate from the runtime ALC used when the game plays

```csharp
// In ModdingPlugin (Phase 7.1):
public class EditorMetadataLoader
{
    private readonly Dictionary<string, Assembly> _metadataAssemblies = new();

    public void LoadModMetadata(ModManifest manifest)
    {
        var alc = new AssemblyLoadContext($"metadata-{manifest.Id}", isCollectible: true);
        var assembly = alc.LoadFromAssemblyPath(manifest.AssemblyPath);
        // Harvest types but NEVER execute code
        var componentTypes = assembly.GetTypes()
            .Where(t => typeof(EntityComponent).IsAssignableFrom(t));
        RegisterComponentTypesForPropertyGrid(componentTypes);
        _metadataAssemblies[manifest.Id] = assembly;
    }
}
```

This resolves the contradiction: mods aren't "loaded" at editor time (no code execution), but their types ARE available for property grid reflection.

**Constructor purity rule:** Stride's property grid and serialization layer call `Activator.CreateInstance(type)` to establish grid baselines and read default property values. This means **mod component constructors and static constructors WILL execute at design-time** in Game Studio. If a constructor touches runtime-only state (`Game.Services`, rendering backbuffers, network sockets), Game Studio crashes instantly. **All component constructors must be pure** — only field initialization and primitive assignment. Environment-dependent logic must be deferred to lifecycle hooks (e.g., `IMod.Initialize()` or an engine-invoked setup method).

**Why not automatic mod reload:**
- Mod unload requires cleanup (entities, events, components)
- Automatic reload during gameplay is risky (mid-frame state changes)
- Manual reload is safer and more predictable
- Developers can use the "Reload Mod" button during iteration

**Unload + Reload is not always reliable.** Some mods will leak references to their ALC — through static event subscriptions, `Task.Run(...)` continuations, or cached delegates. A single leaked reference prevents ALC collection and the mod DLL stays locked in memory. When this happens:

```csharp
public enum ModReloadResult
{
    Success,
    RequiresRestart,  // "Restart the game to reload this mod"
    TemporarilyDisabled  // Mod unloaded but can't reload until restart
}
```

If `ALC.Unload()` + `GC.Collect()` fails to collect the ALC after 3 attempts, the mod is marked `RequiresRestart`. The UI shows: "This mod could not reload cleanly. Please restart the game to reload it." This is the same fallback many mature mod systems (Minecraft Forge/NeoForge, RimWorld Harmony, Kerbal Space Program) eventually adopt.

**UX advantage of runtime-only isolation:** Because mods load at runtime only (never in the editor's WPF process), a `RequiresRestart` condition **never requires closing Game Studio itself**. The user merely restarts the game preview simulation window. The editor stays completely stable, preserving all unsaved work, scene layout, and property inspector state. This keeps the developer iteration loop fast even when mods misbehave.

### Acceptance Criteria

- [ ] Mod management panel in Game Studio
- [ ] Mod components show in property grid
- [ ] Mod assets show in asset browser
- [ ] Enable/disable/reload mods from UI
- [ ] Editor reload does not affect mods
- [ ] Mod reload is manual (button click)
- [ ] Mods that fail ALC collection show "Restart required" message
- [ ] `ModReloadResult` enum implemented (Success, RequiresRestart, TemporarilyDisabled)

---

## Phase 8: Test Mods

### 8.1 Test Mod: Component Adder (Build First)

**Purpose:** Prove that mods can add new ECS components and systems.

**Structure:**
```
tests/mods/test-component-adder/
├── mod.json
├── TestComponentAdder.csproj
└── Components/
    └── RotatingComponent.cs
```

**Code:**
```csharp
[DataContract("RotatingComponent")]
[DefaultEntityComponentProcessor(typeof(RotatingProcessor))]
public class RotatingComponent : EntityComponent
{
    public float Speed { get; set; } = 45.0f;
    public Vector3 Axis { get; set; } = Vector3.UnitY;
}

public class RotatingProcessor : EntityProcessor<RotatingComponent>
{
    public override void Update(GameTime time)
    {
        foreach (var entry in ComponentDatas)
        {
            var entity = entry.Key;
            var component = entry.Value;
            entity.Transform.Rotation =
                Quaternion.RotationAxis(component.Axis, component.Speed * time.DeltaTime)
                * entity.Transform.Rotation;
        }
    }
}
```

**What it proves:**
- Mod can register new component types with the serializer
- Mod can add new EntityProcessor systems
- Components appear in the editor's property grid
- Components are saved/loaded with the scene
- On unload, all RotatingComponent instances are removed

**Test:** Load mod, add RotatingComponent to entity, verify rotation, save scene, reload, verify rotation continues.

### 8.2 Test Mod: System Replacer (Build Second)

**Purpose:** Prove that mods can replace core systems via service-based approach.

**Structure:**
```
tests/mods/test-system-replacer/
├── mod.json
├── TestSystemReplacer.csproj
├── PhysicsReplacer/
│   └── DummyPhysicsSystem.cs
└── ModEntry.cs
```

**Code:**
```csharp
public class DummyPhysicsSystem : PhysicsSystem
{
    public DummyPhysicsSystem(IServiceRegistry services) : base(services) { }
    public override void Update(GameTime gameTime) { /* no-op */ }
}

public class ModEntry : IMod
{
    public string Id => "com.modulus.test.system-replacer";
    public string Name => "Dummy System Replacer";
    public Version Version => new Version(1, 0, 0);
    public Version MinApiVersion => new Version(1, 0, 0);

    public void Initialize(IModContext context)
    {
        context.Services.AddService<PhysicsSystem>(new DummyPhysicsSystem(context.Services));
        context.Logger.Info("Physics system replaced with dummy");
    }

    public void OnEnabled() { }
    public void OnDisabled() { }
}
```

**What it proves:**
- Mod can load via AssemblyLoadContext
- Mod can replace Physics via `Services.AddService<>()`
- Game uses the mod's implementation instead of Stride's
- On unload, original systems are restored

**Test:** Load mod, verify physics disabled, unload mod, verify physics restored.

### 8.3 Test Mod: Cross-Game (Build Third)

**Purpose:** Prove that a mod works in multiple games.

**Structure:**
```
tests/mods/test-cross-game/
├── mod.json
├── TestCrossGame.csproj
├── Components/
│   └── HealthComponent.cs
└── Systems/
    └── HealthProcessor.cs
```

**Code:**
```csharp
[DataContract("HealthComponent")]
[DefaultEntityComponentProcessor(typeof(HealthProcessor))]
public class HealthComponent : EntityComponent
{
    public float MaxHealth { get; set; } = 100f;
    public float CurrentHealth { get; set; } = 100f;
    public bool IsDead { get; set; } = false;
}

public class HealthProcessor : EntityProcessor<HealthComponent>
{
    public override void Update(GameTime time)
    {
        foreach (var entry in ComponentDatas)
        {
            var entity = entry.Key;
            var health = entry.Value;
            if (health.CurrentHealth <= 0 && !health.IsDead)
            {
                health.IsDead = true;
                // Trigger death event via ModEventBus
            }
        }
    }
}
```

**What it proves:**
- Mod targets `Modulus.Modding.Api` (not Stride directly)
- Same mod loads in Game A and Game B
- Component works in both games
- API versioning doesn't break the mod

**Test:** Load same mod in two different game projects, verify it works in both.

### Acceptance Criteria

- [ ] Test Mod 1 (Component Adder) works
- [ ] Test Mod 2 (System Replacer) works
- [ ] Test Mod 3 (Cross-Game) works in multiple games
- [ ] All test mods have automated tests
- [ ] End-to-end integration test passes: install `.modpkg` → start game → mod loads → component works → game shuts down → cleanup verified

---

## Phase 9: Documentation & Distribution

### 9.1 DocFX Setup

**Directory structure:**
```
docs-site/
├── docfx.json
├── index.md
├── toc.yml
├── articles/
│   ├── toc.yml
│   ├── getting-started.md
│   ├── architecture.md
│   ├── modding/
│   │   ├── toc.yml
│   │   ├── writing-a-mod.md
│   │   ├── mod-api-reference.md
│   │   └── cross-game-mods.md
│   ├── mcp-server/
│   │   ├── toc.yml
│   │   ├── setup.md
│   │   └── tool-reference.md
│   └── development/
│       ├── toc.yml
│       ├── stride-modifications.md
│       └── fork-management.md
├── api/   (generated, gitignored)
└── _site/ (generated, gitignored)
```

**DocFX configuration (`docfx.json`):**
```json
{
  "metadata": [
    {
      "src": [
        {
          "files": [
            "bin/Release/**/Modulus.Modding.Api.dll",
            "bin/Release/**/Stride.Engine.dll",
            "bin/Release/**/Stride.Graphics.dll"
          ],
          "src": "../"
        }
      ],
      "dest": "api",
      "properties": { "TargetFramework": "net10.0" },
      "memberLayout": "samePage",
      "namespaceLayout": "flattened"
    }
  ],
  "build": {
    "content": [
      { "files": ["api/**.yml", "toc.yml", "index.md"] },
      { "files": ["articles/**.md", "articles/**/toc.yml"], "dest": "docs" }
    ],
    "resource": [
      { "files": ["articles/images/**"] }
    ],
    "globalMetadata": {
      "_appTitle": "Modulus Engine",
      "_appFooter": "Copyright (c) 2026 Modulus Engine Contributors",
      "_enableSearch": true
    },
    "template": ["default", "modern"]
  }
}
```

**Build commands:**
```bash
dotnet tool update -g docfx
dotnet build -c Release
docfx metadata docs-site/docfx.json
docfx build docs-site/docfx.json
# Output: docs-site/_site/
```

### 9.2 NuGet Distribution

**MCP Server (.csproj):**
```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>modulus-mcp</ToolCommandName>
  <PackageId>ModulusEngine.MCPServer</PackageId>
</PropertyGroup>
```

**Template Pack (.csproj):**
```xml
<PropertyGroup>
  <PackageType>Template</PackageType>
  <PackageId>ModulusEngine.Templates</PackageId>
</PropertyGroup>
```

**Publish commands:**
```bash
# Pack
dotnet pack tools/ModulusEngine.MCPServer -c Release
dotnet pack Modulus.Templates -c Release

# Publish to NuGet.org
dotnet nuget push tools/ModulusEngine.MCPServer/nupkg/*.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json
dotnet nuget push Modulus.Templates/nupkg/*.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json
```

### 9.3 dotnet new Templates

**Game template (`template.json`):**
```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "Modulus Engine Contributors",
  "classifications": ["Game", "Modulus", "Console"],
  "identity": "ModulusEngine.GameTemplate.CSharp",
  "name": "Modulus Engine Game",
  "shortName": "modulus-game",
  "sourceName": "ModulusGame",
  "preferNameDirectory": true,
  "tags": { "language": "C#", "type": "project" }
}
```

**Mod template (`template.json`):**
```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "Modulus Engine Contributors",
  "classifications": ["Mod", "Modulus", "Library"],
  "identity": "ModulusEngine.ModTemplate.CSharp",
  "name": "Modulus Engine Mod",
  "shortName": "modulus-mod",
  "sourceName": "MyMod",
  "preferNameDirectory": true,
  "tags": { "language": "C#", "type": "project" }
}
```

### 9.4 CI/CD (GitHub Actions)

**Build workflow (`.github/workflows/ci.yml`):**
```yaml
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 10.x }
      - run: dotnet build -c Release
      - run: dotnet test
      - run: dotnet pack tools/ModulusEngine.MCPServer -c Release --no-build
      - run: dotnet pack Modulus.Templates -c Release --no-build
      - uses: actions/upload-artifact@v4
        with:
          name: nupkgs
          path: '**/nupkg/*.nupkg'
```

**Release workflow (`.github/workflows/release.yml`):**
```yaml
on:
  push:
    tags: ['v*']
jobs:
  publish:
    runs-on: windows-latest  # Stride requires Windows build tools
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 10.x }
      - run: dotnet build -c Release
      - run: dotnet pack tools/ModulusEngine.MCPServer -c Release --no-build
      - run: dotnet pack Modulus.Templates -c Release --no-build
      - run: dotnet nuget push tools/ModulusEngine.MCPServer/nupkg/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
      - run: dotnet nuget push Modulus.Templates/nupkg/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
```

**Docs workflow (`.github/workflows/docs.yml`):**
```yaml
on:
  push:
    tags: ['v*']
jobs:
  publish-docs:
    runs-on: ubuntu-latest  # DocFX is cross-platform; no Windows build tools needed here
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 10.x }
      - run: dotnet build -c Release
      - run: dotnet tool install -g docfx
      - run: docfx metadata docs-site/docfx.json
      - run: docfx build docs-site/docfx.json
      - uses: actions/upload-pages-artifact@v3
        with: { path: docs-site/_site }
      - uses: actions/deploy-pages@v4
```

### Acceptance Criteria

- [ ] DocFX generates HTML docs from XML comments + markdown
- [ ] Docs publish to GitHub Pages
- [ ] Offline docs downloadable as ZIP
- [ ] MCP server installs via `dotnet tool install`
- [ ] Templates install via `dotnet new install`
- [ ] CI builds and tests on every push
- [ ] Release publishes to NuGet.org on `v*` tag

### Platform Requirements

**Engine development requires Windows.** Stride's asset pipeline, WPF editor (Game Studio), and several build tools have deep ties to Windows-specific APIs. CI workflows use `windows-latest` exclusively.

**However, the modding API (`Modulus.Modding.Api`) targets managed C# only.** Mod developers can write and compile mods on any platform that supports .NET 10. The engine itself (fork build, editor, asset pipeline) must be built on Windows, but the modding layer produces cross-platform managed assemblies.

Document this in contributor guides: "To build the engine: Windows required. To write mods: any .NET 10 platform."

---

## Fork Management

### Branch Strategy

- `main` — stable, production-ready
- `dev` — active development, PRs target this branch
- Feature branches — `feature/mod-loading`, `feature/mod-api`, etc.

### Upstream Sync

**Cadence:** Sync with major Stride releases, cherry-pick important minor fixes.

**Commands:**
```bash
git remote add upstream https://github.com/stride3d/stride.git
git fetch upstream
git checkout main
git merge upstream/main
# Resolve conflicts, run tests
git push origin main
```

### Change Isolation

- Put new code in `sources/engine/Stride.Engine/Modding/` where possible
- Put editor extensions in `sources/editor/Stride.Modding.Editor/`
- Modify existing files only when necessary
- Document all changes with clear commit messages

### Minimizing Merge Conflicts in Modified Stride Files

When we modify Stride core files (AssemblyRegistry.cs, DataSerializerFactory.cs), these become conflict hotspots during upstream merges. To minimize this:

**Pattern: Event dispatch hooks, not inline logic.**

Instead of writing our modding logic directly inside Stride's files, add minimal event dispatch points and keep the actual logic in our own assemblies:

```csharp
// In AssemblyRegistry.cs — minimal hook (1-2 lines)
public static event Action<Assembly>? AssemblyUnregistered;
public static void Unregister(Assembly assembly)
{
    // ... existing Stride logic ...
    AssemblyUnregistered?.Invoke(assembly);  // ← our hook: 1 line
}

// In ModLifecycleManager.cs — our logic (in Modulus assembly)
public ModLifecycleManager()
{
    AssemblyRegistry.AssemblyUnregistered += OnAssemblyUnregistered;
}

private void OnAssemblyUnregistered(Assembly assembly)
{
    // All our cleanup logic lives here, not in Stride's file
    ClearSerializers(assembly);
    ClearTypeDescriptors(assembly);
}
```

**Rules for modifying Stride files:**
- Add events or virtual methods (1-2 lines each) — never write business logic
- When Stride adds a new feature, our hooks continue to work
- When Stride renames a method, only the 1-line hook needs updating
- The bulk of our code stays in `sources/engine/Stride.Engine/Modding/`

---

## Security Model

### Native Dependencies

**Rule:** Mods are managed C# only. No native DLLs in mods.

**Reasoning:**
- Native DLLs are platform-specific (Windows/Linux/macOS)
- Allowing mods to ship native DLLs creates security and compatibility risks
- The service-based approach handles this cleanly: a mod extends `PhysicsSystem` (managed wrapper) and registers via `Services.AddService`, but the underlying Bullet native DLL stays from Stride
- If a mod truly needs custom physics, it implements it in managed C# via ECS components/systems

**What this means:**
- Mods CANNOT ship native DLLs (`.dll`, `.so`, `.dylib`)
- Mods CANNOT override engine native DLLs
- Mods CAN use any managed C# API exposed by the engine
- Mods CAN implement custom physics/audio/etc. in pure C#

### Mods CAN

- Add new EntityComponent types
- Add new EntityProcessor systems
- Access all engine services via IServiceRegistry
- Load assets from their own package
- Register event handlers
- Create/destroy entities
- Communicate with other mods via event bus
- Replace managed systems (Physics, Audio, Navigation) via service override

### Mods CANNOT (v1)

- Ship or override native DLLs
- Replace engine core (EntityManager, SceneSystem, etc.)
- Access file system outside their package directory **(unenforced convention in v1 — .NET does not sandbox this by default; enforced via CAS in v2+)**
- Make network calls (unless game allows it)
- Modify other mods' types
- Crash the engine (exceptions caught and logged)

### Future (v2+)

- Optional CAS sandboxing (like Hangar-Bay)
- Per-mod permission system
- Signed mod verification

---

## Error Handling

| Scenario | Behavior |
|---|---|
| Mod load failure | Log error, continue loading other mods |
| Version mismatch | Clear error: "Mod requires API 2.0, game supports API 1.x" |
| Missing dependency | Mod not loaded, error: "Requires mod X version Y" |
| Circular dependency | All mods in cycle not loaded, error: "Circular dependency: A → B → C → A" |
| Conflicting versions | Mod not loaded, error: "Mod A requires X v1.0, Mod B requires X v2.0" |
| Assembly already registered | Check if same mod, skip if so |
| Content not found | Fallback: mod sources → game source → error |
| Unload during scene load | Queue unload, execute after scene load |
| Mod crash (processor) | Catch exception, log, disable processor, continue game |
| Mod crash (component) | Catch exception, log, disable component, continue game |
| Mod crash (event handler) | Catch exception, log, disable handler, continue game |
| Mod crash (Initialize) | Catch exception, log, reject mod (don't load) |
| Native DLL in mod | Reject mod at validation, error: "Mods cannot ship native DLLs" |
| Missing optional API | Log warning, mod continues (graceful degradation) |

---

## Versioning Strategy

### API Versioning

- **Major version bump** (1.0.0 → 2.0.0): Breaks API. **Mod rejected by default.** Override with `--allow-outdated-mods` CLI flag or `allowOutdatedMods: true` in config. Protection by default preserves engine reputation and prevents save-file corruption.
- **Major version downgrade** (2.0.0 → 1.0.0): Attempt to load; fail only if mod uses newer API features not present in older engine.
- **Minor version bump** (1.0.0 → 1.1.0): Adds features. Older mods still work silently.
- **Patch version bump** (1.0.0 → 1.0.1): Bug fixes. All mods still work silently.
- **Mod declares `apiVersion`:** Minimum API version required.
- **Game declares supported API versions.**
- **Graceful degradation:** Missing optional APIs logged as warnings, mod continues.
- **Consistent policy throughout:** Reject on major version mismatch by default; allow explicit override. Same policy in Phase 5.3 and here.

### ABI Stability

`Modulus.Modding.Api` is the **stable ABI**. All other assemblies are internal.

| Assembly | Stability | What it provides |
|---|---|---|
| `Modulus.Modding.Api` | **Stable** | IMod, IModContext, IModComponent, IModSystem, IModEventBus, shared components |
| `Stride.Core` | Internal | Math, serialization, IO, microthreading |
| `Stride.Engine` | Internal | ECS, scene, entity, components |
| `Stride.Graphics` | Internal | GPU abstraction |
| `Stride.Rendering` | Internal | Render pipeline |
| All other Stride assemblies | Internal | Physics, audio, particles, etc. |

**Rules:**
- Mods MUST reference only `Modulus.Modding.Api` for guaranteed stability
- If a mod references internal assemblies directly, it works but may break on engine updates
- The `Modulus.Modding.Api` version follows semantic versioning (same as engine version)
- Internal assemblies may change signatures, rename types, or restructure without notice

---

## User-Facing Workflow (Post-Release)

```bash
# 1. Install the CLI
dotnet tool install -g ModulusEngine.MCPServer

# 2. Install the templates
dotnet new install ModulusEngine.Templates

# 3. Scaffold a new game
dotnet new modulus-game -n AwesomeShooter
cd AwesomeShooter
dotnet restore
dotnet build

# 4. Scaffold a new mod
dotnet new modulus-mod -n PowerUps

# 5. Read the docs
# Browser: https://modulus-engine.github.io/docs
# Offline: download docs-archive-X.Y.Z.zip from GitHub release
```

---

## Success Criteria

### Phase 0 Complete
- [ ] Fork builds successfully
- [ ] All existing tests pass
- [ ] Game Studio works identically to official Stride

### Phase 1 Complete (In-Engine HTTP API)
- [ ] Engine starts HTTP server on port 9876
- [ ] All API endpoints respond correctly
- [ ] GET endpoints return valid JSON
- [ ] POST endpoints accept JSON body
- [ ] Engine shutdown cleans up HTTP server

### Phase 2 Complete (MCP Server)
- [ ] MCP server starts and connects to engine via HTTP
- [ ] All 15 v1 tools callable via MCP client
- [ ] All resources resolvable
- [ ] Documentation published

### Phase 3 Complete (Modding Foundation)
- [ ] Mod assemblies load via AssemblyLoadContext
- [ ] Mod component types register with serializer
- [ ] Mod systems register with ECS
- [ ] Mod assets load and render

### Phase 4 Complete (Mod Lifecycle)
- [ ] Mod resources clean up on unload
- [ ] ALC fully collected after unload (no leak)
- [ ] Mod state persists across sessions
- [ ] Inter-mod communication works
- [ ] Exceptions in mod code never crash the game

### Phase 5 Complete (Cross-Game API)
- [ ] Mods target Modulus.Modding.Api
- [ ] Same mod works in multiple games
- [ ] API versioning is flexible (warn + allow)
- [ ] Shader extraction from mods works
- [ ] Translation mod format defined
- [ ] Developer best practices documented

### Phase 6 Complete (Packaging)
- [ ] .modpkg format works
- [ ] Mod discovery finds installed mods
- [ ] Mod validation catches errors
- [ ] Load order resolves correctly (topological sort)

### Phase 7 Complete (Editor Integration)
- [ ] Mod management panel in Game Studio
- [ ] Mod components show in property grid
- [ ] Mod assets show in asset browser
- [ ] Editor reload does not affect mods

### Phase 8 Complete (Test Mods)
- [ ] Test Mod 1 (Component Adder) works
- [ ] Test Mod 2 (System Replacer) works
- [ ] Test Mod 3 (Cross-Game) works in multiple games

### Phase 9 Complete (Documentation & Distribution)
- [ ] DocFX generates HTML docs
- [ ] Docs publish to GitHub Pages
- [ ] MCP server installs via dotnet tool
- [ ] Templates install via dotnet new
- [ ] CI/CD pipeline works

---

## Deferred Decisions

These are known questions intentionally deferred to later milestones:

1. **`Modulus.StandardLibrary` versioning** — co-released with engine or independently versioned? Decision: independently versioned, updated when Standard Library maintainers release.

2. **Minimum Stride version** — which Stride version does the fork baseline on? Decision: latest stable release at time of fork.

3. **Service replacement scope** — Physics/Audio/Navigation replacement is a separate milestone. When do we ship it? Decision: after Phase 4 (mod lifecycle) is stable.

4. **Runtime asset compilation** — when do we add runtime shader/model compilation for mods? Decision: v2+. v1 uses pre-compiled mods only.

5. **CAS sandboxing** — when do we add security sandboxing? Decision: v2+. v1 trusts mods (NeoForge model).

6. **Deprecation process for stable API** — how do we remove types from `Modulus.Modding.Api` without breaking mods? Decision: deprecation requires a full major version cycle with deprecation warnings before removal.

---

## Version Roadmap

### v1 — Modding Foundation (Phases 0-9, current plan)

The core: fork Stride, build modding layer, cross-game API, editor integration.

**Goal:** Mods load, unload, add components/systems/assets. Works across games.

### v1.5 — Quality Modding Tools

Make modding easy and practical for developers, modders, and end users.

| Feature | What | Why |
|---|---|---|
| **Event system with priority** | Hooks into entity spawn, physics collision, render pipeline, input events | Mods need to react to game events, not just add new things |
| **Configuration system** | Runtime config GUI, hot-reload, per-save configs, TOML/JSON | Users need to customize mod behavior without editing code |
| **Basic code injection** | Inject at method start/end, modify return values | Allows gameplay overhauls (not just content additions) |
| **Mod validation & conflict detection** | Detect when two mods modify the same thing, warn users | Prevents silent bugs from mod conflicts |
| **Enhanced Standard Library v2** | CharacterController, AIPath, Quest, Crafting, Trading components | Saves developers time implementing common gameplay systems |

### v2 — Polish & Networking

Requires: networking system implementation in the engine.

| Feature | What | Why |
|---|---|---|
| **Network protocol** | Mod synchronization, custom network packets | Essential for multiplayer mod support |
| **Client/server separation** | Enforce sided code at load time | Prevents mods from crashing dedicated servers |
| **Access transformers** | Let mods access internal engine state | Enables deeper modding without source modification |
| **Runtime asset compilation** | Compile shaders/models at runtime | Enables modders to ship raw assets instead of pre-compiled |

### v3 — Advanced Features & Repository

The engine is mature. Now add the polish layer.

| Feature | What | Why |
|---|---|---|
| **Full Mixin system** | Inject code into existing classes, redirect calls, add fields | The most powerful modding feature — enables complete gameplay overhauls |
| **Mod repository/store** | In-game browser for discovering, installing, updating mods | The final piece for end-user experience |
| **Visual load order UI** | Drag-and-drop load order in editor | Better UX than topological sort alone |
| **Save file analysis** | Which mods touched this save, what data they added | Debugging tool for mod conflicts |
| **CAS sandboxing** | Security sandboxing for untrusted mods | Enterprise/security use cases |

---

## Agent Notes

This section captures implementation risks, consistency issues, and execution guidance for agents implementing this plan.

### Phase Execution Order

**Do not change the phase order.** Each phase builds on the previous:
- Phase 0 (fork) → Phase 1 (HTTP API) → Phase 2 (MCP) → Phase 3 (modding foundation) → Phase 4 (lifecycle) → Phase 5 (cross-game API) → Phase 6 (packaging) → Phase 7 (editor) → Phase 8 (test mods) → Phase 9 (docs)

**Each phase should be a separate PR into `dev`.** Keeps reviews manageable, allows rollback of individual phases.

### High-Risk Areas

**1. ALC Unload (Phase 4.1.1) — Highest risk in entire project**

The 17-step cleanup checklist is the hardest part. A single leaked reference prevents the ALC from collecting. Before implementing cleanup logic, write the `WeakReference` test and 100-cycle memory test first — you need a pass/fail criterion before you start.

Key traps:
- `MicroThreadScheduler.RunningEntries` may not be public in Stride's actual source. Verify during Phase 3 exploration. If not public, use option (a): one-line source modification to expose it, or option (b): reflection.
- `TaskCompletionSource` objects from cancelled micro-threads can linger on the scheduler. The `SweepModDelegates` code handles this but must be tested.
- `AssemblyLoadContext.Unload()` is asynchronous. Use `[MethodImpl(MethodImplOptions.NoInlining)]` on test helper methods to prevent local variable references from keeping the ALC alive.

**2. Thread Safety (Phase 1) — High risk of silent corruption**

Stride's ECS is not thread-safe. ALL HTTP requests (GET and POST) must marshal through `_updateQueue`. An agent might implement GET requests as direct reads from the HTTP thread — this will cause collection modification exceptions or silent memory corruption.

The dual-queue design (`_updateQueue` for ECS, `_drawQueue` for GPU) must be implemented consistently. Do not mix queues. Screenshot capture goes in `_drawQueue` because it records `CommandList` commands.

**3. Cross-ALC Type Identity (Phase 3.1) — Hard to debug**

If `ModLoadContext.Load()` doesn't correctly route shared assemblies to their parent ALCs, mods get "type mismatch" errors that are very confusing. The code example in the plan is correct — implement it exactly as shown. Test this with a mod that depends on `Modulus.StandardLibrary` early.

### Medium-Risk Areas

**4. Stride Source Modifications (Phases 3, 4)**

You will modify these Stride files:
- `sources/core/Stride.Core/Reflection/AssemblyRegistry.cs` — add `Unregister(Assembly)` + event hook
- `sources/core/Stride.Core/Serialization/DataSerializerFactory.cs` — expose alias collision override + cache clear hook
- `sources/core/Stride.Core/Reflection/TypeDescriptorFactory.cs` — expose `ClearAssemblyCache(Assembly)`

**Follow the 1-line hook pattern.** Add events or virtual methods (1-2 lines each). Never write business logic in Stride's files. All modding logic goes in `sources/engine/Stride.Engine/Modding/`. This minimizes merge conflicts during upstream Stride syncs.

**5. Stride API Verification**

The plan references several Stride internal APIs that were researched from GitHub source but not verified in the actual forked code:
- `MicroThreadScheduler.RunningEntries` — may not be public
- `TypeDescriptorFactory.typeDescriptors` — field name may differ
- `DataSerializerFactory.AvailableAssemblySerializers` — may have different access modifiers
- `EntityManager.Processors` — collection type may differ

**During Phase 3, verify all referenced Stride APIs before implementing.** The plan's code examples are illustrative — translate them to match the actual Stride source.

**6. `IModComponent` / `IModSystem` Standalone Interfaces**

There are TWO valid mod patterns — document this clearly:
- **Stride-native mods** (Phase 8 test mods): use `EntityComponent`/`EntityProcessor` directly. Simpler, works on Modulus only.
- **Cross-game mods** (Phase 5): use `IModComponent`/`IModSystem` standalone interfaces with internal adapters. More portable, works across games.

Both are valid. The test mods in Phase 8 use Stride types directly (simpler, proves the mechanism works). The cross-game test in Phase 8.3 uses standalone interfaces (proves portability).

### Low-Risk Areas

**7. Phase 1 Must Register All Routes Including Stubs**

Phase 1 defines 23 API endpoints. Mod-related endpoints (`/api/v1/mod/*`, `/api/v1/render/*`) return 501 stubs until Phases 3-4 provide the backing implementation. The agent implementing Phase 1 must register ALL routes (including stubs) so the MCP server in Phase 2 has a consistent API surface.

**8. Phase 2 MCP Server — Straightforward**

The MCP server is a separate repo (`ModulusEngine.MCPServer`). It's an HTTP client that calls the engine's API. No Stride internals needed. The v1 scope is 15 tools — don't over-engineer.

**9. Test Mod Consistency**

The Phase 8 test mods use Stride types directly (`EntityComponent`, `EntityProcessor`, `DataContract`). This is intentional — they prove the basic modding mechanism works. The cross-game test (Phase 8.3) uses `Modulus.Modding.Api` interfaces to prove portability. Both approaches are valid and should coexist.

**10. Code Examples Are Illustrative, Not Compilable**

All code snippets in this plan are pseudocode-style. They show the intended design pattern but won't compile as-is. The agent must translate them into actual C# that compiles against the Stride API. This is expected — the plan documents architecture, not implementation.

**11. Build Performance — Use Solution Filters**

Stride is a massive codebase (~120+ projects). `dotnet build build/Stride.sln` takes significant time and creates enormous build log output that can blow past an agent's context window. During rapid write-test-fix loops in Phases 1, 3, and 4, use Stride's existing `.slnf` files or create a targeted filter:

```bash
# Fast build: runtime only (~29 projects)
dotnet build build/Stride.Runtime.slnf

# Or create a custom filter for just the projects you're modifying
# File: build/Modulus.Phase3.slnf
{
  "solution": "Stride.sln",
  "solutionFilter": {
    "projects": [
      "sources/core/Stride.Core/Stride.Core.csproj",
      "sources/core/Stride.Core.Serialization/Stride.Core.Serialization.csproj",
      "sources/engine/Stride.Engine/Stride.Engine.csproj",
      "sources/editor/Stride.GameStudio/Stride.GameStudio.csproj"
    ]
  }
}
```

Only use `dotnet build build/Stride.sln` for final validation before PRs.

**12. `.WithTimeout()` Is a Custom Utility**

The `tcs.Task.WithTimeout(TimeSpan.FromSeconds(5), ...)` pattern in Phase 1 is NOT a built-in .NET method. The agent must implement this as a task extension utility:

```csharp
// In sources/engine/Stride.Engine/HttpApi/TaskExtensions.cs
public static class TaskExtensions
{
    public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout, Func<T> fallback)
    {
        var delay = Task.Delay(timeout);
        if (await Task.WhenAny(task, delay) == task)
            return await task;
        return fallback();
    }
}
```

Place this in a common utility directory alongside the HTTP API code.

**13. Permission to Modify Stride's Base Library Files**

The agent has explicit permission to modify Stride's core source files (`Stride.Core`, `Stride.Core.Serialization`, `Stride.Engine`) to expose cache-clearing hooks, event dispatch points, and access modifiers. When modifying Stride files:

- Maintain Stride's existing coding style and formatting conventions
- Add minimal hooks (1-2 lines each) — never write business logic in Stride's files
- Follow the event dispatch pattern documented in "Minimizing Merge Conflicts"
- Do NOT attempt over-engineered reflection workarounds to access private variables — just open the field/method as `public` or `internal` with a one-line change

### Verification Checklist

Before starting each phase, verify:
- [ ] Previous phase's acceptance criteria are all checked
- [ ] All referenced Stride APIs exist in the forked codebase
- [ ] Code examples have been translated to match actual Stride types
- [ ] Tests are written before implementation (TDD for high-risk areas)

After completing each phase, verify:
- [ ] All acceptance criteria are met
- [ ] All unit tests pass
- [ ] No regressions in existing Stride tests
- [ ] `dotnet build` succeeds with 0 errors
- [ ] `dotnet test` passes all tests
