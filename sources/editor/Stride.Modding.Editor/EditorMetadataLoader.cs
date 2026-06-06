// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Stride.Core.Diagnostics;
using Stride.Engine.Modding;

namespace Stride.Modding.Editor;

/// <summary>
/// Loads mod assemblies into lightweight, execution-free AssemblyLoadContexts
/// purely to harvest type descriptors, property metadata, and asset compile
/// definitions for the property grid and asset browser.
///
/// IMPORTANT: This loader NEVER executes mod code (no IMod.Initialize, no
/// constructors with side effects). It only provides reflection metadata
/// so mod components appear in the Game Studio property grid.
///
/// Per the design-time reflection paradox (Phase 7.4): mods aren't "loaded"
/// at editor time (no code execution), but their types ARE available for
/// property grid reflection.
/// </summary>
public sealed class EditorMetadataLoader : IDisposable
{
    private static readonly Logger Log = GlobalLogger.GetLogger("EditorMetadataLoader");

    private readonly Dictionary<string, MetadataContext> _loadedMods = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads mod assemblies for metadata reflection only. No code is executed.
    /// </summary>
    /// <param name="manifest">The mod manifest.</param>
    /// <param name="modDirectory">Path to the unpacked mod directory.</param>
    public void LoadModMetadata(ModManifest manifest, string modDirectory)
    {
        if (_loadedMods.ContainsKey(manifest.Id))
            return; // Already loaded

        try
        {
            var assembliesDir = Path.Combine(modDirectory, "assemblies");
            if (!Directory.Exists(assembliesDir))
            {
                Log.Info($"[EditorMetadataLoader] No assemblies directory for mod '{manifest.Id}' — skipping metadata load");
                return;
            }

            var alc = new AssemblyLoadContext($"metadata-{manifest.Id}", isCollectible: true);
            var loadedAssemblies = new List<Assembly>();

            foreach (var dll in Directory.GetFiles(assembliesDir, "*.dll"))
            {
                try
                {
                    var assembly = alc.LoadFromAssemblyPath(Path.GetFullPath(dll));
                    loadedAssemblies.Add(assembly);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EditorMetadataLoader] Failed to load metadata from {Path.GetFileName(dll)}: {ex.Message}");
                }
            }

            if (loadedAssemblies.Count > 0)
            {
                _loadedMods[manifest.Id] = new MetadataContext(alc, loadedAssemblies);
                Log.Info($"[EditorMetadataLoader] Loaded metadata for mod '{manifest.Id}' ({loadedAssemblies.Count} assemblies)");
            }
            else
            {
                alc.Unload();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[EditorMetadataLoader] Failed to load metadata for mod '{manifest.Id}': {ex.Message}");
        }
    }

    /// <summary>
    /// Unloads metadata ALC for a mod. Called when mod is disabled or uninstalled.
    /// </summary>
    public void UnloadModMetadata(string modId)
    {
        if (!_loadedMods.Remove(modId, out var ctx))
            return;

        try
        {
            ctx.Alc.Unload();
            Log.Info($"[EditorMetadataLoader] Unloaded metadata for mod '{modId}'");
        }
        catch (Exception ex)
        {
            Log.Warning($"[EditorMetadataLoader] Error unloading metadata for mod '{modId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all component types from loaded mod metadata.
    /// Used for property grid registration.
    /// </summary>
    public IEnumerable<Type> GetModComponentTypes()
    {
        foreach (var ctx in _loadedMods.Values)
        {
            foreach (var assembly in ctx.Assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }

                foreach (var type in types)
                {
                    if (type != null && typeof(Stride.Engine.EntityComponent).IsAssignableFrom(type) && !type.IsAbstract)
                    {
                        yield return type;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets all types from loaded mod metadata, for general reflection purposes.
    /// </summary>
    public IEnumerable<Type> GetAllModTypes()
    {
        foreach (var ctx in _loadedMods.Values)
        {
            foreach (var assembly in ctx.Assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }

                foreach (var type in types)
                {
                    if (type != null)
                        yield return type;
                }
            }
        }
    }

    /// <summary>Number of mods with loaded metadata.</summary>
    public int LoadedModCount => _loadedMods.Count;

    public void Dispose()
    {
        foreach (var modId in _loadedMods.Keys.ToList())
            UnloadModMetadata(modId);
    }

    private sealed record MetadataContext(AssemblyLoadContext Alc, List<Assembly> Assemblies);
}
