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

    /// <summary>
    /// Discovers all mod packages in the given mods directory.
    /// Supports both .modpkg archives and unpacked mod directories containing mod.json.
    /// </summary>
    /// <param name="modsDirectory">Path to the mods/ directory</param>
    /// <returns>List of discovered ModPackage instances (not yet loaded).</returns>
    public static List<ModPackage> Discover(string modsDirectory)
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
