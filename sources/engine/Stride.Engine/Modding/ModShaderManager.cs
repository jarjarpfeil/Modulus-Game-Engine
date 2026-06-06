// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Stride.Core.Diagnostics;

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
/// - The mod developer compiles shaders offline
/// - The .modpkg contains the compiled bytecode in shaders/
/// - The EffectSystem's cache picks up pre-compiled results
///
/// Fallback: If a mod material references a shader that isn't available
/// (e.g., Game B loads a mod built for Game A), the material renders with
/// a default PBR shader — the mesh stays visible.
/// </summary>
public sealed class ModShaderManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModShaderManager");

    // modId -> list of registered shader entries with file validation
    private readonly Dictionary<string, List<RegisteredShader>> _registeredShaders = new(StringComparer.OrdinalIgnoreCase);
    
    // Reference to the EffectSystem for shader registration
    private object? _effectSystem;

    /// <summary>
    /// Sets the EffectSystem instance for shader registration.
    /// Call this during engine initialization before loading mods.
    /// </summary>
    public void SetEffectSystem(object effectSystem)
    {
        _effectSystem = effectSystem;
        Log.Info("[ModShaderManager] EffectSystem connected");
    }

    /// <summary>
    /// Registers all shaders from a mod's manifest.
    /// Validates that shader files exist on disk.
    /// </summary>
    /// <param name="modId">The mod ID.</param>
    /// <param name="manifest">The mod manifest containing shader entries.</param>
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
    /// Validates that registered shader files actually exist on disk.
    /// Call this after the mod directory is known.
    /// </summary>
    /// <param name="modId">The mod ID.</param>
    /// <param name="modDirectory">The mod's root directory.</param>
    /// <returns>List of missing shader file paths (empty = all present).</returns>
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
    /// Unregisters all shaders belonging to a mod.
    /// </summary>
    public void UnregisterModShaders(string modId)
    {
        if (!_registeredShaders.Remove(modId, out var shaders))
            return;

        foreach (var shader in shaders)
            Log.Info($"[ModShaderManager] Unregistered shader '{shader.Name}' from mod '{modId}'");
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
