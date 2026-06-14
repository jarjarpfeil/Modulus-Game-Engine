// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using Stride.Core.Diagnostics;
using Stride.Engine.Modding;

namespace Stride.Modding.Editor;

/// <summary>
/// Loads mod assemblies into <see cref="MetadataLoadContext"/> instances for
/// design-time reflection only. Zero code execution, no module initializers,
/// no constructors run — assemblies are parsed strictly as metadata.
///
/// This replaces the previous <see cref="AssemblyLoadContext"/> approach to
/// eliminate the design-time reflection paradox: mods aren't "loaded" at
/// editor time (no code execution), but their types ARE available for the
/// property grid via <see cref="MetadataLoadContext"/>.
/// </summary>
public sealed class EditorMetadataLoader : IDisposable
{
    private static readonly Logger Log = GlobalLogger.GetLogger("EditorMetadataLoader");

    private readonly Dictionary<string, MetadataContext> _loadedMods = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads mod assemblies for metadata reflection only. No code is executed.
    /// Uses <see cref="MetadataLoadContext"/> to guarantee zero side-effects.
    /// </summary>
    public void LoadModMetadata(ModManifest manifest, string modDirectory)
    {
        if (_loadedMods.ContainsKey(manifest.Id))
            return;

        try
        {
            var assembliesDir = Path.Combine(modDirectory, "assemblies");
            if (!Directory.Exists(assembliesDir))
            {
                Log.Info($"[EditorMetadataLoader] No assemblies directory for mod '{manifest.Id}' — skipping metadata load");
                return;
            }

            var modAssemblyPaths = new List<string>();
            foreach (var dll in Directory.GetFiles(assembliesDir, "*.dll"))
            {
                modAssemblyPaths.Add(Path.GetFullPath(dll));
            }

            if (modAssemblyPaths.Count == 0)
            {
                Log.Info($"[EditorMetadataLoader] No DLLs found for mod '{manifest.Id}' — skipping metadata load");
                return;
            }

            MetadataLoadContext mlc;

            try
            {
                var pathResolver = new PathAssemblyResolver(modAssemblyPaths.ToArray());
                var defaultResolver = MetadataAssemblyResolver.CreateDefault();
                var compositeResolver = MetadataAssemblyResolver.CreateCompositeResolver(pathResolver, defaultResolver);

                var coreAssemblies = new[] { typeof(object).Assembly };
                mlc = new MetadataLoadContext(compositeResolver, coreAssemblies);
            }
            catch (Exception ex)
            {
                Log.Error($"[EditorMetadataLoader] Failed to create MetadataLoadContext for mod '{manifest.Id}': {ex.Message}");
                return;
            }

            var loadedAssemblies = new List<Assembly>();

            foreach (var path in modAssemblyPaths)
            {
                try
                {
                    var assembly = mlc.LoadFromAssemblyPath(path);
                    loadedAssemblies.Add(assembly);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EditorMetadataLoader] Failed to load metadata from {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            if (loadedAssemblies.Count > 0)
            {
                _loadedMods[manifest.Id] = new MetadataContext(mlc, loadedAssemblies);
                Log.Info($"[EditorMetadataLoader] Loaded metadata for mod '{manifest.Id}' ({loadedAssemblies.Count} assemblies)");
            }
            else
            {
                mlc.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[EditorMetadataLoader] Failed to load metadata for mod '{manifest.Id}': {ex.Message}");
        }
    }

    /// <summary>
    /// Unloads metadata <see cref="MetadataLoadContext"/> for a mod.
    /// Called when mod is disabled or uninstalled.
    /// </summary>
    public void UnloadModMetadata(string modId)
    {
        if (!_loadedMods.Remove(modId, out var ctx))
            return;

        try
        {
            ctx.Mlc.Dispose();
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

    private sealed record MetadataContext(MetadataLoadContext Mlc, List<Assembly> Assemblies);
}
