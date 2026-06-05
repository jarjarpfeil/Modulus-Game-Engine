// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Manages shader extraction and registration from mod packages.
/// Mods include pre-compiled shader bytecode in shaders/ directory.
/// Fallback: missing shaders render with default PBR shader.
/// </summary>
public sealed class ModShaderManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModShaderManager");
    private readonly Dictionary<string, List<string>> _registeredShaders = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterModShaders(string modId, ModManifest manifest)
    {
        if (manifest.Shaders == null || manifest.Shaders.Count == 0) return;
        var shaderNames = new List<string>();
        foreach (var shader in manifest.Shaders)
        {
            try
            {
                shaderNames.Add(shader.Name);
                Log.Info($"[ModShaderManager] Registered shader '{shader.Name}' from mod '{modId}'");
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModShaderManager] Failed to register shader '{shader.Name}' from mod '{modId}': {ex.Message}");
            }
        }
        if (shaderNames.Count > 0)
            _registeredShaders[modId] = shaderNames;
    }

    public void UnregisterModShaders(string modId)
    {
        if (!_registeredShaders.Remove(modId, out var shaderNames)) return;
        foreach (var name in shaderNames)
            Log.Info($"[ModShaderManager] Unregistered shader '{name}' from mod '{modId}'");
    }

    public bool HasModShaders(string modId) => _registeredShaders.ContainsKey(modId);

    public IReadOnlyList<string> GetModShaderNames(string modId)
        => _registeredShaders.TryGetValue(modId, out var names) ? names : [];
}
