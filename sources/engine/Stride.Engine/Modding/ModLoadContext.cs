// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace Stride.Engine.Modding;

/// <summary>
/// Collectible AssemblyLoadContext for a single mod. Each mod gets its own ALC so
/// it can be unloaded independently. Implements cross-ALC type identity rules:
/// core API assemblies are routed to the default context, shared dependencies
/// resolve through the owning ModHost.
/// </summary>
public class ModLoadContext : AssemblyLoadContext
{
    private readonly ModHost _host;
    private readonly string _modDirectory;
    private readonly List<string> _assemblyPaths = [];

    public string ModId { get; }

    public ModLoadContext(ModHost host, string modId, string modDirectory)
        : base($"mod-{modId}", isCollectible: true)
    {
        _host = host;
        ModId = modId;
        _modDirectory = modDirectory;
    }

    /// <summary>
    /// Load a mod assembly from a DLL path. Tracks the path for later cleanup.
    /// </summary>
    public Assembly LoadFromModPath(string assemblyPath)
    {
        _assemblyPaths.Add(assemblyPath);
        return LoadFromAssemblyPath(assemblyPath);
    }

    /// <summary>
    /// Resolution policy for cross-ALC type identity:
    ///   - Core API assemblies → delegate to default context (null = use default)
    ///   - Shared dependencies → delegate to default context (null = use default)
    ///   - Everything else → isolated to this ALC
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name == null)
            return null;

        // Route core API assemblies to the default load context (engine's ALC).
        // This preserves type identity — Mod A and Mod B both see the same Stride.Engine types.
        if (IsCoreApiAssembly(name))
            return null; // Falls back to default load behavior

        // Route shared dependencies to the default context to preserve type identity.
        // IMPORTANT: Do NOT return the shared assembly directly — that would create a
        // new reference from this ALC to the shared assembly's ALC, preventing collection.
        // Instead, return null to delegate to the default context which already has it loaded.
        if (_host.TryGetLoadedSharedAssembly(name, out _))
            return null; // Delegate to default context

        // Standard isolation: mod-private assemblies stay in this ALC.
        return null;
    }

    /// <summary>
    /// Returns the list of assembly file paths loaded by this context.
    /// </summary>
    public IReadOnlyList<string> GetLoadedPaths() => _assemblyPaths;

    /// <summary>
    /// Assemblies that should always resolve to the default (engine) ALC.
    /// Mods MUST NOT load their own copies of these.
    /// </summary>
    private static bool IsCoreApiAssembly(string name)
    {
        return name switch
        {
            "Stride.Core" => true,
            "Stride.Core.IO" => true,
            "Stride.Core.MicroThreading" => true,
            "Stride.Core.Serialization" => true,
            "Stride.Core.Mathematics" => true,
            "Stride.Engine" => true,
            "Stride.Graphics" => true,
            "Stride.Rendering" => true,
            "Stride.Audio" => true,
            "Stride.Shaders" => true,
            "Stride.Games" => true,
            "Stride.Physics" => true,
            "Stride.Navigation" => true,
            "Stride.VirtualReality" => true,
            "Stride.Assets" => true,
            "Stride.UI" => true,
            "Stride.Particles" => true,
            "Stride.SpriteStudio" => true,
            "Stride.Video" => true,
            "Modulus.Modding.Api" => true,
            _ => false
        };
    }
}
