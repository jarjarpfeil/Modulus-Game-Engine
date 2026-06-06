// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Scans the mods/ directory for installed mod packages (.modpkg files and unpacked directories).
/// </summary>
public static class ModDiscovery
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModDiscovery");
    
    // Cache: modsDirectory -> (lastScanTime, packages)
    private static readonly Dictionary<string, (DateTime LastScan, List<ModPackage> Packages)> _discoveryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Discovers all mod packages in the given mods directory.
    /// Supports both .modpkg archives and unpacked mod directories containing mod.json.
    /// Results are cached for 5 seconds to avoid repeated filesystem scans.
    /// </summary>
    /// <param name="modsDirectory">Path to the mods/ directory</param>
    /// <returns>List of discovered ModPackage instances (not yet loaded).</returns>
    public static List<ModPackage> Discover(string modsDirectory)
    {
        lock (_cacheLock)
        {
            if (_discoveryCache.TryGetValue(modsDirectory, out var cached))
            {
                if ((DateTime.UtcNow - cached.LastScan) < CacheDuration)
                    return new List<ModPackage>(cached.Packages);
            }
        }
        
        var packages = DiscoverInternal(modsDirectory);
        
        lock (_cacheLock)
        {
            _discoveryCache[modsDirectory] = (DateTime.UtcNow, packages);
        }
        
        return new List<ModPackage>(packages);
    }
    
    /// <summary>
    /// Invalidates the discovery cache for a directory (call after install/uninstall).
    /// </summary>
    public static void InvalidateCache(string modsDirectory)
    {
        lock (_cacheLock)
        {
            _discoveryCache.Remove(modsDirectory);
        }
    }
    
    private static List<ModPackage> DiscoverInternal(string modsDirectory)
    {
        var packages = new List<ModPackage>();

        if (!Directory.Exists(modsDirectory))
        {
            Log.Info($"[ModDiscovery] Mods directory does not exist: {modsDirectory}");
            return packages;
        }

        // Scan for .modpkg files
        foreach (var pkgPath in Directory.GetFiles(modsDirectory, "*.modpkg"))
        {
            try
            {
                // Extract to temp directory for inspection
                var extractDir = Path.Combine(modsDirectory, ".extracted", Path.GetFileNameWithoutExtension(pkgPath));
                var pkg = ModPackage.FromModPkg(pkgPath, extractDir);
                packages.Add(pkg);
                Log.Info($"[ModDiscovery] Found mod package: {pkg.Manifest.Id} v{pkg.Manifest.Version} ({pkgPath})");
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModDiscovery] Failed to read mod package {pkgPath}: {ex.Message}");
            }
        }

        // Scan for unpacked mod directories (containing mod.json)
        foreach (var dir in Directory.GetDirectories(modsDirectory))
        {
            // Skip .extracted (temp extraction dir)
            if (Path.GetFileName(dir).StartsWith('.'))
                continue;

            var manifestPath = Path.Combine(dir, "mod.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                using var stream = File.OpenRead(manifestPath);
                var manifest = ModManifest.FromStream(stream);
                var pkg = new ModPackage(manifest, dir);

                // Don't add duplicates (already found as .modpkg)
                if (!packages.Exists(p => p.Manifest.Id == manifest.Id))
                {
                    packages.Add(pkg);
                    Log.Info($"[ModDiscovery] Found mod directory: {manifest.Id} v{manifest.Version} ({dir})");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModDiscovery] Failed to read mod manifest {manifestPath}: {ex.Message}");
            }
        }

        return packages;
    }
}
