// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Modulus.Modding.Api;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Serialization.Contents;

namespace Stride.Engine.Modding;

/// <summary>
/// Discovers scene assets in loaded mods, resolves their declared load behavior,
/// and provides a scene catalog for games to present to players.
///
/// When a mod ships scenes with no explicit <see cref="ModSceneLoadBehavior"/>,
/// they are added to the <see cref="GetPlayerSelectableScenes"/> catalog so the
/// game can present a scene selection UI.
///
/// Callbacks:
/// - <see cref="RegisterModScenes"/> — called by <see cref="ModHost.LoadMod"/> after content registration.
/// - <see cref="UnregisterModScenes"/> — called by <see cref="ModHost.UnloadMod"/> during cleanup.
/// </summary>
public class ModSceneManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModSceneManager");

    private readonly IServiceRegistry _services;

    /// <summary>Per-mod scene entries indexed by mod ID.</summary>
    private readonly Dictionary<string, List<ModSceneEntry>> _modScenes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Currently loaded scene instances, keyed by "modId:sceneUrl".</summary>
    private readonly Dictionary<string, Scene> _loadedScenes =
        new(StringComparer.OrdinalIgnoreCase);

    public ModSceneManager(IServiceRegistry services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    // ──────────────────────────────────────────────
    //  Catalog — called by ModHost
    // ──────────────────────────────────────────────

    /// <summary>
    /// Scans a mod package's <see cref="ModManifest.Scenes"/> and registers them
    /// in the scene catalog. Safe to call multiple times (idempotent per modId).
    /// </summary>
    public void RegisterModScenes(ModPackage package)
    {
        var manifest = package.Manifest;
        if (manifest.Scenes == null || manifest.Scenes.Count == 0)
            return;

        // Replace existing entries (supports mod reload)
        _modScenes.Remove(manifest.Id);

        var entries = new List<ModSceneEntry>(manifest.Scenes.Count);
        foreach (var decl in manifest.Scenes)
        {
            if (string.IsNullOrWhiteSpace(decl.Path))
            {
                Log.Warning($"[ModSceneManager] Mod '{manifest.Id}' has a scene entry with empty path — skipped");
                continue;
            }

            var displayName = decl.Name ?? Path.GetFileNameWithoutExtension(decl.Path);
            var behavior = decl.ParsedBehavior;

            entries.Add(new ModSceneEntry(manifest.Id, decl.Path, displayName, behavior));
            Log.Info($"[ModSceneManager] Registered scene: {displayName} [{behavior}] (mod: {manifest.Id})");
        }

        if (entries.Count > 0)
            _modScenes[manifest.Id] = entries;
    }

    /// <summary>
    /// Removes all scene entries for a mod and unloads any scenes it had loaded.
    /// </summary>
    public void UnregisterModScenes(string modId)
    {
        // Unload any scenes this mod still has loaded
        var sceneKeys = new List<string>();
        foreach (var key in _loadedScenes.Keys)
        {
            if (key.StartsWith(modId + ":", StringComparison.OrdinalIgnoreCase))
                sceneKeys.Add(key);
        }
        foreach (var key in sceneKeys)
        {
            try
            {
                UnloadSceneByKey(key);
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModSceneManager] Error unloading scene '{key}' during mod unload: {ex.Message}");
            }
        }

        _modScenes.Remove(modId);
        Log.Info($"[ModSceneManager] Unregistered scenes for mod '{modId}'");
    }

    // ──────────────────────────────────────────────
    //  Catalog queries
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns all scenes with no explicit behavior (or <see cref="ModSceneLoadBehavior.MenuSelect"/>).
    /// These are scenes the game should present to the player for selection.
    /// </summary>
    public IReadOnlyList<ModSceneEntry> GetPlayerSelectableScenes()
    {
        var result = new List<ModSceneEntry>();
        foreach (var (_, entries) in _modScenes)
        {
            foreach (var entry in entries)
            {
                if (entry.IsPlayerSelectable)
                    result.Add(entry);
            }
        }
        return result;
    }

    /// <summary>Returns all scenes across every loaded mod.</summary>
    public IReadOnlyList<ModSceneEntry> GetAllModScenes()
    {
        var result = new List<ModSceneEntry>();
        foreach (var (_, entries) in _modScenes)
            result.AddRange(entries);
        return result;
    }

    /// <summary>Returns all scenes for a specific mod.</summary>
    public IReadOnlyList<ModSceneEntry> GetModScenes(string modId)
    {
        return _modScenes.TryGetValue(modId, out var entries) ? entries : [];
    }

    /// <summary>
    /// Finds a scene entry by mod ID and scene path.
    /// </summary>
    public ModSceneEntry? FindEntry(string modId, string sceneUrl)
    {
        if (!_modScenes.TryGetValue(modId, out var entries))
            return null;

        foreach (var entry in entries)
        {
            if (string.Equals(entry.SceneUrl, sceneUrl, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }

    /// <summary>Returns the number of mods with registered scenes.</summary>
    public int ModSceneCount => _modScenes.Count;

    /// <summary>Returns the total number of scene entries across all mods.</summary>
    public int TotalEntryCount
    {
        get
        {
            int count = 0;
            foreach (var (_, entries) in _modScenes)
                count += entries.Count;
            return count;
        }
    }

    // ──────────────────────────────────────────────
    //  Scene loading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Loads a mod scene and applies its declared behavior.
    /// Uses <see cref="ContentManager"/> to resolve the scene asset URL
    /// (requires a compiled asset database in the mod).
    ///
    /// Behavior handling:
    /// - <see cref="ModSceneLoadBehavior.Replace"/>: replaces the active <see cref="SceneSystem.SceneInstance"/>.
    /// - <see cref="ModSceneLoadBehavior.Additive"/>: adds the scene as a child of the root scene.
    /// - <see cref="ModSceneLoadBehavior.MenuSelect"/> and <see cref="ModSceneLoadBehavior.Background"/>:
    ///   loaded additively (the game is responsible for any special handling).
    /// </summary>
    /// <param name="modId">The mod that owns the scene.</param>
    /// <param name="sceneUrl">The scene asset URL within the mod (from mod.json).</param>
    /// <returns>The loaded scene, or null on failure.</returns>
    public Scene? LoadScene(string modId, string sceneUrl)
    {
        var key = $"{modId}:{sceneUrl}";

        // Already loaded?
        if (_loadedScenes.ContainsKey(key))
        {
            Log.Warning($"[ModSceneManager] Scene '{key}' is already loaded");
            return _loadedScenes[key];
        }

        var contentManager = _services.GetService<ContentManager>();
        if (contentManager == null)
        {
            Log.Error("[ModSceneManager] ContentManager not available — cannot load scene");
            return null;
        }

        // Find the mod package to get its directory
        var modHost = _services.GetService<ModHost>();
        ModPackage? package = null;
        if (modHost?.LoadedMods.TryGetValue(modId, out var pkg) == true)
            package = pkg;

        Scene? scene = null;

        // Strategy 1: Try ContentManager (works when asset.db or compiled assets exist)
        // Note: We always try Load() regardless of Exists() result because Exists() only checks
        // the primary provider's ContentIndexMap, while Load() has TryLoadFromComposite fallback
        // that checks all composite providers (including mod ObjectDatabases).
        try
        {
            scene = contentManager.Load<Scene>(sceneUrl);
            if (scene != null)
                Log.Info($"[ModSceneManager] Loaded scene '{sceneUrl}' via ContentManager (mod: {modId})");
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModSceneManager] ContentManager load failed for '{sceneUrl}': {ex.Message}");
        }

        // Strategy 2: Try direct file loading from mod directory
        if (scene == null && package != null)
        {
            scene = TryLoadSceneFromFile(package.ModDirectory, sceneUrl);
            if (scene != null)
                Log.Info($"[ModSceneManager] Loaded scene '{sceneUrl}' from mod file (mod: {modId})");
        }

        if (scene == null)
        {
            Log.Warning($"[ModSceneManager] Could not load scene '{sceneUrl}' (mod: {modId}) — no compiled asset.db and no matching .sdscene file");
            return null;
        }

        // Determine behavior
        var entry = FindEntry(modId, sceneUrl);
        var behavior = entry?.Behavior ?? ModSceneLoadBehavior.MenuSelect;

        // Apply behavior
        var sceneSystem = _services.GetService<SceneSystem>();
        switch (behavior)
        {
            case ModSceneLoadBehavior.Replace:
                if (sceneSystem != null)
                {
                    sceneSystem.SceneInstance = new SceneInstance(_services, scene);
                    WireCameraSlots(scene, sceneSystem);
                    Log.Info($"[ModSceneManager] Replaced active scene with '{key}'");
                }
                else
                {
                    Log.Warning("[ModSceneManager] SceneSystem not available — scene loaded but not activated");
                }
                break;

            case ModSceneLoadBehavior.Additive:
            case ModSceneLoadBehavior.MenuSelect:
            case ModSceneLoadBehavior.Background:
                if (sceneSystem?.SceneInstance?.RootScene != null)
                {
                    sceneSystem.SceneInstance.RootScene.Children.Add(scene);
                    Log.Info($"[ModSceneManager] Additively loaded scene '{key}'");
                }
                else
                {
                    Log.Warning("[ModSceneManager] No active root scene — scene loaded but not attached");
                }
                break;
        }

        _loadedScenes[key] = scene;
        return scene;
    }

    /// <summary>
    /// Unloads a mod scene and removes it from the scene graph.
    /// </summary>
    public void UnloadScene(string modId, string sceneUrl)
    {
        var key = $"{modId}:{sceneUrl}";
        UnloadSceneByKey(key);
    }

    private void UnloadSceneByKey(string key)
    {
        if (!_loadedScenes.TryGetValue(key, out var scene))
            return;

        // Remove from parent's Children collection
        if (scene.Parent != null)
        {
            scene.Parent.Children.Remove(scene);
        }

        // If it's the root scene of a SceneInstance, set to empty scene
        var sceneSystem = _services.GetService<SceneSystem>();
        if (sceneSystem?.SceneInstance?.RootScene == scene)
        {
            sceneSystem.SceneInstance = new SceneInstance(_services, new Scene());
        }

        // Dispose the scene
        try
        {
            scene.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModSceneManager] Error disposing scene '{key}': {ex.Message}");
        }

        _loadedScenes.Remove(key);
        Log.Info($"[ModSceneManager] Unloaded scene '{key}'");
    }

    /// <summary>
    /// Attempts to load a scene directly from a .sdscene file in the mod directory.
    /// This is a fallback for when no compiled asset.db exists.
    /// Returns null if the file doesn't exist or can't be loaded.
    /// </summary>
    private Scene? TryLoadSceneFromFile(string modDirectory, string sceneUrl)
    {
        // Map URL to file path: "assets/Scene.sdscene" → modDir/assets/Scene.sdscene
        var filePath = Path.Combine(modDirectory, sceneUrl.Replace('/', Path.DirectorySeparatorChar));

        // Try .sdscene extension if not present
        if (!File.Exists(filePath) && !filePath.EndsWith(".sdscene"))
            filePath += ".sdscene";

        if (!File.Exists(filePath))
        {
            // Try looking in the assets subdirectory
            filePath = Path.Combine(modDirectory, "assets", Path.GetFileName(sceneUrl));
            if (!File.Exists(filePath) && !filePath.EndsWith(".sdscene"))
                filePath += ".sdscene";

            if (!File.Exists(filePath))
                return null;
        }

        // For now, direct file loading of .sdscene requires the content pipeline.
        // The composite provider chain handles compiled assets automatically.
        Log.Info($"[ModSceneManager] Found scene file '{filePath}' — requires compiled asset.db for loading");
        return null;
    }

    /// <summary>Returns the number of currently loaded scene instances.</summary>
    public int LoadedSceneInstanceCount => _loadedScenes.Count;

    /// <summary>
    /// Assigns unassigned CameraComponents to the GraphicsCompositor's camera slots.
    /// Mod scenes are compiled without knowledge of the runtime compositor, so cameras
    /// need their Slot wired after the scene is activated.
    /// </summary>
    private void WireCameraSlots(Scene scene, SceneSystem sceneSystem)
    {
        var compositor = sceneSystem.GraphicsCompositor;
        if (compositor?.Cameras == null || compositor.Cameras.Count == 0)
            return;

        var firstSlot = compositor.Cameras[0];
        var slotId = firstSlot.ToSlotId();

        foreach (var entity in scene.Entities)
        {
            var camera = entity.Get<CameraComponent>();
            if (camera != null && camera.Slot == default)
            {
                camera.Slot = slotId;
                Log.Info($"[ModSceneManager] Assigned camera '{entity.Name}' to compositor slot '{firstSlot.Name ?? "Main"}'");
            }
        }
    }

    /// <summary>
    /// Checks whether a specific mod scene is currently loaded.
    /// </summary>
    public bool IsSceneLoaded(string modId, string sceneUrl)
    {
        var key = $"{modId}:{sceneUrl}";
        return _loadedScenes.ContainsKey(key);
    }

    /// <summary>
    /// Disposes all loaded scenes. Called during engine shutdown.
    /// </summary>
    public void Dispose()
    {
        foreach (var (key, scene) in _loadedScenes)
        {
            try
            {
                scene.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModSceneManager] Error disposing scene '{key}' during shutdown: {ex.Message}");
            }
        }
        _loadedScenes.Clear();
        _modScenes.Clear();
    }
}

/// <summary>
/// Represents a scene entry in the mod scene catalog.
/// Returned by <see cref="ModSceneManager"/> queries.
/// </summary>
public sealed class ModSceneEntry
{
    /// <summary>The mod that owns this scene.</summary>
    public string ModId { get; }

    /// <summary>The scene asset URL within the mod (from mod.json).</summary>
    public string SceneUrl { get; }

    /// <summary>Human-readable display name for UI menus.</summary>
    public string DisplayName { get; }

    /// <summary>The declared load behavior for this scene.</summary>
    public ModSceneLoadBehavior Behavior { get; }

    /// <summary>
    /// True when the engine should present this scene to the player for selection.
    /// </summary>
    public bool IsPlayerSelectable => Behavior == ModSceneLoadBehavior.MenuSelect;

    public ModSceneEntry(string modId, string sceneUrl, string displayName, ModSceneLoadBehavior behavior)
    {
        ModId = modId ?? throw new ArgumentNullException(nameof(modId));
        SceneUrl = sceneUrl ?? throw new ArgumentNullException(nameof(sceneUrl));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Behavior = behavior;
    }
}
