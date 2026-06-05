// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Serialization;

namespace Stride.Engine.Modding;

/// <summary>
/// Central mod lifecycle manager. Handles discovery, validation, loading, unloading,
/// and provides the shared assembly resolution map for cross-ALC type identity.
/// </summary>
public class ModHost
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModHost");

    private readonly IServiceRegistry _services;
    private readonly Dictionary<string, ModPackage> _loadedMods = [];
    private readonly Dictionary<string, Assembly> _sharedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ModTypeRegistry _typeRegistry;
    private readonly ModSystemRegistry _systemRegistry;

    /// <summary>Path to the mods/ directory. Defaults to "mods" relative to game content.</summary>
    public string ModsDirectory { get; set; } = "mods";

    /// <summary>All currently loaded mod packages, keyed by mod ID.</summary>
    public IReadOnlyDictionary<string, ModPackage> LoadedMods => _loadedMods;

    public ModHost(IServiceRegistry services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _typeRegistry = new ModTypeRegistry();
        _systemRegistry = new ModSystemRegistry(services);
    }

    // ──────────────────────────────────────────────
    //  Discovery
    // ──────────────────────────────────────────────

    /// <summary>
    /// Discovers all mod packages in the mods directory without loading them.
    /// </summary>
    public List<ModPackage> DiscoverMods()
    {
        return ModDiscovery.Discover(ModsDirectory);
    }

    // ──────────────────────────────────────────────
    //  Validation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Validates a mod manifest. Returns validation errors (empty = valid).
    /// </summary>
    public List<string> ValidateMod(ModManifest manifest)
    {
        return ModValidator.Validate(manifest);
    }

    // ──────────────────────────────────────────────
    //  Loading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Loads a mod from a directory containing mod.json + assemblies/.
    /// Returns the loaded ModPackage, or throws on failure.
    /// </summary>
    public ModPackage LoadMod(string modDirectory)
    {
        var manifestPath = Path.Combine(modDirectory, "mod.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"mod.json not found in: {modDirectory}");

        ModManifest manifest;
        using (var stream = File.OpenRead(manifestPath))
            manifest = ModManifest.FromStream(stream);

        // Validate
        var errors = ModValidator.Validate(manifest);
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Mod validation failed for '{manifest.Id}':\n  - {string.Join("\n  - ", errors)}");

        // Check for duplicate
        if (_loadedMods.ContainsKey(manifest.Id))
            throw new InvalidOperationException($"Mod '{manifest.Id}' is already loaded. Unload it first.");

        // Create package and load assemblies
        var package = new ModPackage(manifest, modDirectory);
        package.LoadAssemblies(this);

        // Discover the main mod assembly (first non-core DLL loaded)
        if (package.LoadContext != null)
        {
            foreach (var asm in package.LoadContext.Assemblies)
            {
                if (!IsCoreApiAssembly(asm.GetName().Name))
                {
                    package.ModAssembly = asm;
                    break;
                }
            }
        }

        // Register assembly with the shared map
        if (package.ModAssembly != null)
        {
            var name = package.ModAssembly.GetName().Name;
            if (name != null)
                _sharedAssemblies[name] = package.ModAssembly;
        }

        // Register types with serialization system
        if (package.ModAssembly != null)
        {
            _typeRegistry.RegisterModAssembly(package.ModAssembly);
        }

        // Register systems with ECS
        _systemRegistry.RegisterModSystems(package);

        // Load the entry point (IMod)
        if (manifest.EntryPoint != null && package.ModAssembly != null)
        {
            var entryType = package.ModAssembly.GetType(manifest.EntryPoint);
            if (entryType != null)
            {
                package.ModInstance = Activator.CreateInstance(entryType);
            }
            else
            {
                Log.Warning($"[ModHost] Entry point type '{manifest.EntryPoint}' not found in mod '{manifest.Id}'");
            }
        }

        _loadedMods[manifest.Id] = package;
        Log.Info($"[ModHost] Loaded mod: {manifest.Id} v{manifest.Version}");

        return package;
    }

    /// <summary>
    /// Installs a .modpkg file into the mods directory, then loads it.
    /// </summary>
    public ModPackage InstallMod(string modPkgPath)
    {
        // Extract to mods/{id}/
        var tempPkg = ModPackage.FromModPkg(modPkgPath, Path.Combine(ModsDirectory, ".temp_extract"));
        var targetDir = Path.Combine(ModsDirectory, tempPkg.Manifest.Id);

        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, recursive: true);

        // Copy the .modpkg to mods/ and extract
        var destPkg = Path.Combine(ModsDirectory, Path.GetFileName(modPkgPath));
        if (!File.Exists(destPkg) || !string.Equals(Path.GetFullPath(modPkgPath), Path.GetFullPath(destPkg), StringComparison.OrdinalIgnoreCase))
            File.Copy(modPkgPath, destPkg, overwrite: true);

        // Extract in place for direct directory access
        System.IO.Compression.ZipFile.ExtractToDirectory(destPkg, targetDir, overwriteFiles: true);

        return LoadMod(targetDir);
    }

    // ──────────────────────────────────────────────
    //  Unloading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Unloads a mod: unregisters types, systems, unloads ALC, and attempts cleanup.
    /// </summary>
    public void UnloadMod(string modId)
    {
        if (!_loadedMods.TryGetValue(modId, out var package))
            return;

        Log.Info($"[ModHost] Unloading mod: {modId}");

        // Unregister systems first
        _systemRegistry.UnregisterModSystems(package);

        // Unregister types
        if (package.ModAssembly != null)
            _typeRegistry.UnregisterModAssembly(package.ModAssembly);

        // Remove from shared assembly map
        if (package.ModAssembly != null)
        {
            var name = package.ModAssembly.GetName().Name;
            if (name != null)
                _sharedAssemblies.Remove(name);
        }

        // Unload ALC
        package.UnloadAssemblies();

        // Force GC
        for (int i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        _loadedMods.Remove(modId);
        Log.Info($"[ModHost] Unloaded mod: {modId}");
    }

    // ──────────────────────────────────────────────
    //  Enable / Disable
    // ──────────────────────────────────────────────

    /// <summary>
    /// Enables a previously loaded (but disabled/errored) mod.
    /// </summary>
    public void EnableMod(string modId)
    {
        if (!_loadedMods.TryGetValue(modId, out var package))
            throw new InvalidOperationException($"Mod '{modId}' is not loaded.");

        if (package.State != ModState.Disabled && package.State != ModState.Errored)
            return; // Already enabled

        package.State = ModState.Loaded;
        package.IsEnabled = true;
        package.ErrorReason = null;

        if (package.ModAssembly != null)
        {
            _typeRegistry.RegisterModAssembly(package.ModAssembly);
            _systemRegistry.RegisterModSystems(package);
        }

        Log.Info($"[ModHost] Enabled mod: {modId}");
    }

    /// <summary>
    /// Disables a mod without unloading it.
    /// </summary>
    public void DisableMod(string modId)
    {
        if (!_loadedMods.TryGetValue(modId, out var package))
            throw new InvalidOperationException($"Mod '{modId}' is not loaded.");

        if (package.State != ModState.Loaded)
            return; // Already disabled

        package.State = ModState.Disabled;
        package.IsEnabled = false;

        _systemRegistry.UnregisterModSystems(package);

        if (package.ModAssembly != null)
            _typeRegistry.UnregisterModAssembly(package.ModAssembly);

        Log.Info($"[ModHost] Disabled mod: {modId}");
    }

    // ──────────────────────────────────────────────
    //  Shared Assembly Resolution
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called by ModLoadContext to resolve shared dependencies across ALCs.
    /// </summary>
    internal bool TryGetLoadedSharedAssembly(string name, out Assembly? assembly)
    {
        return _sharedAssemblies.TryGetValue(name, out assembly);
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private static bool IsCoreApiAssembly(string? name)
    {
        if (name == null) return false;
        return name.StartsWith("Stride.", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Modulus.Modding.Api", StringComparison.OrdinalIgnoreCase);
    }
}
