// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.IO;
using Stride.Core.Storage;

namespace Stride.Engine.Modding;

/// <summary>
/// Manages content resolution across the game's built-in content and all loaded mods.
/// Creates a DatabaseFileProvider for each mod and merges them with the game's
/// provider via CompositeFileProviderService.
/// </summary>
public class ModContentManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModContentManager");

    private readonly IServiceRegistry _services;
    private readonly CompositeFileProviderService _compositeService;
    private readonly Dictionary<string, DatabaseFileProvider> _modProviders = [];

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
    /// Mod providers are added after the game provider so mods can override game assets.
    /// </summary>
    public void RegisterModContent(ModPackage package)
    {
        if (_modProviders.ContainsKey(package.Manifest.Id))
            return;

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
    /// Removes a mod's file provider from the composite.
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
    }

    /// <summary>
    /// Resolves asset content URL across all registered providers.
    /// Returns the first matching stream from mod providers (highest priority first),
    /// falling back to the game provider.
    /// </summary>
    public Stream? ResolveAsset(string url)
    {
        return _compositeService.OpenStream(url);
    }

    public void Dispose()
    {
        foreach (var provider in _modProviders.Values)
            provider.Dispose();
        _modProviders.Clear();
        _compositeService.Dispose();
    }
}
