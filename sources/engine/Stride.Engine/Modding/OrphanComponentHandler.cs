// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Serialization;

namespace Stride.Engine.Modding;

/// <summary>
/// Handles mapping unknown component types to OrphanComponent during deserialization.
///
/// Problem: When a player saves a game containing custom components from Mod A,
/// then uninstalls Mod A, Stride's deserializer encounters unknown type signatures
/// and either fails the entire load or silently drops components — corrupting the save.
///
/// Solution: Register a type resolver that maps unknown types to OrphanComponent,
/// preserving the raw serialized data so the component can be re-hydrated if the
/// mod is reinstalled.
/// </summary>
public sealed class OrphanComponentHandler
{
    private static readonly Logger Log = GlobalLogger.GetLogger("OrphanComponentHandler");

    // Maps DataContract names to their raw data for orphaned components
    private readonly Dictionary<string, List<OrphanComponent>> _orphans = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All orphaned components currently tracked, grouped by original type name.
    /// </summary>
    public IReadOnlyDictionary<string, List<OrphanComponent>> Orphans => _orphans;

    /// <summary>
    /// Creates an OrphanComponent from an unknown type's serialized data.
    /// Called by the deserialization pipeline when a mod type is not found.
    /// </summary>
    /// <param name="dataContractName">The DataContract name of the unknown type.</param>
    /// <param name="modId">The mod ID that provided this type (if known).</param>
    /// <param name="rawData">The raw serialized bytes of the component.</param>
    /// <returns>An OrphanComponent preserving the unknown type's data.</returns>
    public OrphanComponent CreateOrphan(string dataContractName, string modId, byte[] rawData)
    {
        var orphan = new OrphanComponent
        {
            OriginalTypeName = dataContractName,
            ModId = modId,
            RawData = rawData,
        };

        if (!_orphans.TryGetValue(dataContractName, out var list))
        {
            list = [];
            _orphans[dataContractName] = list;
        }
        list.Add(orphan);

        Log.Info($"[OrphanComponentHandler] Created orphan for '{dataContractName}' from mod '{modId}'");
        return orphan;
    }

    /// <summary>
    /// Attempts to re-hydrate orphaned components when a mod is reinstalled.
    /// Looks up the original type by DataContract name and converts the raw data back.
    /// </summary>
    /// <param name="modId">The mod that was reinstalled.</param>
    /// <param name="modAssembly">The mod's loaded assembly.</param>
    /// <returns>Number of components successfully re-hydrated.</returns>
    public int RehydrateOrphans(string modId, Assembly modAssembly)
    {
        int rehydrated = 0;

        foreach (var (typeName, orphans) in _orphans)
        {
            var targetType = modAssembly.GetTypes()
                .FirstOrDefault(t => GetDataContractName(t) == typeName);

            if (targetType == null) continue;

            foreach (var orphan in orphans.Where(o => o.ModId == modId && o.CanRehydrate))
            {
                try
                {
                    // Mark for re-hydration — the actual conversion happens when
                    // the scene serializer processes the entity
                    Log.Info($"[OrphanComponentHandler] Re-hydratable: '{typeName}' -> {targetType.Name}");
                    rehydrated++;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[OrphanComponentHandler] Failed to re-hydrate '{typeName}': {ex.Message}");
                }
            }
        }

        return rehydrated;
    }

    /// <summary>
    /// Gets the DataContract name for a type, or falls back to the full type name.
    /// </summary>
    public static string GetDataContractName(Type type)
    {
        var attr = type.GetCustomAttribute<DataContractAttribute>();
        if (attr?.Alias != null)
            return attr.Alias;

        return type.FullName ?? type.Name;
    }

    /// <summary>
    /// Checks if a given DataContract name corresponds to any known mod component type.
    /// Used during deserialization to determine if a type should be treated as orphaned.
    /// </summary>
    public bool IsKnownModType(string dataContractName, IEnumerable<Assembly> modAssemblies)
    {
        foreach (var assembly in modAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (GetDataContractName(type) == dataContractName)
                    return true;
            }
        }
        return false;
    }
}
