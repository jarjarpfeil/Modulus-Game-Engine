// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stride.Core.Diagnostics;
using Stride.Rendering;
using Stride.Shaders;

namespace Stride.Engine.Modding;

/// <summary>
/// Manages shader extraction and registration from mod packages.
///
/// How shader integration works in Stride:
/// 1. Materials reference shaders by name (e.g., "CustomPBR")
/// 2. EffectCompiler resolves shader sources via the FileProvider
/// 3. The CompositeFileProviderService routes lookups to mod directories
/// 4. EffectCompiler finds .sdsl source files in the mod's shaders/ directory
/// 5. The shader is compiled and cached by the EffectSystem
///
/// For pre-compiled bytecode (.sdbundle):
/// - The mod developer compiles shaders offline via the AssetCompiler
/// - The .modpkg contains the compiled bytecode in assets/{platform}/bundles/
/// - The EffectSystem's cache picks up pre-compiled results via the merged ContentIndexMap
///
/// For extra shader bytecode bundles in shaders/:
/// - RegisterShaderBytecodeBundles scans the shaders/ directory
/// - Each .sdbundle file contains standalone compiled effect bytecode
/// - These are registered with the EffectSystem for runtime resolution
///
/// Fallback: If a mod material references a shader that isn't available
/// (e.g., Game B loads a mod built for Game A), the material renders with
/// a default PBR shader — the mesh stays visible (but may not be visible
/// at all if EffectSystem can't resolve it — see Material.New() limitation).
/// </summary>
public sealed class ModShaderManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModShaderManager");

    // modId -> list of registered shader entries with file validation
    private readonly Dictionary<string, List<RegisteredShader>> _registeredShaders = new(StringComparer.OrdinalIgnoreCase);

    // modId -> list of shader bytecode bundle paths
    private readonly Dictionary<string, List<string>> _shaderBytecodeBundles = new(StringComparer.OrdinalIgnoreCase);

    // Reference to the EffectSystem for shader registration
    private EffectSystem? _effectSystem;

    /// <summary>
    /// The currently-connected <see cref="EffectSystem"/>, or null if not yet wired.
    /// </summary>
    public EffectSystem? EffectSystem => _effectSystem;

    /// <summary>
    /// Sets the EffectSystem instance for shader registration.
    /// Call this during engine initialization before loading mods.
    /// </summary>
    public void SetEffectSystem(EffectSystem effectSystem)
    {
        _effectSystem = effectSystem ?? throw new ArgumentNullException(nameof(effectSystem));
        Log.Info("[ModShaderManager] EffectSystem connected");
    }

    /// <summary>
    /// Registers all shaders from a mod's manifest.
    /// Validates that shader files exist on disk.
    /// </summary>
    public void RegisterModShaders(string modId, ModManifest manifest)
    {
        if (manifest.Shaders == null || manifest.Shaders.Count == 0)
            return;

        var registered = new List<RegisteredShader>();

        foreach (var shader in manifest.Shaders)
        {
            try
            {
                var entry = new RegisteredShader
                {
                    Name = shader.Name,
                    Path = shader.Path,
                    Exists = false, // Will be validated when mod directory is known
                };

                registered.Add(entry);
                Log.Info($"[ModShaderManager] Registered shader '{shader.Name}' from mod '{modId}' (path: {shader.Path})");
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModShaderManager] Failed to register shader '{shader.Name}' from mod '{modId}': {ex.Message}");
            }
        }

        if (registered.Count > 0)
            _registeredShaders[modId] = registered;
    }

    /// <summary>
    /// Scans the mod's shaders/ directory for .sdbundle (shader bytecode) files
    /// and registers them with the EffectSystem for runtime resolution.
    ///
    /// Pre-compiled bytecode in the mod's asset bundles is already resolvable
    /// through the merged ContentIndexMap. This method handles extra standalone
    /// shader bytecode bundles that ship in the shaders/ directory, registering
    /// them with EffectSystem.RegisterPrecompiledBytecode so that LoadEffect
    /// returns them directly without invoking the shader compiler.
    ///
    /// Mod authors declare each .sdbundle file in mod.json's "shaders" array:
    ///   "shaders": [ { "name": "CustomPBR", "path": "shaders/custom-pbr.sdbundle" } ]
    /// The "name" is the effect name materials reference. The "path" is the
    /// .sdbundle file location relative to the mod root.
    /// </summary>
    /// <param name="modId">The mod ID.</param>
    /// <param name="modDirectory">The mod's root directory.</param>
    /// <param name="manifest">The mod manifest (provides shader name → path mapping).</param>
    /// <returns>Number of shader bytecode bundles registered with EffectSystem.</returns>
    public int RegisterShaderBytecodeBundles(string modId, string modDirectory, ModManifest manifest)
    {
        if (_effectSystem == null)
        {
            Log.Warning($"[ModShaderManager] Cannot register shader bytecode for mod '{modId}' — EffectSystem not set. Call SetEffectSystem first.");
            return 0;
        }

        if (manifest.Shaders == null || manifest.Shaders.Count == 0)
        {
            // No shader declarations in the manifest — try to scan anyway, but we won't have
            // the effect name mapping needed to register them with EffectSystem.
            var shadersDir = Path.Combine(modDirectory, "shaders");
            if (Directory.Exists(shadersDir))
            {
                var orphanedBundles = Directory.GetFiles(shadersDir, "*.sdbundle", SearchOption.AllDirectories);
                if (orphanedBundles.Length > 0)
                {
                    Log.Warning($"[ModShaderManager] Found {orphanedBundles.Length} .sdbundle file(s) in mod '{modId}' but the manifest has no 'shaders' declarations. " +
                                 "Add each .sdbundle to the 'shaders' array with a 'name' and 'path' to register it with EffectSystem.");
                }
            }
            return 0;
        }

        var bundles = new List<string>();

        foreach (var shader in manifest.Shaders)
        {
            var fullPath = Path.Combine(modDirectory, shader.Path);
            if (!File.Exists(fullPath))
            {
                Log.Warning($"[ModShaderManager] Shader bytecode file not found for '{shader.Name}': {fullPath}");
                continue;
            }

            try
            {
                // Skip shader source files — they are not pre-compiled bytecode bundles.
                var ext = Path.GetExtension(shader.Path);
                if (string.Equals(ext, ".sdsl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, ".sdfx", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info($"[ModShaderManager] Shader '{shader.Name}' in mod '{modId}' is a source file ({ext}), not pre-compiled bytecode — skipping bytecode registration. Compile it to a .sdbundle to enable runtime pre-compiled registration.");
                    continue;
                }

                // Deserialize the .sdbundle file using Stride's EffectBytecode format.
                // .sdbundle files contain a serialized EffectBytecode with magic header 0xEFFEC009.
                using var stream = File.OpenRead(fullPath);
                var bytecode = EffectBytecode.FromStream(stream);

                if (bytecode == null)
                {
                    Log.Error($"[ModShaderManager] Invalid .sdbundle file (missing magic header) for shader '{shader.Name}' in mod '{modId}': {fullPath}");
                    continue;
                }

                // Register with EffectSystem so LoadEffect(name) returns this bytecode without compilation.
                _effectSystem.RegisterPrecompiledBytecode(shader.Name, bytecode);
                bundles.Add(fullPath);

                Log.Info($"[ModShaderManager] Registered precompiled bytecode for shader '{shader.Name}' from mod '{modId}' " +
                         $"(stages: {bytecode.Stages?.Length ?? 0}, file: {Path.GetFileName(fullPath)})");
            }
            catch (Exception ex)
            {
                Log.Error($"[ModShaderManager] Failed to register shader bytecode for '{shader.Name}' from mod '{modId}': {ex.Message}");
            }
        }

        if (bundles.Count > 0)
            _shaderBytecodeBundles[modId] = bundles;

        // Also scan for .sdsl source files (for runtime compilation fallback if bytecode is missing)
        var shadersDirInfo = Path.Combine(modDirectory, "shaders");
        if (Directory.Exists(shadersDirInfo))
        {
            foreach (var file in Directory.GetFiles(shadersDirInfo, "*.sdsl", SearchOption.AllDirectories))
            {
                Log.Info($"[ModShaderManager] Found shader source: {Path.GetFileName(file)} (mod: {modId}) — available for runtime compilation if bytecode is missing");
            }
        }

        return bundles.Count;
    }

    /// <summary>
    /// Validates that registered shader files actually exist on disk.
    /// Call this after the mod directory is known.
    /// </summary>
    public List<string> ValidateShaderFiles(string modId, string modDirectory)
    {
        var missing = new List<string>();

        if (!_registeredShaders.TryGetValue(modId, out var shaders))
            return missing;

        foreach (var shader in shaders)
        {
            var fullPath = Path.Combine(modDirectory, shader.Path);
            shader.Exists = File.Exists(fullPath);

            if (!shader.Exists)
            {
                missing.Add(shader.Path);
                Log.Warning($"[ModShaderManager] Shader file not found: {fullPath}");
            }
            else
            {
                Log.Info($"[ModShaderManager] Validated shader '{shader.Name}': {fullPath}");
            }
        }

        return missing;
    }

    /// <summary>
    /// Gets the full disk paths to all registered shader files for a mod.
    /// Used by ModContentManager to register shader directories with the file provider.
    /// </summary>
    public List<string> GetShaderPaths(string modId, string modDirectory)
    {
        var paths = new List<string>();

        if (!_registeredShaders.TryGetValue(modId, out var shaders))
            return paths;

        foreach (var shader in shaders)
        {
            var fullPath = Path.Combine(modDirectory, shader.Path);
            if (File.Exists(fullPath))
                paths.Add(fullPath);
        }

        return paths;
    }

    /// <summary>
    /// Gets the paths to all shader bytecode bundles (.sdbundle) for a mod.
    /// </summary>
    public IReadOnlyList<string> GetShaderBytecodeBundles(string modId)
    {
        return _shaderBytecodeBundles.TryGetValue(modId, out var bundles) ? bundles : [];
    }

    /// <summary>
    /// Unregisters all shaders belonging to a mod, including precompiled bytecode
    /// registered with EffectSystem.
    /// </summary>
    /// <param name="modId">The mod ID.</param>
    /// <param name="manifest">The mod manifest (provides shader name → path mapping for unregister).</param>
    public void UnregisterModShaders(string modId, ModManifest manifest)
    {
        if (_registeredShaders.Remove(modId, out var shaders))
        {
            foreach (var shader in shaders)
            {
                // Unregister precompiled bytecode from EffectSystem if it was registered
                if (_effectSystem != null)
                    _effectSystem.UnregisterPrecompiledBytecode(shader.Name);
                Log.Info($"[ModShaderManager] Unregistered shader '{shader.Name}' from mod '{modId}'");
            }
        }
        else if (manifest?.Shaders != null && _effectSystem != null)
        {
            // Manifest provided but shaders weren't tracked — try to unregister by name
            foreach (var shader in manifest.Shaders)
                _effectSystem.UnregisterPrecompiledBytecode(shader.Name);
        }

        if (_shaderBytecodeBundles.Remove(modId, out var bundles))
        {
            Log.Info($"[ModShaderManager] Unregistered {bundles.Count} shader bytecode bundle(s) from mod '{modId}'");
        }
    }

    /// <summary>
    /// Unregisters all shaders belonging to a mod (without EffectSystem cleanup).
    /// Use the overload that takes a manifest when EffectSystem should also be cleaned up.
    /// </summary>
    public void UnregisterModShaders(string modId)
    {
        if (_registeredShaders.Remove(modId, out var shaders))
        {
            foreach (var shader in shaders)
                Log.Info($"[ModShaderManager] Unregistered shader '{shader.Name}' from mod '{modId}'");
        }

        if (_shaderBytecodeBundles.Remove(modId, out var bundles))
        {
            Log.Info($"[ModShaderManager] Unregistered {bundles.Count} shader bytecode bundle(s) from mod '{modId}'");
        }
    }

    /// <summary>
    /// Checks if a mod has registered shaders.
    /// </summary>
    public bool HasModShaders(string modId)
        => _registeredShaders.ContainsKey(modId);

    /// <summary>
    /// Returns all shader names registered by a mod.
    /// </summary>
    public IReadOnlyList<string> GetModShaderNames(string modId)
        => _registeredShaders.TryGetValue(modId, out var shaders)
            ? shaders.ConvertAll(s => s.Name)
            : [];

    /// <summary>
    /// Returns the number of registered shaders for a mod.
    /// </summary>
    public int GetShaderCount(string modId)
        => _registeredShaders.TryGetValue(modId, out var shaders) ? shaders.Count : 0;

    /// <summary>
    /// Tracks a registered shader entry with file validation state.
    /// </summary>
    private sealed class RegisteredShader
    {
        public required string Name { get; init; }
        public required string Path { get; init; }
        public bool Exists { get; set; }
    }
}
