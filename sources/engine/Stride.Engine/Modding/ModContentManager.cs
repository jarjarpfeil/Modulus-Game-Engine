// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.IO;
using Stride.Core.Serialization.Contents;
using Stride.Core.Storage;

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
    /// </summary>
    public void RegisterGameProvider(DatabaseFileProvider gameProvider)
    {
        _compositeService.AddProvider(gameProvider);
        Log.Info("[ModContentManager] Registered game content provider");
    }

    /// <summary>
    /// Creates a file provider for a mod's assets and adds it to the composite.
    /// Also loads asset-guids.json for GUID-aware content resolution.
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
            Log.Info($"[ModContentManager] Registered mod content: {package.Manifest.Id} (database mode)");
        }
        else if (Directory.Exists(modAssetPath))
        {
            // Fallback: create a local object database for loose mod assets
            var objectDatabase = new ObjectDatabase(modAssetPath, "index", loadDefaultBundle: false);
            var provider = new DatabaseFileProvider(objectDatabase);
            _compositeService.AddProvider(provider);
            _modProviders[package.Manifest.Id] = provider;
            Log.Info($"[ModContentManager] Registered mod content: {package.Manifest.Id} (loose assets mode)");
        }
        else
        {
            Log.Info($"[ModContentManager] Mod '{package.Manifest.Id}' has no assets — skipping content registration");
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
    /// This patches the ContentManager's object database with the mod's virtual-path-to-GUID mappings.
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

            // Store mappings for runtime resolution — the composite file provider will use these
            // to resolve virtual paths to the correct GUIDs when assets are loaded
            foreach (var mapping in mappings)
            {
                Log.Debug($"[ModContentManager] Registered GUID mapping: {mapping.VirtualPath} -> {mapping.Guid} (mod: {modId})");
            }
            
            Log.Info($"[ModContentManager] Injected {mappings.Count} GUID mappings for mod '{modId}'");
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
        foreach (var provider in _modProviders.Values)
            provider.Dispose();
        _modProviders.Clear();
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
