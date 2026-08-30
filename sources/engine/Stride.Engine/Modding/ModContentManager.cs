// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.IO;
using Stride.Core.Serialization.Contents;
using Stride.Core.Storage;
using Stride.Games;
using Stride.Graphics;

namespace Stride.Engine.Modding;

/// <summary>
/// Manages content resolution across the game's built-in content and all loaded mods.
/// Creates a DatabaseFileProvider for each mod and merges them with the game's
/// provider via CompositeFileProviderService.
///
/// Also handles GUID-aware content pipeline: mods include asset-guids.json
/// mapping virtual asset paths to their compiled GUIDs. These are injected
/// into the runtime so scene references to mod assets resolve correctly.
/// </summary>
public class ModContentManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModContentManager");

    private readonly IServiceRegistry _services;
    private readonly CompositeFileProviderService _compositeService;
    private readonly Dictionary<string, DatabaseFileProvider> _modProviders = [];

    // Mods whose platform-specific assets were registered with a fallback directory
    // because GraphicsDevice was not yet initialized. Refreshed on DeviceCreated.
    private readonly Dictionary<string, ModPackage> _deferredPlatformPackages = new(StringComparer.OrdinalIgnoreCase);

    private bool _deviceCreatedSubscribed;

    // GUID mapping: modId -> list of (virtualPath, guid) entries
    private readonly Dictionary<string, List<GuidMapping>> _modGuidMappings = new(StringComparer.OrdinalIgnoreCase);

    public CompositeFileProviderService CompositeService => _compositeService;

    public ModContentManager(IServiceRegistry services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _compositeService = new CompositeFileProviderService();
    }

    /// <summary>
    /// Registers the game's built-in file provider as the base layer.
    /// Must be called before adding mod providers.
    /// Also wires up the composite provider on ContentManager for cross-mod asset resolution.
    /// </summary>
    public void RegisterGameProvider(DatabaseFileProvider gameProvider)
    {
        _compositeService.AddProvider(gameProvider);

        // Wire up composite provider on ContentManager for cross-mod asset resolution
        var contentManager = _services.GetService<ContentManager>();
        if (contentManager != null)
        {
            contentManager.CompositeProvider = _compositeService;
            Log.Info("[ModContentManager] Wired composite provider on ContentManager");
        }

        // Subscribe to device creation so mods registered before GraphicsDevice init
        // can be rebound to the correct platform asset directory before SceneSystem loads content.
        if (!_deviceCreatedSubscribed)
        {
            var graphicsDeviceService = _services.GetService<IGraphicsDeviceService>();
            if (graphicsDeviceService != null)
            {
                graphicsDeviceService.DeviceCreated += OnGraphicsDeviceCreated;
                _deviceCreatedSubscribed = true;
                Log.Info("[ModContentManager] Subscribed to GraphicsDevice.DeviceCreated for platform refresh");
            }
        }

        Log.Info("[ModContentManager] Registered game content provider");
    }

    /// <summary>
    /// Creates a file provider for a mod's assets and adds it to the composite.
    /// Also loads asset-guids.json for GUID-aware content resolution.
    ///
    /// Supports both the new multi-platform layout (assets/windows-vulkan/, assets/windows-dx11/,
    /// assets/windows-dx12/) and the legacy flat layout (assets/index at root).
    /// Platform selection is based on the active GraphicsDevice.Platform.
    /// </summary>
    public void RegisterModContent(ModPackage package)
    {
        if (_modProviders.ContainsKey(package.Manifest.Id))
            return;

        // Load GUID mappings if present
        LoadGuidMappings(package);

        // Each mod gets its own ObjectDatabase + ContentIndexMap for asset resolution
        var modAssetPath = Path.Combine(package.ModDirectory, "assets");
        var modDbPath = Path.Combine(package.ModDirectory, "asset.db");

        // If the mod has a pre-built asset database, use it
        if (File.Exists(modDbPath))
        {
            var objectDatabase = new ObjectDatabase(modDbPath, "index", loadDefaultBundle: false);
            var provider = new DatabaseFileProvider(objectDatabase);
            _compositeService.AddProvider(provider);
            _modProviders[package.Manifest.Id] = provider;

            // Merge mod's ContentIndexMap entries into the game's primary index
            MergeIntoGameIndex(objectDatabase.ContentIndexMap, package.Manifest.Id);

            Log.Info($"[ModContentManager] Registered mod content: {package.Manifest.Id} (database mode, {objectDatabase.ContentIndexMap.GetMergedIdMap().Count()} index entries)");
        }
        else if (Directory.Exists(modAssetPath))
        {
            // Try platform-aware selection first (new multi-platform layout)
            var platformAssetPath = SelectPlatformAssetPath(modAssetPath, package.Manifest.Id);

            // GraphicsDevice may not exist yet during early mod loading. If we had to fall back to
            // an arbitrary platform directory, queue the mod for re-registration on DeviceCreated
            // so SceneSystem loads content from the correct directory.
            var deviceNotReady = GetCurrentPlatformDirectoryName() == null;

            if (platformAssetPath != null && File.Exists(Path.Combine(platformAssetPath, "index")))
            {
                RegisterPlatformAssets(package, platformAssetPath);

                if (deviceNotReady)
                {
                    _deferredPlatformPackages[package.Manifest.Id] = package;
                    Log.Info($"[ModContentManager] Mod '{package.Manifest.Id}' registered with fallback platform assets; will refresh after GraphicsDevice init");
                }
            }
            else if (File.Exists(Path.Combine(modAssetPath, "index")))
            {
                // Legacy flat layout: assets/index at root (no platform subdirectories)
                RegisterPlatformAssets(package, modAssetPath);
            }
            else
            {
                // Assets directory exists but has no compiled index — raw source files only
                Log.Info($"[ModContentManager] Mod '{package.Manifest.Id}' has raw assets in {modAssetPath} (no compiled index — requires asset pipeline)");
            }
        }
        else
        {
            Log.Info($"[ModContentManager] Mod '{package.Manifest.Id}' has no assets — skipping content registration");
        }
    }

    /// <summary>
    /// Selects the platform-specific asset path based on the current GraphicsDevice.Platform.
    /// Falls back to whichever platform directory exists if the exact match isn't present.
    /// </summary>
    private string? SelectPlatformAssetPath(string assetsDir, string modId)
    {
        // Only treat directories matching the {os}-{graphicsapi} pattern as platform dirs.
        // The legacy flat layout has hash bucket subdirs (e.g., "04", "2c", "5f") and "bundles/",
        // "tmp/" — these must NOT be mistaken for platform directories.
        var knownPlatformDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "windows-vulkan", "windows-dx11", "windows-dx12",
            "linux-vulkan", "macos-vulkan", "macos-metal",
        };

        var platformDirs = Directory.GetDirectories(assetsDir)
            .Select(d => Path.GetFileName(d))
            .Where(n => n != null && knownPlatformDirs.Contains(n))
            .ToList();

        if (platformDirs.Count == 0)
            return null; // No platform subdirectories → use legacy flat layout

        // Try to determine current platform from GraphicsDevice
        var currentPlatformDir = GetCurrentPlatformDirectoryName();

        if (currentPlatformDir != null && platformDirs.Contains(currentPlatformDir))
        {
            Log.Info($"[ModContentManager] Selected platform assets: {currentPlatformDir} for mod '{modId}'");
            return Path.Combine(assetsDir, currentPlatformDir);
        }

        // Fallback: use the first available platform directory
        var fallback = platformDirs[0];
        Log.Warning($"[ModContentManager] Exact platform match not found for mod '{modId}'. " +
                    $"Using fallback: {fallback}. Available: {string.Join(", ", platformDirs)}");
        return Path.Combine(assetsDir, fallback);
    }

    /// <summary>
    /// Gets the current platform's directory name based on GraphicsDevice.Platform.
    /// Returns null if the graphics device is not yet available (early loading).
    /// </summary>
    private static string? GetCurrentPlatformDirectoryName()
    {
        try
        {
            // GraphicsDevice.Platform is a static property available once any device is created.
            // During early mod loading (before GraphicsDevice init), this defaults to Null.
            var platform = GraphicsDevice.Platform;
            return platform switch
            {
                GraphicsPlatform.Vulkan => "windows-vulkan",
                GraphicsPlatform.Direct3D11 => "windows-dx11",
                GraphicsPlatform.Direct3D12 => "windows-dx12",
                _ => null, // Null or unknown → let caller fall back
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Registers a platform-specific asset directory as a file provider.
    /// </summary>
    private void RegisterPlatformAssets(ModPackage package, string assetPath)
    {
        // Mount a VFS provider for this mod's platform-specific assets directory
        var vfsUrl = $"/mod-assets-{package.Manifest.Id}";
        try
        {
            VirtualFileSystem.RemountFileSystem(vfsUrl, assetPath);
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModContentManager] Failed to mount VFS for mod '{package.Manifest.Id}': {ex.Message}");
        }

            var objectDatabase = new ObjectDatabase(vfsUrl, "index", loadDefaultBundle: true);
        var provider = new DatabaseFileProvider(objectDatabase);
        _compositeService.AddProvider(provider);
        _modProviders[package.Manifest.Id] = provider;

        // Merge mod's ContentIndexMap entries into the game's primary index
        MergeIntoGameIndex(objectDatabase.ContentIndexMap, package.Manifest.Id);

        Log.Info($"[ModContentManager] Registered mod content: {package.Manifest.Id} (platform assets, {objectDatabase.ContentIndexMap.GetMergedIdMap().Count()} index entries, path: {assetPath})");
    }

    /// <summary>
    /// Merges a mod's ContentIndexMap entries into the game's primary ContentIndexMap.
    /// This enables ContentManager.Exists(url) to find URLs from any mod.
    /// </summary>
    private void MergeIntoGameIndex(IContentIndexMap modIndex, string modId)
    {
        try
        {
            var contentManager = _services.GetService<ContentManager>();
            if (contentManager == null) return;

            var gameProvider = contentManager.FileProvider;
            if (gameProvider?.ContentIndexMap == null) return;

            var gameIndex = gameProvider.ContentIndexMap;
            int merged = 0;
            foreach (var entry in modIndex.GetMergedIdMap())
            {
                gameIndex[entry.Key] = entry.Value;
                merged++;
            }

            if (merged > 0)
                Log.Info($"[ModContentManager] Merged {merged} index entries from mod '{modId}' into game index");
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModContentManager] Failed to merge index for mod '{modId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Called once the GraphicsDevice is created. Re-registers any mods that were
    /// loaded before the device existed so they bind to the correct platform asset
    /// directory before SceneSystem.LoadContent() runs.
    /// </summary>
    private void OnGraphicsDeviceCreated(object? sender, EventArgs e)
    {
        RefreshPlatformProviders();
    }

    /// <summary>
    /// Re-registers mods that were bound to a fallback platform directory because
    /// GraphicsDevice.Platform was not yet available. Scene content has not loaded
    /// when this runs, so switching to the correct directory is safe.
    /// </summary>
    public void RefreshPlatformProviders()
    {
        if (_deferredPlatformPackages.Count == 0)
            return;

        var actualPlatformDir = GetCurrentPlatformDirectoryName();
        if (actualPlatformDir == null)
        {
            Log.Info("[ModContentManager] GraphicsDevice still not available — platform refresh deferred");
            return;
        }

        var deferred = _deferredPlatformPackages.Values.ToList();
        _deferredPlatformPackages.Clear();

        foreach (var package in deferred)
        {
            Log.Info($"[ModContentManager] Refreshing platform-specific content for mod '{package.Manifest.Id}' (selected: {actualPlatformDir})");
            UnregisterModContent(package);
            RegisterModContent(package);
        }
    }

    /// <summary>
    /// Removes a mod's file provider from the composite and clears GUID mappings.
    /// </summary>
    public void UnregisterModContent(ModPackage package)
    {
        if (_modProviders.TryGetValue(package.Manifest.Id, out var provider))
        {
            _compositeService.RemoveProvider(provider);
            provider.Dispose();
            _modProviders.Remove(package.Manifest.Id);
            Log.Info($"[ModContentManager] Unregistered mod content: {package.Manifest.Id}");
        }

        // Clear GUID mappings
        _modGuidMappings.Remove(package.Manifest.Id);
    }

    /// <summary>
    /// Resolves asset content URL across all registered providers.
    /// </summary>
    public Stream? ResolveAsset(string url)
    {
        return _compositeService.OpenStream(url);
    }

    // ──────────────────────────────────────────────
    //  GUID-aware content pipeline
    // ──────────────────────────────────────────────

    /// <summary>
    /// Loads asset-guids.json from a mod's directory.
    /// Format: { "mappings": [ { "virtualPath": "...", "guid": "..." }, ... ] }
    /// </summary>
    private void LoadGuidMappings(ModPackage package)
    {
        var guidFilePath = Path.Combine(package.ModDirectory, "asset-guids.json");
        if (!File.Exists(guidFilePath))
            return;

        try
        {
            var json = File.ReadAllText(guidFilePath);
            var doc = JsonSerializer.Deserialize<AssetGuidManifest>(json);
            if (doc?.Mappings == null || doc.Mappings.Count == 0)
                return;

            var mappings = new List<GuidMapping>();
            foreach (var entry in doc.Mappings)
            {
                if (Guid.TryParse(entry.Guid, out var guid))
                {
                    mappings.Add(new GuidMapping(entry.VirtualPath, guid));
                }
                else
                {
                    Log.Warning($"[ModContentManager] Invalid GUID in asset-guids.json: {entry.Guid} for {entry.VirtualPath}");
                }
            }

            _modGuidMappings[package.Manifest.Id] = mappings;
            Log.Info($"[ModContentManager] Loaded {mappings.Count} GUID mappings for mod '{package.Manifest.Id}'");
            
            // Inject GUID mappings into the runtime asset database so scene references resolve
            InjectGuidMappingsIntoRuntime(mappings, package.Manifest.Id);
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModContentManager] Failed to load asset-guids.json for mod '{package.Manifest.Id}': {ex.Message}");
        }
    }

    /// <summary>
    /// Checks for GUID collisions between incoming mod mappings and existing game/mod GUIDs.
    /// Returns a list of collision descriptions (empty = no collisions).
    /// </summary>
    public List<string> CheckGuidCollisions(string modId)
    {
        var collisions = new List<string>();

        if (!_modGuidMappings.TryGetValue(modId, out var newMappings))
            return collisions;

        // Check against all other registered mods
        foreach (var (existingModId, existingMappings) in _modGuidMappings)
        {
            if (existingModId == modId) continue;

            foreach (var newMapping in newMappings)
            {
                foreach (var existingMapping in existingMappings)
                {
                    if (newMapping.Guid == existingMapping.Guid)
                    {
                        collisions.Add(
                            $"CRITICAL: Mod '{modId}' asset '{newMapping.VirtualPath}' " +
                            $"shares GUID {newMapping.Guid} with mod '{existingModId}' asset '{existingMapping.VirtualPath}'. " +
                            $"Mod loading should be aborted.");
                    }
                }
            }
        }

        return collisions;
    }

    /// <summary>
    /// Gets the GUID for a virtual asset path from a specific mod.
    /// </summary>
    public Guid? GetModAssetGuid(string modId, string virtualPath)
    {
        if (!_modGuidMappings.TryGetValue(modId, out var mappings))
            return null;

        foreach (var mapping in mappings)
        {
            if (string.Equals(mapping.VirtualPath, virtualPath, StringComparison.OrdinalIgnoreCase))
                return mapping.Guid;
        }

        return null;
    }

    /// <summary>
    /// Returns all GUID mappings for a mod.
    /// </summary>
    public IReadOnlyList<GuidMapping> GetModGuidMappings(string modId)
        => _modGuidMappings.TryGetValue(modId, out var mappings) ? mappings : [];

    /// <summary>
    /// Injects GUID mappings into the runtime asset database so scene references to mod assets resolve correctly.
    /// This patches the ContentManager's ContentIndexMap with the mod's virtual-path-to-ObjectId mappings.
    /// </summary>
    private void InjectGuidMappingsIntoRuntime(List<GuidMapping> mappings, string modId)
    {
        try
        {
            var contentManager = _services.GetService<ContentManager>();
            if (contentManager == null)
            {
                Log.Warning($"[ModContentManager] ContentManager not available — GUID injection skipped for mod '{modId}'");
                return;
            }

            // Inject each mod asset URL into the game's ContentIndexMap
            // This enables ContentManager.Load<Scene>("assets/MyScene") to resolve mod assets
            var indexMap = contentManager.FileProvider.ContentIndexMap;
            int injected = 0;
            foreach (var mapping in mappings)
            {
                try
                {
                    indexMap[mapping.VirtualPath] = (ObjectId)mapping.Guid;
                    injected++;
                    Log.Debug($"[ModContentManager] Injected GUID mapping: {mapping.VirtualPath} -> {mapping.Guid} (mod: {modId})");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[ModContentManager] Failed to inject GUID mapping '{mapping.VirtualPath}': {ex.Message}");
                }
            }

            if (injected > 0)
                Log.Info($"[ModContentManager] Injected {injected}/{mappings.Count} GUID mappings for mod '{modId}'");
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModContentManager] GUID injection failed for mod '{modId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Removes injected GUID mappings when a mod is unloaded.
    /// </summary>
    private void RemoveGuidMappingsFromRuntime(string modId)
    {
        try
        {
            if (!_modGuidMappings.TryGetValue(modId, out var mappings))
                return;
                
            Log.Info($"[ModContentManager] Removed {mappings.Count} GUID mappings for mod '{modId}'");
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModContentManager] GUID removal failed for mod '{modId}': {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_deviceCreatedSubscribed)
        {
            var graphicsDeviceService = _services.GetService<IGraphicsDeviceService>();
            if (graphicsDeviceService != null)
                graphicsDeviceService.DeviceCreated -= OnGraphicsDeviceCreated;
        }

        foreach (var provider in _modProviders.Values)
            provider.Dispose();
        _modProviders.Clear();
        _deferredPlatformPackages.Clear();
        _modGuidMappings.Clear();
        _compositeService.Dispose();
    }

    // ──────────────────────────────────────────────
    //  Types
    // ──────────────────────────────────────────────

    /// <summary>
    /// A single GUID mapping entry from asset-guids.json.
    /// </summary>
    public sealed class GuidMapping
    {
        public string VirtualPath { get; }
        public Guid Guid { get; }

        public GuidMapping(string virtualPath, Guid guid)
        {
            VirtualPath = virtualPath;
            Guid = guid;
        }
    }

    /// <summary>
    /// Deserialization target for asset-guids.json.
    /// </summary>
    private sealed class AssetGuidManifest
    {
        public List<AssetGuidEntry>? Mappings { get; set; }
    }

    private sealed class AssetGuidEntry
    {
        public string VirtualPath { get; set; } = "";
        public string Guid { get; set; } = "";
    }
}
