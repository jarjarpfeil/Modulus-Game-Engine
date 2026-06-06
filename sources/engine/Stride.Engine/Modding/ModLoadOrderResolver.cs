// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Resolves deterministic mod load order via topological sort with cycle detection.
///
/// Algorithm:
/// 1. Parse all mod manifests, build dependency graph
/// 2. Detect cycles using DFS with coloring (White→Gray→Black)
/// 3. Topological sort (Kahn's algorithm)
/// 4. Tie-break by loadOrder field (lower = loads first)
/// 5. Report cycles clearly: "Circular dependency: A → B → C → A"
///
/// Edge cases:
/// - Missing dependency → mod not loaded, error reported
/// - Circular dependency → all mods in cycle not loaded, error reported
/// - Conflicting versions → mod not loaded, error reported
/// - Optional dependencies → mod loads even if optional dependency missing, warning logged
/// </summary>
public sealed class ModLoadOrderResolver
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModLoadOrderResolver");

    /// <summary>
    /// Result of resolving mod load order.
    /// </summary>
    public sealed class LoadOrderResult
    {
        /// <summary>Mods in resolved load order (ready to load).</summary>
        public List<ModPackage> OrderedMods { get; init; } = [];

        /// <summary>Mods that could not be loaded due to errors.</summary>
        public List<ModLoadError> Errors { get; init; } = [];

        /// <summary>Warnings for optional dependencies that are missing.</summary>
        public List<string> Warnings { get; init; } = [];

        /// <summary>Whether all mods resolved successfully (no errors).</summary>
        public bool Success => Errors.Count == 0;
    }

    /// <summary>
    /// Describes a mod that failed to load.
    /// </summary>
    public sealed class ModLoadError
    {
        /// <summary>The mod ID that failed.</summary>
        public string ModId { get; init; } = string.Empty;

        /// <summary>Human-readable error message.</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>The type of error.</summary>
        public ModLoadErrorType ErrorType { get; init; }
    }

    public enum ModLoadErrorType
    {
        /// <summary>A required dependency is not installed.</summary>
        MissingDependency,
        /// <summary>Mods form a circular dependency chain.</summary>
        CircularDependency,
        /// <summary>Two mods require incompatible versions of the same dependency.</summary>
        VersionConflict,
    }

    /// <summary>
    /// Resolves the load order for a collection of mod packages.
    /// Returns ordered list of loadable mods + errors for unresolvable mods.
    /// </summary>
    public LoadOrderResult ResolveLoadOrder(IEnumerable<ModPackage> mods)
    {
        var modList = mods.ToList();
        var modMap = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in modList)
        {
            // Deduplicate: first occurrence wins
            if (!modMap.ContainsKey(mod.Manifest.Id))
                modMap[mod.Manifest.Id] = mod;
        }

        var errors = new List<ModLoadError>();
        var warnings = new List<string>();

        // ─── Step 1: Validate dependencies exist and versions are satisfiable ───
        var unresolvableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in modMap.Values)
        {
            foreach (var dep in mod.Manifest.Dependencies)
            {
                if (!modMap.TryGetValue(dep.Id, out var depMod))
                {
                    if (dep.Optional)
                    {
                        warnings.Add(
                            $"Mod '{mod.Manifest.Id}': optional dependency '{dep.Id}' v{dep.MinVersion} not installed — loading anyway.");
                    }
                    else
                    {
                        errors.Add(new ModLoadError
                        {
                            ModId = mod.Manifest.Id,
                            Message = $"Mod '{mod.Manifest.Id}' requires '{dep.Id}' v{dep.MinVersion} which is not installed.",
                            ErrorType = ModLoadErrorType.MissingDependency,
                        });
                        unresolvableIds.Add(mod.Manifest.Id);
                    }
                    continue;
                }

                // Check version compatibility
                if (!IsVersionSatisfied(depMod.Manifest.Version, dep.MinVersion))
                {
                    if (dep.Optional)
                    {
                        warnings.Add(
                            $"Mod '{mod.Manifest.Id}': optional dependency '{dep.Id}' installed v{depMod.Manifest.Version} but requires v{dep.MinVersion} — loading anyway.");
                    }
                    else
                    {
                        errors.Add(new ModLoadError
                        {
                            ModId = mod.Manifest.Id,
                            Message =
                                $"Mod '{mod.Manifest.Id}' requires '{dep.Id}' v{dep.MinVersion} but installed version is v{depMod.Manifest.Version}.",
                            ErrorType = ModLoadErrorType.VersionConflict,
                        });
                        unresolvableIds.Add(mod.Manifest.Id);
                    }
                }
            }
        }

        // ─── Step 1.5: Cross-mod version conflict detection ───
        // If two mods both depend on the same third mod but one requires a higher
        // minimum version than the other, AND the installed version doesn't satisfy
        // both, report a conflict for the demanding mod.
        //
        // Example: Mod A requires libX >= 2.0, Mod B requires libX >= 1.0, libX = 1.5
        // → Mod B is fine, Mod A fails with VersionConflict.
        //
        // More complex: Mod A requires libX >= 2.0, Mod B requires libX >= 1.0, libX = 1.0
        // → Both fail independently (already caught above). No extra logic needed.
        //
        // The plan says: "Conflicting versions → mod not loaded, error:
        // 'Mod A requires X v1.0, Mod B requires X v2.0'"
        // This is covered by the individual version checks above — each mod's deps
        // are validated against the installed version independently.

        // Remove unresolvable mods from the graph
        foreach (var id in unresolvableIds)
            modMap.Remove(id);

        // Also remove any mods that depended on unresolvable mods (cascade)
        bool changed = true;
        while (changed)
        {
            changed = false;
            var toRemove = new List<string>();
            foreach (var mod in modMap.Values)
            {
                foreach (var dep in mod.Manifest.Dependencies.Where(d => !d.Optional))
                {
                    if (unresolvableIds.Contains(dep.Id) && !unresolvableIds.Contains(mod.Manifest.Id))
                    {
                        errors.Add(new ModLoadError
                        {
                            ModId = mod.Manifest.Id,
                            Message =
                                $"Mod '{mod.Manifest.Id}' depends on '{dep.Id}' which failed to load.",
                            ErrorType = ModLoadErrorType.MissingDependency,
                        });
                        toRemove.Add(mod.Manifest.Id);
                        unresolvableIds.Add(mod.Manifest.Id);
                        changed = true;
                    }
                }
            }

            foreach (var id in toRemove)
                modMap.Remove(id);
        }

        // ─── Step 2: Detect cycles via DFS coloring ───
        var cycles = DetectCycles(modMap);
        if (cycles.Count > 0)
        {
            foreach (var cycle in cycles)
            {
                var cycleStr = string.Join(" → ", cycle) + " → " + cycle[0];
                foreach (var modId in cycle)
                {
                    if (modMap.ContainsKey(modId))
                    {
                        errors.Add(new ModLoadError
                        {
                            ModId = modId,
                            Message = $"Circular dependency detected: {cycleStr}",
                            ErrorType = ModLoadErrorType.CircularDependency,
                        });
                        modMap.Remove(modId);
                    }
                }
            }
        }

        // ─── Step 3: Topological sort (Kahn's algorithm) ───
        var ordered = TopologicalSort(modMap);

        Log.Info($"[ModLoadOrderResolver] Resolved load order: {ordered.Count} mods ready, {errors.Count} errors, {warnings.Count} warnings");

        return new LoadOrderResult
        {
            OrderedMods = ordered,
            Errors = errors,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Detects all strongly connected components (cycles) in the dependency graph.
    /// Uses DFS with coloring: White (unvisited) → Gray (in stack) → Black (done).
    /// When we encounter a Gray node during DFS, we've found a cycle.
    /// </summary>
    internal List<List<string>> DetectCycles(Dictionary<string, ModPackage> modMap)
    {
        var cycles = new List<List<string>>();
        var color = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0=white, 1=gray, 2=black
        var parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in modMap.Keys)
            color[id] = 0; // white

        foreach (var id in modMap.Keys)
        {
            if (color[id] == 0)
                DfsVisit(id, modMap, color, parent, cycles);
        }

        return cycles;
    }

    private void DfsVisit(string nodeId, Dictionary<string, ModPackage> modMap,
        Dictionary<string, int> color, Dictionary<string, string?> parent,
        List<List<string>> cycles)
    {
        color[nodeId] = 1; // gray — being processed

        if (!modMap.TryGetValue(nodeId, out var mod))
        {
            color[nodeId] = 2; // black
            return;
        }

        foreach (var dep in mod.Manifest.Dependencies.Where(d => !d.Optional))
        {
            if (!modMap.ContainsKey(dep.Id))
                continue; // external dependency, skip

            if (color[dep.Id] == 0) // white — unvisited
            {
                parent[dep.Id] = nodeId;
                DfsVisit(dep.Id, modMap, color, parent, cycles);
            }
            else if (color[dep.Id] == 1) // gray — back edge → cycle!
            {
                // Reconstruct the cycle path
                var cycle = new List<string>();
                var current = nodeId;
                cycle.Add(dep.Id);
                while (current != null && !string.Equals(current, dep.Id, StringComparison.OrdinalIgnoreCase))
                {
                    cycle.Add(current);
                    if (!parent.TryGetValue(current, out current))
                        break; // Parent not found — stop to prevent infinite loop
                }

                cycle.Reverse();
                cycles.Add(cycle);
            }
        }

        color[nodeId] = 2; // black — done
    }

    /// <summary>
    /// Topological sort using Kahn's algorithm with loadOrder tie-breaking.
    /// Returns mods in dependency order (dependencies before dependents).
    /// </summary>
    internal List<ModPackage> TopologicalSort(Dictionary<string, ModPackage> modMap)
    {
        // Build adjacency list and in-degree map
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in modMap.Keys)
        {
            inDegree[id] = 0;
            dependents[id] = [];
        }

        foreach (var (id, mod) in modMap)
        {
            foreach (var dep in mod.Manifest.Dependencies.Where(d => !d.Optional))
            {
                if (dependents.ContainsKey(dep.Id))
                {
                    dependents[dep.Id].Add(id);
                    inDegree[id]++;
                }
            }
        }

        // Priority queue: (loadOrder, modId) — lower loadOrder first
        // Use a sorted set for deterministic ordering
        var queue = new SortedSet<(int LoadOrder, string Id)>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0)
                queue.Add((modMap[id].Manifest.LoadOrder, id));
        }

        var result = new List<ModPackage>();

        while (queue.Count > 0)
        {
            // Take the item with lowest loadOrder
            var first = queue.Min;
            queue.Remove(first);
            var (_, currentId) = first;

            result.Add(modMap[currentId]);

            foreach (var dependentId in dependents[currentId])
            {
                inDegree[dependentId]--;
                if (inDegree[dependentId] == 0)
                    queue.Add((modMap[dependentId].Manifest.LoadOrder, dependentId));
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if an installed version satisfies a minimum version requirement.
    /// Uses semantic versioning: installed >= required.
    /// </summary>
    internal static bool IsVersionSatisfied(string installedVersion, string requiredMinVersion)
    {
        if (!TryParseVersion(installedVersion, out var installed))
            return true; // Can't parse → assume OK
        if (!TryParseVersion(requiredMinVersion, out var required))
            return true; // Can't parse → assume OK

        return installed >= required;
    }

    /// <summary>
    /// Parses a semver string into a comparable Version. Handles 2-part and 3-part versions.
    /// </summary>
    private static bool TryParseVersion(string versionStr, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(versionStr))
            return false;

        var normalized = versionStr.Trim();
        // Strip pre-release suffix for comparison (e.g., "1.0.0-beta.1" → "1.0.0")
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
            normalized = normalized[..dashIndex];

        // Allow 2-part: "1.0" → "1.0.0"
        if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^\d+\.\d+$"))
            normalized += ".0";

        return Version.TryParse(normalized, out version);
    }
}
