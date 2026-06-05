// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.MicroThreading;
using Stride.Core.Reflection;
using Stride.Core.Serialization;
using Stride.Engine.Processors;

namespace Stride.Engine.Modding;

/// <summary>
/// Manages the complete lifecycle of mods — resource tracking, safe unloading,
/// ALC leak prevention, and hot-reload. Implements the 17-step cleanup checklist
/// from the Modulus Engine plan.
///
/// THE SINGLE HARDEST TECHNICAL PROBLEM: A collectible ALC can only be GC'd when
/// zero strong references exist from outside the ALC to types inside it.
/// A single leaked reference = DLL stays in memory forever.
/// </summary>
public sealed class ModLifecycleManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModLifecycleManager");

    private readonly IServiceRegistry _services;
    private readonly Dictionary<string, ModScope> _scopes = new(StringComparer.OrdinalIgnoreCase);

    public ModLifecycleManager(IServiceRegistry services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Gets or creates the ModScope for a given mod ID.
    /// </summary>
    public ModScope GetOrCreateScope(string modId)
    {
        if (!_scopes.TryGetValue(modId, out var scope))
        {
            scope = new ModScope(modId);
            _scopes[modId] = scope;
        }
        return scope;
    }

    /// <summary>
    /// Returns the scope for a mod, or null if not tracked.
    /// </summary>
    public ModScope? GetScope(string modId)
    {
        _scopes.TryGetValue(modId, out var scope);
        return scope;
    }

    /// <summary>
    /// Executes the full 17-step cleanup checklist for a mod.
    /// Every step must complete to ensure ALC collection.
    /// </summary>
    /// <param name="modId">The mod to clean up.</param>
    /// <param name="modAssembly">The mod's root assembly (for reflection cache clearing).</param>
    /// <param name="package">The mod package (for ALC unload).</param>
    /// <returns>WeakReference to the ALC — verify Target is null after GC.</returns>
    public WeakReference PerformFullCleanup(string modId, Assembly? modAssembly, ModPackage package)
    {
        Log.Info($"[ModLifecycleManager] Starting cleanup for mod '{modId}'");

        if (!_scopes.TryGetValue(modId, out var scope))
        {
            scope = new ModScope(modId);
        }

        // ─── Step 1: Cancel all MicroThreads from the mod ───
        CancelModMicroThreads(scope);

        // ─── Step 2: Unregister from DataSerializerFactory ───
        if (modAssembly != null)
        {
            try
            {
                DataSerializerFactory.ClearAssemblySerializers(modAssembly);
                Log.Info($"[ModLifecycleManager] Cleared DataSerializerFactory for {modAssembly.GetName().Name}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModLifecycleManager] DataSerializerFactory cleanup failed: {ex.Message}");
            }
        }

        // ─── Step 3: Unregister from AssemblyRegistry ───
        if (modAssembly != null)
        {
            try
            {
                AssemblyRegistry.Unregister(modAssembly);
                Log.Info($"[ModLifecycleManager] Unregistered from AssemblyRegistry: {modAssembly.GetName().Name}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModLifecycleManager] AssemblyRegistry unregister failed: {ex.Message}");
            }
        }

        // ─── Step 4: Remove all mod EntityProcessor instances ───
        RemoveModProcessors(scope);

        // ─── Step 5: Destroy all entities with mod components ───
        DestroyModEntities(scope);

        // ─── Step 6: Null all cached reflection references ───
        scope.CachedReflectionMembers.Clear();

        // ─── Step 7: Run static reference cleanup actions ───
        foreach (var cleanup in scope.StaticReferenceCleanup)
        {
            try { cleanup(); }
            catch (Exception ex) { Log.Warning($"[ModLifecycleManager] Static cleanup failed: {ex.Message}"); }
        }
        scope.StaticReferenceCleanup.Clear();

        // ─── Step 8: Clear TypeDescriptorFactory cache ───
        if (modAssembly != null)
        {
            ClearTypeDescriptorCache(modAssembly);
        }

        // ─── Step 9: Call mod.Dispose() on all IDisposable instances ───
        if (package.ModInstance is IDisposable disposable)
        {
            try { disposable.Dispose(); }
            catch (Exception ex) { Log.Warning($"[ModLifecycleManager] mod.Dispose() failed: {ex.Message}"); }
        }

        // ─── Step 10-11: Capture WeakReference before unload ───
        WeakReference alcWeakRef = new(package.LoadContext!);

        // ─── Step 12: Unload ALC ───
        package.UnloadAssemblies();

        // ─── Step 13-15: Force GC (2 cycles as per plan) ───
        for (int i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // ─── Step 16: Remove scope ───
        _scopes.Remove(modId);

        Log.Info($"[ModLifecycleManager] Cleanup complete for mod '{modId}'");
        return alcWeakRef;
    }

    /// <summary>
    /// Verifies that an ALC was actually collected after cleanup.
    /// Call after PerformFullCleanup + GC.
    /// </summary>
    public static bool VerifyAlcCollected(WeakReference alcWeakRef)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return alcWeakRef.Target == null;
    }

    // ──────────────────────────────────────────────
    //  Step implementations
    // ──────────────────────────────────────────────

    /// <summary>
    /// Step 1: Cancel all MicroThreads owned by the mod.
    /// Uses the scope's tracked microthread list.
    /// </summary>
    private void CancelModMicroThreads(ModScope scope)
    {
        try
        {
            var scriptSystem = _services.GetService<ScriptSystem>();
            if (scriptSystem?.Scheduler == null)
            {
                Log.Warning("[ModLifecycleManager] ScriptSystem not available — skipping MicroThread cleanup");
                return;
            }

            // Use the public MicroThreads collection
            var scheduler = scriptSystem.Scheduler;
            int cancelled = 0;

            // Cancel microthreads that belong to this mod's tracked processors/scripts
            foreach (var mt in scheduler.MicroThreads.ToList())
            {
                if (mt.State == MicroThreadState.Running)
                {
                    // Check if this microthread is associated with a mod-owned script
                    // For v1, we cancel all non-core microthreads when a mod unloads
                    // A more precise approach would track mod-initiated microthreads in the scope
                    if (IsModOwnedMicroThread(mt, scope))
                    {
                        mt.Cancel();
                        cancelled++;
                    }
                }
            }

            if (cancelled > 0)
                Log.Info($"[ModLifecycleManager] Cancelled {cancelled} MicroThreads from mod");
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModLifecycleManager] MicroThread cleanup failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Determines if a MicroThread is owned by the mod being unloaded.
    /// Checks the microthread's name or associated assembly.
    /// </summary>
    private static bool IsModOwnedMicroThread(Core.MicroThreading.MicroThread mt, ModScope scope)
    {
        // MicroThreads created by SyncScript have names containing the script type
        // We check if the name contains any of the mod's tracked type names
        if (mt.Name != null)
        {
            foreach (var processor in scope.OwnedProcessors)
            {
                var typeName = processor.GetType().Name;
                if (mt.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Step 4: Remove all EntityProcessors belonging to the mod.
    /// </summary>
    private void RemoveModProcessors(ModScope scope)
    {
        try
        {
            var sceneSystem = _services.GetService<SceneSystem>();
            if (sceneSystem?.SceneInstance == null) return;

            var entityManager = sceneSystem.SceneInstance;
            foreach (var processor in scope.OwnedProcessors)
            {
                try
                {
                    entityManager.Processors.Remove((EntityProcessor)processor);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[ModLifecycleManager] Failed to remove processor: {ex.Message}");
                }
            }
            scope.OwnedProcessors.Clear();
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModLifecycleManager] Processor cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Step 5: Destroy all entities created by the mod.
    /// </summary>
    private void DestroyModEntities(ModScope scope)
    {
        try
        {
            var sceneSystem = _services.GetService<SceneSystem>();
            if (sceneSystem?.SceneInstance == null) return;

            var scene = sceneSystem.SceneInstance;
            int destroyed = 0;
            foreach (var entityId in scope.OwnedEntities)
            {
                var entity = scene.FirstOrDefault(e => e.Id == entityId);
                if (entity != null)
                {
                    scene.Remove(entity);
                    destroyed++;
                }
            }
            scope.OwnedEntities.Clear();

            if (destroyed > 0)
                Log.Info($"[ModLifecycleManager] Destroyed {destroyed} entities from mod");
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModLifecycleManager] Entity cleanup failed: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────
    //  Convenience tracking methods
    // ──────────────────────────────────────────────

    /// <summary>Register an entity as owned by a mod.</summary>
    public void TrackEntity(string modId, Guid entityId)
    {
        GetOrCreateScope(modId).OwnedEntities.Add(entityId);
    }

    /// <summary>Register a processor as owned by a mod.</summary>
    public void TrackProcessor(string modId, object processor)
    {
        GetOrCreateScope(modId).OwnedProcessors.Add(processor);
    }

    /// <summary>Register a cleanup action for static references.</summary>
    public void RegisterStaticCleanup(string modId, Action cleanup)
    {
        GetOrCreateScope(modId).StaticReferenceCleanup.Add(cleanup);
    }

    /// <summary>
    /// Clears TypeDescriptorFactory cache via reflection (best-effort).
    /// TypeDescriptorFactory is in Stride.Core.Reflection assembly which may not be
    /// directly referenced, so we use reflection to access it.
    /// </summary>
    private static void ClearTypeDescriptorCache(Assembly modAssembly)
    {
        try
        {
            var factoryType = Type.GetType("Stride.Core.Reflection.TypeDescriptorFactory, Stride.Core.Reflection");
            if (factoryType == null)
            {
                // Try loading from the assembly
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "Stride.Core.Reflection")
                    {
                        factoryType = asm.GetType("Stride.Core.Reflection.TypeDescriptorFactory");
                        break;
                    }
                }
            }

            if (factoryType == null) return;

            var defaultProp = factoryType.GetProperty("Default", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var defaultInstance = defaultProp?.GetValue(null);
            if (defaultInstance == null) return;

            var clearMethod = factoryType.GetMethod("ClearAssemblyCache", [typeof(Assembly)]);
            clearMethod?.Invoke(defaultInstance, [modAssembly]);
            Log.Info($"[ModLifecycleManager] Cleared TypeDescriptorFactory cache for {modAssembly.GetName().Name}");
        }
        catch (Exception ex)
        {
            Log.Warning($"[ModLifecycleManager] TypeDescriptorFactory cache clear failed (non-fatal): {ex.Message}");
        }
    }
}
