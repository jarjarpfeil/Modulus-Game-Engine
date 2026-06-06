// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Engine.Design;

namespace Stride.Engine.Modding;

/// <summary>
/// Manages registration of mod EntityProcessors with the engine's ECS (EntityManager).
/// Mod systems are discovered via reflection and registered with the scene's EntityManager.
/// </summary>
public class ModSystemRegistry
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModSystemRegistry");

    private readonly IServiceRegistry _services;
    private readonly Dictionary<string, List<EntityProcessor>> _modProcessors = [];

    public ModSystemRegistry(IServiceRegistry services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Scans a mod assembly for EntityProcessor subclasses and registers them
    /// with the active scene's EntityManager.
    /// </summary>
    public void RegisterModSystems(ModPackage package)
    {
        if (package.ModAssembly == null)
            return;

        var sceneSystem = _services.GetService<SceneSystem>();
        if (sceneSystem?.SceneInstance == null)
        {
            Log.Warning($"[ModSystemRegistry] No active scene — cannot register systems for mod '{package.Manifest.Id}'");
            return;
        }

        var entityManager = sceneSystem.SceneInstance;
        var processors = new List<EntityProcessor>();

        try
        {
            // Discover EntityProcessor subclasses in the mod assembly
            // SKIP processors that are auto-created via [DefaultEntityComponentProcessor] attribute
            // Those are registered automatically when the component type is added to an entity
            var autoCreatedProcessors = GetAutoCreatedProcessorTypes(package.ModAssembly);
            
            var processorTypes = package.ModAssembly.GetTypes()
                .Where(t => typeof(EntityProcessor).IsAssignableFrom(t)
                            && !t.IsAbstract
                            && !autoCreatedProcessors.Contains(t)
                            && t.GetConstructor(Type.EmptyTypes) != null);

            foreach (var processorType in processorTypes)
            {
                var processor = (EntityProcessor)Activator.CreateInstance(processorType)!;
                processor.Services = _services;
                entityManager.Processors.Add(processor);
                processors.Add(processor);
                Log.Info($"[ModSystemRegistry] Registered processor '{processorType.Name}' from mod '{package.Manifest.Id}'");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ModSystemRegistry] Failed to register systems for mod '{package.Manifest.Id}': {ex}");
        }

        if (processors.Count > 0)
            _modProcessors[package.Manifest.Id] = processors;
    }

    /// <summary>
    /// Unregisters all EntityProcessors belonging to a mod from the active scene.
    /// </summary>
    public void UnregisterModSystems(ModPackage package)
    {
        if (!_modProcessors.TryGetValue(package.Manifest.Id, out var processors))
            return;

        var sceneSystem = _services.GetService<SceneSystem>();
        if (sceneSystem?.SceneInstance == null)
            return;

        var entityManager = sceneSystem.SceneInstance;

        foreach (var processor in processors)
        {
            try
            {
                entityManager.Processors.Remove(processor);
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModSystemRegistry] Failed to remove processor '{processor.GetType().Name}': {ex.Message}");
            }
        }

        _modProcessors.Remove(package.Manifest.Id);
        Log.Info($"[ModSystemRegistry] Unregistered {processors.Count} processors from mod '{package.Manifest.Id}'");
    }

    /// <summary>
    /// Returns the set of processor types that are auto-created via [DefaultEntityComponentProcessor] attribute.
    /// These should NOT be manually registered — Stride's EntityManager creates them automatically
    /// when a component with the attribute is added to a scene.
    /// </summary>
    private static HashSet<Type> GetAutoCreatedProcessorTypes(Assembly assembly)
    {
        var result = new HashSet<Type>();
        try
        {
            var attrType = typeof(DefaultEntityComponentProcessorAttribute);
            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<DefaultEntityComponentProcessorAttribute>();
                if (attr != null)
                {
                    // DynamicTypeAttributeBase stores the type as TypeName string
                    // Resolve it back to a Type to know which processor is auto-created
                    var processorTypeName = attr.TypeName;
                    if (processorTypeName != null)
                    {
                        var processorType = Type.GetType(processorTypeName);
                        if (processorType != null)
                            result.Add(processorType);
                    }
                }
            }
        }
        catch { }
        return result;
    }
}
