// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Reflection;
using Stride.Core.Serialization;

namespace Stride.Engine.Modding;

/// <summary>
/// Manages registration of mod assembly types with Stride's serialization and
/// reflection systems. Ensures EntityComponent subclasses from mods are
/// serialization-aware and discoverable via AssemblyRegistry.
/// </summary>
public class ModTypeRegistry
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModTypeRegistry");

    private readonly HashSet<Assembly> _registeredAssemblies = [];

    /// <summary>
    /// Registers a mod assembly with Stride's serialization and reflection systems.
    /// </summary>
    public void RegisterModAssembly(Assembly assembly)
    {
        if (_registeredAssemblies.Contains(assembly))
            return;

        try
        {
            // Register with AssemblyRegistry for type resolution
            AssemblyRegistry.Register(assembly, "mods");

            // Register with DataSerializerFactory for serialization
            // Scan the assembly for DataSerializer types and register them
            var assemblySerializers = DataSerializerFactory.GetAssemblySerializers(assembly);
            if (assemblySerializers != null)
            {
                DataSerializerFactory.RegisterSerializationAssembly(assemblySerializers);
            }
            else
            {
                // Register directly (it may get its serializers discovered later)
                DataSerializerFactory.RegisterSerializationAssembly(assembly);
            }

            _registeredAssemblies.Add(assembly);
            Log.Info($"[ModTypeRegistry] Registered mod assembly: {assembly.GetName().Name}");
        }
        catch (Exception ex)
        {
            Log.Error($"[ModTypeRegistry] Failed to register mod assembly '{assembly.GetName().Name}': {ex}");
            throw;
        }
    }

    /// <summary>
    /// Unregisters a mod assembly from Stride's serialization and reflection systems.
    /// </summary>
    public void UnregisterModAssembly(Assembly assembly)
    {
        if (!_registeredAssemblies.Contains(assembly))
            return;

        try
        {
            // Unregister from DataSerializerFactory
            DataSerializerFactory.UnregisterSerializationAssembly(assembly);

            // Unregister from AssemblyRegistry
            AssemblyRegistry.Unregister(assembly);

            _registeredAssemblies.Remove(assembly);
            Log.Info($"[ModTypeRegistry] Unregistered mod assembly: {assembly.GetName().Name}");
        }
        catch (Exception ex)
        {
            Log.Error($"[ModTypeRegistry] Failed to unregister mod assembly '{assembly.GetName().Name}': {ex}");
        }
    }

    /// <summary>
    /// Returns all currently registered mod assemblies.
    /// </summary>
    public IReadOnlyCollection<Assembly> GetRegisteredAssemblies() => _registeredAssemblies.ToList();
}
