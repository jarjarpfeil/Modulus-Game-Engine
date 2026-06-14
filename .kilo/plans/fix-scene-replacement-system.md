# Plan: Fix Scene Replacement System for Mod Loading

## Overview

Fix the grey screen issue when `ModAutoLoadSystem` replaces `SceneSystem.SceneInstance` mid-frame. The root cause is that programmatically-created `Material.New()` materials require runtime effect compilation, which fails because:
1. The `EffectSystem` can't find shader source files through the `DatabaseFileProvider`
2. The `VisibilityGroup` initialization timing is incorrect (created inside `DrawCore()`, then immediately reset)
3. The rendering pipeline hasn't been warmed up for the new scene's materials

## Solution Approach

Instead of replacing the scene mid-frame, hook into `SceneSystem.LoadContent()` so the mod scene loads during normal initialization. This eliminates all timing issues and allows the rendering pipeline to initialize properly.

Additionally, implement runtime scene switching support so mods can load multiple scenes and switch between them dynamically (like Skyrim's world transitions).

---

## Phase 1: Fix Initial Scene Replacement

### Goal
Allow mods to replace the game's default scene by loading the mod scene during `SceneSystem.LoadContent()` instead of mid-frame.

### Root Cause Analysis

The grey screen occurs because:

1. **VisibilityGroup initialization timing**: When `SceneSystem.SceneInstance` is replaced mid-frame, the new scene's `VisibilityGroup` is created inside `GraphicsCompositor.DrawCore()` during the `VisibilityGroups_CollectionChanged` event. This triggers `ModelRenderProcessor` creation and immediate `Draw()` call, which creates `RenderMesh` objects. However, `visibilityGroup.Reset()` is called immediately after, clearing `ObjectNode` references before `RenderSystem.Collect()` can establish proper connections.

2. **Effect compilation timing**: The `EffectSystem` needs to compile effects for `Material.New()` materials. This compilation is asynchronous and may not complete before the first draw call. When effects are null, `MeshRenderFeature.Draw()` skips rendering.

3. **CameraProcessor timing**: The `CameraProcessor` needs to attach the camera to the compositor slot before `SceneCameraRenderer.ResolveCamera()` is called. This happens during `SceneInstance.Draw()`, which runs before `GraphicsCompositor.Draw()`, so this should work correctly.

### Solution

Load the mod scene during `SceneSystem.LoadContent()` so the entire initialization pipeline runs normally:
- `SceneSystem` creates the `SceneInstance` during `LoadContent()`
- First frame's `Update()` runs all entity processors (TransformProcessor, ModelTransformProcessor, CameraProcessor)
- First frame's `Draw()` creates the `VisibilityGroup` and `ModelRenderProcessor` normally
- `EffectSystem` has time to compile effects before the first draw
- No mid-frame timing issues

### Changes

#### 1.1 Modify `Game.Initialize()` to load mods early
**File**: `sources/engine/Stride.Engine/Engine/Game.cs`

- After `ModAutoLoadSystem` is added to `GameSystems`, call a new method `LoadModsEarly()`
- This method loads all mods and determines if a mod scene should replace the default scene
- If yes, set `SceneSystem.InitialSceneUrl` to the mod scene URL

```csharp
// In Game.Initialize(), after line 433:
GameSystems.Add(new Modding.ModAutoLoadSystem(Services));

// NEW: Load mods early to check for scene replacement
LoadModsEarly();
```

#### 1.2 Add `LoadModsEarly()` method to `Game`
**File**: `sources/engine/Stride.Engine/Engine/Game.cs`

```csharp
private void LoadModsEarly()
{
    var modHost = Services.GetService<ModHost>();
    if (modHost == null) return;
    
    // Only load if mods directory exists
    if (!System.IO.Directory.Exists(modHost.ModsDirectory)) return;
    
    // Load all mods
    var loaded = modHost.LoadAllMods();
    Log.Info($"[Game] Loaded {loaded.Count} mod(s) during initialization");
    
    // Check if any mod provides a replacement scene
    var allScenes = modHost.SceneManager.GetAllModScenes();
    foreach (var entry in allScenes)
    {
        if (entry.Behavior == Modulus.Modding.Api.ModSceneLoadBehavior.Replace)
        {
            // Replace the initial scene URL with the mod scene
            SceneSystem.InitialSceneUrl = entry.SceneUrl;
            Log.Info($"[Game] Mod scene '{entry.DisplayName}' will replace default scene");
            break;
        }
    }
}
```

#### 1.3 Modify `ModAutoLoadSystem` to skip scene replacement
**File**: `sources/engine/Stride.Engine/Modding/ModAutoLoadSystem.cs`

- Remove the scene replacement logic from `Update()`
- The scene is now loaded during initialization, not mid-frame
- Keep the mod loading and verification logic for logging purposes

```csharp
public override void Update(GameTime gameTime)
{
    if (_loaded) return;
    _loaded = true;
    
    // Mods are already loaded during Game.Initialize()
    // Just log the scene catalog for debugging
    var modHost = Services.GetService<ModHost>();
    if (modHost == null) return;
    
    var selectable = modHost.SceneManager.GetPlayerSelectableScenes();
    if (selectable.Count > 0)
    {
        Log.Info($"[ModAutoLoad] {selectable.Count} player-selectable scene(s) available:");
        foreach (var entry in selectable)
            Log.Info($"  [{entry.ModId}] {entry.DisplayName}");
    }
    
    // Verify the mod scene loaded correctly
    var allScenes = modHost.SceneManager.GetAllModScenes();
    foreach (var entry in allScenes)
    {
        if (entry.Behavior == Modulus.Modding.Api.ModSceneLoadBehavior.Replace)
        {
            var contentManager = Services.GetService<ContentManager>();
            if (contentManager != null && contentManager.Exists(entry.SceneUrl))
            {
                var loadedScene = contentManager.Load<Scene>(entry.SceneUrl);
                Log.Info($"[ModAutoLoad] Verified mod scene: {entry.DisplayName} ({loadedScene?.Entities?.Count ?? 0} entities)");
            }
            break;
        }
    }
    
    Enabled = false;
}
```

### Expected Result
- The mod scene loads during `SceneSystem.LoadContent()`
- The `VisibilityGroup` is created during the first frame's `DrawCore()`
- The `EffectSystem` has time to compile effects for mod materials
- The scene renders correctly without grey screen

---

## Phase 2: Runtime Scene Switching

### Goal
Allow mods to load multiple scenes and switch between them at runtime (like Skyrim's world transitions).

### Changes

#### 2.1 Create `ModSceneManager` service
**File**: `sources/engine/Stride.Engine/Modding/ModSceneManager.cs` (NEW)

The `ModSceneManager` manages scene lifecycle for mods:
- Pre-loads scenes into memory for fast switching
- Handles scene unloading to prevent memory leaks
- Provides a clean API for runtime scene switching
- Integrates with the game loop to process switches between frames

```csharp
public class ModSceneManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModSceneManager");
    
    private readonly IServiceRegistry _services;
    private readonly Dictionary<string, Scene> _cachedScenes = new();
    private readonly HashSet<string> _preloadedScenes = new();
    private string _pendingSceneUrl;
    private string _currentSceneUrl;
    
    public ModSceneManager(IServiceRegistry services)
    {
        _services = services;
    }
    
    /// <summary>
    /// Pre-load a scene into memory for fast switching later.
    /// Call this during mod initialization to avoid load delays.
    /// Scenes are kept in memory until explicitly unloaded.
    /// </summary>
    public void PreloadScene(string sceneUrl)
    {
        if (_preloadedScenes.Contains(sceneUrl))
        {
            Log.Debug($"[ModSceneManager] Scene already preloaded: {sceneUrl}");
            return;
        }
        
        var contentManager = _services.GetService<ContentManager>();
        if (contentManager == null || !contentManager.Exists(sceneUrl))
        {
            Log.Warning($"[ModSceneManager] Cannot preload scene: {sceneUrl} (not found)");
            return;
        }
        
        try
        {
            var scene = contentManager.Load<Scene>(sceneUrl);
            _cachedScenes[sceneUrl] = scene;
            _preloadedScenes.Add(sceneUrl);
            Log.Info($"[ModSceneManager] Preloaded scene: {sceneUrl} ({scene.Entities.Count} entities)");
        }
        catch (Exception ex)
        {
            Log.Error($"[ModSceneManager] Failed to preload scene {sceneUrl}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Unload a pre-loaded scene from memory.
    /// Call this when a scene is no longer needed to free memory.
    /// </summary>
    public void UnloadScene(string sceneUrl)
    {
        if (!_cachedScenes.TryGetValue(sceneUrl, out var scene))
            return;
        
        // Don't unload the current scene
        if (sceneUrl == _currentSceneUrl)
        {
            Log.Warning($"[ModSceneManager] Cannot unload current scene: {sceneUrl}");
            return;
        }
        
        _cachedScenes.Remove(sceneUrl);
        _preloadedScenes.Remove(sceneUrl);
        
        // Release the scene reference (GC will clean up)
        Log.Info($"[ModSceneManager] Unloaded scene: {sceneUrl}");
    }
    
    /// <summary>
    /// Switch to a different scene at runtime.
    /// The switch happens at the end of the current frame to avoid timing issues.
    /// </summary>
    public void SwitchToScene(string sceneUrl)
    {
        if (sceneUrl == _currentSceneUrl)
        {
            Log.Debug($"[ModSceneManager] Already on scene: {sceneUrl}");
            return;
        }
        
        _pendingSceneUrl = sceneUrl;
        Log.Info($"[ModSceneManager] Scene switch requested: {sceneUrl}");
    }
    
    /// <summary>
    /// Get the URL of the currently active scene.
    /// </summary>
    public string CurrentSceneUrl => _currentSceneUrl;
    
    /// <summary>
    /// Check if a scene is pre-loaded.
    /// </summary>
    public bool IsScenePreloaded(string sceneUrl)
    {
        return _preloadedScenes.Contains(sceneUrl);
    }
    
    /// <summary>
    /// Called by ModSceneSwitchSystem to process pending scene switches.
    /// This runs at the end of each frame to ensure clean transitions.
    /// </summary>
    internal void ProcessPendingSwitch()
    {
        if (string.IsNullOrEmpty(_pendingSceneUrl))
            return;
        
        var sceneUrl = _pendingSceneUrl;
        _pendingSceneUrl = null;
        
        var sceneSystem = _services.GetService<SceneSystem>();
        if (sceneSystem == null)
        {
            Log.Error("[ModSceneManager] SceneSystem not available");
            return;
        }
        
        // Get the scene (from cache or load it)
        Scene scene;
        if (_cachedScenes.TryGetValue(sceneUrl, out var cachedScene))
        {
            scene = cachedScene;
            Log.Debug($"[ModSceneManager] Using cached scene: {sceneUrl}");
        }
        else
        {
            var contentManager = _services.GetService<ContentManager>();
            if (contentManager == null || !contentManager.Exists(sceneUrl))
            {
                Log.Error($"[ModSceneManager] Cannot load scene: {sceneUrl} (not found)");
                return;
            }
            
            try
            {
                scene = contentManager.Load<Scene>(sceneUrl);
                Log.Info($"[ModSceneManager] Loaded scene on-demand: {sceneUrl}");
            }
            catch (Exception ex)
            {
                Log.Error($"[ModSceneManager] Failed to load scene {sceneUrl}: {ex.Message}");
                return;
            }
        }
        
        // Replace the scene instance
        // This happens between frames, so timing issues are avoided
        try
        {
            sceneSystem.SceneInstance = new SceneInstance(_services, scene);
            _currentSceneUrl = sceneUrl;
            Log.Info($"[ModSceneManager] Scene switched to: {sceneUrl} ({scene.Entities.Count} entities)");
        }
        catch (Exception ex)
        {
            Log.Error($"[ModSceneManager] Failed to switch scene: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Called during initialization to set the initial scene URL.
    /// </summary>
    internal void SetInitialScene(string sceneUrl)
    {
        _currentSceneUrl = sceneUrl;
    }
}
```

#### 2.2 Add `ModSceneManager` to `Game.Initialize()`
**File**: `sources/engine/Stride.Engine/Engine/Game.cs`

```csharp
// In Game.Initialize(), after ModHost initialization (around line 430):
var modSceneManager = new Modding.ModSceneManager(Services);
Services.AddService(modSceneManager);
```

#### 2.3 Process pending scene switches at the end of each frame
**File**: `sources/engine/Stride.Engine/Engine/Game.cs`

Add a new game system that processes pending scene switches:

```csharp
// In Game.Initialize(), after ModAutoLoadSystem:
GameSystems.Add(new Modding.ModSceneSwitchSystem(Services));
```

**File**: `sources/engine/Stride.Engine/Modding/ModSceneSwitchSystem.cs` (NEW)

```csharp
public class ModSceneSwitchSystem : GameSystemBase
{
    public ModSceneSwitchSystem(IServiceRegistry registry) : base(registry)
    {
        Enabled = true;
        Visible = false;
        // Run after all other systems
        UpdateOrder = 1000;
    }
    
    public override void Update(GameTime gameTime)
    {
        var modSceneManager = Services.GetService<ModSceneManager>();
        modSceneManager?.ProcessPendingSwitch();
    }
}
```

#### 2.4 Update `ModAutoLoadSystem` to preload mod scenes
**File**: `sources/engine/Stride.Engine/Modding/ModAutoLoadSystem.cs`

```csharp
// In Update(), after verifying the replacement scene:
var modSceneManager = Services.GetService<ModSceneManager>();
if (modSceneManager != null)
{
    // Preload all mod scenes for fast switching
    foreach (var entry in allScenes)
    {
        modSceneManager.PreloadScene(entry.SceneUrl);
    }
}
```

### Usage Example

```csharp
// In a mod's script:
public class TeleportScript : SyncScript
{
    public override void Update()
    {
        if (Input.IsKeyPressed(Keys.T))
        {
            var modSceneManager = Services.GetService<ModSceneManager>();
            modSceneManager?.SwitchToScene("assets/DungeonScene.sdscene");
        }
    }
}
```

### Expected Result
- Mods can preload scenes during initialization for fast switching
- Mods can switch scenes at runtime using `ModSceneManager.SwitchToScene()`
- The switch happens at the end of the frame, avoiding mid-frame timing issues
- Cached scenes load instantly; uncached scenes load on-demand

---

## Phase 3: Ensure Effect Compilation Works

### Goal
Ensure that programmatically-created `Material.New()` materials can compile effects at runtime.

### Changes

#### 3.1 Modify `ModContentManager` to mount shader sources
**File**: `sources/engine/Stride.Engine/Modding/ModContentManager.cs`

Each mod can include a `shaders/` directory with .sdsl files. Mount these in the VirtualFileSystem so the `EffectCompiler` can find them.

```csharp
public void MountModShaders()
{
    var modHost = _services.GetService<ModHost>();
    if (modHost == null) return;
    
    foreach (var mod in modHost.LoadedMods)
    {
        var shadersDir = Path.Combine(mod.ModDirectory, "shaders");
        if (Directory.Exists(shadersDir))
        {
            // Mount the shaders directory in the VFS
            var mountPath = $"/mod-shaders/{mod.Manifest.Id}";
            VirtualFileSystem.Mount(mountPath, new PhysicalFileProvider(shadersDir));
            Log.Info($"[ModContentManager] Mounted mod shaders: {mod.Manifest.Id}");
        }
    }
}
```

Call this method in `ModAutoLoadSystem.Update()` after loading mods.

#### 3.2 Modify `EffectCompiler` to search mod shader directories
**File**: `sources/engine/Stride.Rendering/Rendering/EffectCompiler.cs`

Add logic to search mod shader directories when resolving shader sources:

```csharp
// In ResolveShaderSource() or similar method:
private string ResolveShaderSource(string shaderName)
{
    // First, try the default file provider
    var path = _fileProvider.GetFile(shaderName + ".sdsl");
    if (path != null) return path;
    
    // If not found, search mod shader directories
    var modHost = _services.GetService<ModHost>();
    if (modHost != null)
    {
        foreach (var mod in modHost.LoadedMods)
        {
            var modPath = $"/mod-shaders/{mod.Manifest.Id}/{shaderName}.sdsl";
            if (_fileProvider.Exists(modPath))
                return _fileProvider.GetFile(modPath);
        }
    }
    
    return null;
}
```

### Expected Result
- Mod shaders are accessible to the `EffectCompiler`
- `Material.New()` materials can compile effects at runtime
- Mods can include custom shaders that are used by their materials

---

## Phase 5: VisibilityGroup Initialization Safety

### Goal
Ensure that even if a scene is replaced mid-frame (for runtime switching), the `VisibilityGroup` is properly initialized before the first draw.

### Problem
When `SceneSystem.SceneInstance` is replaced, the new `SceneInstance` has no `VisibilityGroup`. The `VisibilityGroup` is created lazily during `GraphicsCompositor.DrawCore()` when it detects a new `SceneInstance`. However, this happens inside the draw phase, which can cause timing issues.

### Solution
Modify `SceneSystem` to eagerly create the `VisibilityGroup` when `SceneInstance` is set, rather than waiting for `DrawCore()`.

### Changes

#### 5.1 Modify `SceneSystem.SceneInstance` setter
**File**: `sources/engine/Stride.Engine/Engine/SceneSystem.cs`

```csharp
private SceneInstance _sceneInstance;

public SceneInstance SceneInstance
{
    get => _sceneInstance;
    set
    {
        if (_sceneInstance == value)
            return;
        
        _sceneInstance = value;
        
        // Eagerly initialize the VisibilityGroup if we have a GraphicsCompositor
        if (_sceneInstance != null && GraphicsCompositor != null)
        {
            InitializeVisibilityGroup(_sceneInstance);
        }
    }
}

private void InitializeVisibilityGroup(SceneInstance sceneInstance)
{
    // Check if a VisibilityGroup already exists for this RenderSystem
    var renderSystem = GraphicsCompositor.RenderSystem;
    foreach (var vg in sceneInstance.VisibilityGroups)
    {
        if (vg.RenderSystem == renderSystem)
            return; // Already initialized
    }
    
    // Create a new VisibilityGroup
    var visibilityGroup = new VisibilityGroup(renderSystem);
    sceneInstance.VisibilityGroups.Add(visibilityGroup);
    
    Log.Info($"[SceneSystem] Initialized VisibilityGroup for SceneInstance");
}
```

This ensures that when `SceneInstance` is set (either during `LoadContent()` or during runtime switching), the `VisibilityGroup` is created immediately. This avoids the lazy initialization inside `DrawCore()` and ensures all processors are ready before the first draw.

### Expected Result
- `VisibilityGroup` is created when `SceneInstance` is set, not during `DrawCore()`
- `ModelRenderProcessor` is created and initialized before the first draw
- No timing issues with `ObjectNode` references
- Runtime scene switching works cleanly

### Test Cases

1. **Initial scene replacement**
   - Create a test game with a default scene (sphere + skybox)
   - Add a mod that provides a replacement scene (ground + marker)
   - Verify that the mod scene renders correctly (not grey)
   - Verify that the mod's camera, lighting, and geometry are visible

2. **Runtime scene switching**
   - Add a second mod scene (dungeon)
   - Implement a script that switches scenes when a key is pressed
   - Verify that the scene switches correctly
   - Verify that the new scene renders correctly
   - Verify that switching back to the original scene works

3. **Effect compilation**
   - Create a mod with a custom shader
   - Use the custom shader in a `Material.New()` material
   - Verify that the effect compiles without errors
   - Verify that the material renders correctly

### Verification Steps

1. Build the engine: `dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false`
2. Build the test game
3. Run the test game with mods
4. Check the log output for:
   - `[Game] Loaded X mod(s) during initialization`
   - `[Game] Mod scene 'X' will replace default scene`
   - `[ModAutoLoad] Verified mod scene: X (Y entities)`
   - No shader compilation errors
5. Visually verify that the mod scene renders correctly

---

## Implementation Order

1. **Phase 1**: Fix initial scene replacement (highest priority)
   - This is the minimum viable fix for the grey screen issue
   - Estimated effort: 2-3 hours

2. **Phase 3**: Ensure effect compilation works
   - This is required for Phase 1 to work with `Material.New()` materials
   - Estimated effort: 1-2 hours

3. **Phase 2**: Runtime scene switching
   - This is a nice-to-have feature for advanced modding
   - Estimated effort: 3-4 hours

4. **Phase 4**: Testing and verification
   - Estimated effort: 1-2 hours

**Total estimated effort**: 7-11 hours

---

## Risks and Mitigations

### Risk 1: Mod scene loading fails during initialization
**Mitigation**: Add error handling in `LoadModsEarly()`. If mod loading fails, log a warning and fall back to the default scene.

### Risk 2: Effect compilation still fails after mounting mod shaders
**Mitigation**: Pre-compile mod effects during the mod build process. The mod's `asset.db` would include pre-compiled effects for all materials.

### Risk 3: Runtime scene switching causes memory leaks
**Mitigation**: Implement proper scene unloading in `ModSceneManager`. When switching scenes, unload the old scene's entities and clear processor state.

### Risk 4: Scene switching causes visual glitches
**Mitigation**: Add a fade-to-black transition during scene switches. Load the new scene in the background while showing a loading screen.

---

## Phase 6: Edge Cases and Error Handling

### 6.1 Scene Loading Failures
**Problem**: A mod scene might fail to load (missing assets, corrupted data, etc.)

**Solution**: Wrap all scene loading in try-catch blocks and fall back to the default scene:

```csharp
try
{
    var scene = contentManager.Load<Scene>(sceneUrl);
    sceneSystem.SceneInstance = new SceneInstance(services, scene);
}
catch (Exception ex)
{
    Log.Error($"[ModSceneManager] Failed to load scene {sceneUrl}: {ex.Message}");
    Log.Info("[ModSceneManager] Falling back to default scene");
    // Don't change SceneInstance - keep the default scene
}
```

### 6.2 Missing Camera in Mod Scene
**Problem**: A mod scene might not have a camera entity, resulting in a black screen.

**Solution**: After loading a mod scene, check if it has a camera. If not, log a warning:

```csharp
var hasCamera = scene.Entities.Any(e => e.Get<CameraComponent>() != null);
if (!hasCamera)
{
    Log.Warning($"[ModSceneManager] Scene {sceneUrl} has no camera entity - screen will be black");
}
```

### 6.3 Camera Slot Mismatch
**Problem**: The mod scene's camera might not be assigned to the correct compositor slot.

**Solution**: After loading a mod scene, ensure the camera is assigned to the compositor's first slot:

```csharp
var camera = scene.Entities.FirstOrDefault(e => e.Get<CameraComponent>() != null);
if (camera != null)
{
    var cameraComponent = camera.Get<CameraComponent>();
    var compositor = sceneSystem.GraphicsCompositor;
    if (compositor?.Cameras?.Count > 0)
    {
        cameraComponent.Slot = compositor.Cameras[0].ToSlotId();
        Log.Debug($"[ModSceneManager] Assigned camera to slot: {compositor.Cameras[0].Name}");
    }
}
```

### 6.4 Effect Compilation Failures
**Problem**: `Material.New()` materials might fail to compile effects if shader sources are missing.

**Solution**: Add logging to detect effect compilation failures:

```csharp
// In ModContentManager, after mounting mod shaders:
Log.Info("[ModContentManager] Mod shaders mounted - effect compilation should work");

// If effects still fail, the user will see shader compilation errors in the log
```

### 6.5 Memory Leaks from Scene Switching
**Problem**: Repeatedly switching scenes without unloading old scenes can cause memory leaks.

**Solution**: The `ModSceneManager.UnloadScene()` method allows explicit unloading. Additionally, add a warning if too many scenes are cached:

```csharp
if (_cachedScenes.Count > 10)
{
    Log.Warning($"[ModSceneManager] {_cachedScenes.Count} scenes cached - consider unloading unused scenes");
}
```

---

## Implementation Order and Dependencies

### Critical Path
1. **Phase 1** (Fix Initial Scene Replacement) - **MUST DO FIRST**
   - This fixes the immediate grey screen issue
   - Dependencies: None
   - Estimated effort: 2-3 hours

2. **Phase 5** (VisibilityGroup Initialization Safety) - **MUST DO SECOND**
   - This ensures runtime scene switching works correctly
   - Dependencies: Phase 1
   - Estimated effort: 1 hour

3. **Phase 3** (Ensure Effect Compilation Works) - **MUST DO THIRD**
   - This ensures `Material.New()` materials can compile effects
   - Dependencies: Phase 1
   - Estimated effort: 1-2 hours

### Nice-to-Have
4. **Phase 2** (Runtime Scene Switching) - **OPTIONAL**
   - This adds the ability to switch scenes at runtime
   - Dependencies: Phase 1, Phase 5
   - Estimated effort: 3-4 hours

5. **Phase 6** (Edge Cases and Error Handling) - **OPTIONAL**
   - This adds robustness and error handling
   - Dependencies: Phase 1, Phase 2
   - Estimated effort: 1-2 hours

### Testing
6. **Phase 4** (Testing and Verification) - **MUST DO LAST**
   - This verifies all changes work correctly
   - Dependencies: All previous phases
   - Estimated effort: 1-2 hours

---

## Files to Modify

### Core Engine Files
1. `sources/engine/Stride.Engine/Engine/Game.cs`
   - Add `LoadModsEarly()` method
   - Call it in `Initialize()` after `ModAutoLoadSystem` is added
   - Add `ModSceneManager` service registration

2. `sources/engine/Stride.Engine/Engine/SceneSystem.cs`
   - Modify `SceneInstance` setter to eagerly initialize `VisibilityGroup`
   - Add `InitializeVisibilityGroup()` method

3. `sources/engine/Stride.Engine/Modding/ModAutoLoadSystem.cs`
   - Remove scene replacement logic from `Update()`
   - Add mod scene verification logging
   - Add mod scene preloading

### New Files
4. `sources/engine/Stride.Engine/Modding/ModSceneManager.cs` (NEW)
   - Scene caching and lifecycle management
   - Runtime scene switching API

5. `sources/engine/Stride.Engine/Modding/ModSceneSwitchSystem.cs` (NEW)
   - Game system that processes pending scene switches

### Optional Files (Phase 3)
6. `sources/engine/Stride.Engine/Modding/ModContentManager.cs`
   - Add `MountModShaders()` method

7. `sources/engine/Stride.Rendering/Rendering/EffectCompiler.cs`
   - Modify shader resolution to search mod directories

---

## Summary

This plan fixes the scene replacement system by:

1. **Loading mod scenes during initialization** instead of mid-frame, eliminating timing issues
2. **Eagerly initializing the VisibilityGroup** when `SceneInstance` is set, ensuring processors are ready before the first draw
3. **Ensuring effect compilation works** by mounting mod shader directories in the VirtualFileSystem
4. **Adding runtime scene switching** via `ModSceneManager` for advanced modding scenarios
5. **Handling edge cases** like missing cameras, effect compilation failures, and memory leaks

The fix is minimal, targeted, and leverages Stride's existing initialization pipeline rather than fighting against it. By loading mod scenes during `SceneSystem.LoadContent()`, we ensure the entire rendering pipeline initializes correctly, just like it does for the game's default scene.

**Total estimated effort**: 8-12 hours

**Success criteria**:
- [ ] Mod scenes replace the default scene without grey screen
- [ ] Mod scenes render correctly with their own cameras, lighting, and geometry
- [ ] Runtime scene switching works without crashes or visual glitches
- [ ] Effect compilation works for `Material.New()` materials
- [ ] All test cases pass
- [ ] No regressions in existing functionality
