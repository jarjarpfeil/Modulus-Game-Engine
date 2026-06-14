// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Modulus.Modding.Api;
using Stride.Core;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Represents a scene entry registered by a mod.
/// </summary>
public sealed class ModSceneEntry
{
    /// <summary>The mod ID that registered this scene.</summary>
    public string ModId { get; set; } = string.Empty;

    /// <summary>The URL/path to the scene asset.</summary>
    public string SceneUrl { get; set; } = string.Empty;

    /// <summary>Human-readable display name for UI menus.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>How this scene integrates with the host game.</summary>
    public Modulus.Modding.Api.ModSceneLoadBehavior Behavior { get; set; }

    /// <summary>Whether this scene is player-selectable (MenuSelect behavior).</summary>
    public bool IsPlayerSelectable => Behavior == Modulus.Modding.Api.ModSceneLoadBehavior.MenuSelect;
}

/// <summary>
/// Manages scene lifecycle for mods, enabling runtime scene switching.
/// Provides pre-loading of scenes for fast switching and handles scene unloading to prevent memory leaks.
/// </summary>
public class ModSceneManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModSceneManager");

    private readonly IServiceRegistry _services;
    private readonly Dictionary<string, Scene> _cachedScenes = new();
    private readonly HashSet<string> _preloadedScenes = new();
    private readonly List<ModSceneEntry> _registeredScenes = new();
    private string _pendingSceneUrl;
    private string _currentSceneUrl;

    public static string OriginalSceneUrl { get; set; }

    public ModSceneManager(IServiceRegistry services)
    {
        _services = services;
    }

    /// <summary>
    /// Gets all registered mod scenes.
    /// </summary>
    public IReadOnlyList<ModSceneEntry> GetAllModScenes()
    {
        return _registeredScenes;
    }

    /// <summary>
    /// Gets player-selectable scenes (those with Replace behavior).
    /// </summary>
    public IReadOnlyList<ModSceneEntry> GetPlayerSelectableScenes()
    {
        return _registeredScenes.Where(s => s.Behavior == Modulus.Modding.Api.ModSceneLoadBehavior.Replace).ToList();
    }

    /// <summary>
    /// Find a specific scene entry by mod ID and scene URL.
    /// </summary>
    public ModSceneEntry? FindEntry(string modId, string sceneUrl)
    {
        return _registeredScenes.FirstOrDefault(s => s.ModId == modId && s.SceneUrl == sceneUrl);
    }

    /// <summary>
    /// Gets all scenes registered by a specific mod.
    /// </summary>
    public IReadOnlyList<ModSceneEntry> GetModScenes(string modId)
    {
        return _registeredScenes.Where(s => s.ModId == modId).ToList();
    }

    /// <summary>
    /// Gets the total number of registered scene entries across all mods.
    /// </summary>
    public int TotalEntryCount => _registeredScenes.Count;

    /// <summary>
    /// Gets the number of mods that have registered scenes.
    /// </summary>
    public int ModSceneCount => _registeredScenes.Select(s => s.ModId).Distinct().Count();

    /// <summary>
    /// Register scenes from a mod package. Called by ModHost when a mod is loaded.
    /// </summary>
    public void RegisterModScenes(ModPackage package)
    {
        if (package?.Manifest?.Scenes == null) return;

        foreach (var sceneEntry in package.Manifest.Scenes)
        {
            // Check if already registered
            if (_registeredScenes.Any(s => s.SceneUrl == sceneEntry.Path && s.ModId == package.Manifest.Id))
            {
                Log.Debug($"[ModSceneManager] Scene already registered: {sceneEntry.Path} from {package.Manifest.Id}");
                continue;
            }

            var entry = new ModSceneEntry
            {
                ModId = package.Manifest.Id,
                SceneUrl = sceneEntry.Path,
                DisplayName = sceneEntry.Name ?? sceneEntry.Path,
                Behavior = sceneEntry.ParsedBehavior,
            };

            _registeredScenes.Add(entry);
            Log.Info($"[ModSceneManager] Registered scene: {entry.DisplayName} ({entry.SceneUrl}) from {entry.ModId}");
        }
    }

    /// <summary>
    /// Unregister all scenes from a mod. Called by ModHost when a mod is unloaded.
    /// </summary>
    public void UnregisterModScenes(string modId)
    {
        var removed = _registeredScenes.RemoveAll(s => s.ModId == modId);
        if (removed > 0)
        {
            Log.Info($"[ModSceneManager] Unregistered {removed} scene(s) from {modId}");
        }
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

        var contentManager = _services.GetService<Stride.Core.Serialization.Contents.ContentManager>();
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
            var contentManager = _services.GetService<Stride.Core.Serialization.Contents.ContentManager>();
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
