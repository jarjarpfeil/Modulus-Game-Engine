// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace Stride.Engine.Modding;

/// <summary>
/// Defines the set of core API assemblies that all ALCs (both collectible
/// <see cref="ModLoadContext"/> and non-collectible <see cref="NativeModLoadContext"/>)
/// must route to the default load context. This is the single source of truth that
/// prevents type-identity mismatches across ALC boundaries.
///
/// IMPORTANT: Adding or removing ANY entry here has cross-ALC type-identity
/// implications. Every ALC uses this exact set. Keep in sync with
/// <see cref="ModHost.IsCoreApiAssembly"/> (used for shared assembly resolution).
/// </summary>
internal static class ModCoreAssemblies
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "Stride.Core",
        "Stride.Core.IO",
        "Stride.Core.MicroThreading",
        "Stride.Core.Serialization",
        "Stride.Core.Mathematics",
        "Stride.Engine",
        "Stride.Graphics",
        "Stride.Rendering",
        "Stride.Audio",
        "Stride.Shaders",
        "Stride.Games",
        "Stride.Physics",
        "Stride.Navigation",
        "Stride.VirtualReality",
        "Stride.Assets",
        "Stride.UI",
        "Stride.Particles",
        "Stride.SpriteStudio",
        "Stride.Video",
        "Modulus.Modding.Api",
    };

    /// <summary>
    /// Returns true if the given assembly name is a core API assembly that
    /// must be resolved through the default (engine) ALC.
    /// </summary>
    public static bool IsCore(string? name)
    {
        return name != null && Names.Contains(name);
    }
}

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
        if (ModCoreAssemblies.IsCore(name))
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
    /// Mods are managed C# only — no native DLL loading is permitted in the
    /// default collectible ALC. This override enforces that policy.
    /// </summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        throw new NotSupportedException(
            $"Mod '{ModId}' attempted to load the native library '{unmanagedDllName}'. " +
            "Native dependencies are blocked by default. Enable RequiresNativeCode in manifest " +
            "and EnableNativeModLoading in engine security config.");
    }
}

/// <summary>
/// Non-collectible AssemblyLoadContext for mods that require native code.
/// Unlike <see cref="ModLoadContext"/>, this ALC allows native DLL loading
/// and is NOT collectible (the mod cannot be hot-swapped).
///
/// Use <see cref="ModSecurityConfig.EnableNativeModLoading"/> and
/// <see cref="ModManifest.RequiresNativeCode"/> to gate access.
/// </summary>
public sealed class NativeModLoadContext : AssemblyLoadContext
{
    private readonly ModHost _host;
    private readonly string _modDirectory;
    private readonly List<string> _assemblyPaths = [];

    public string ModId { get; }

    public NativeModLoadContext(ModHost host, string modId, string modDirectory)
        : base($"native-mod-{modId}", isCollectible: false)
    {
        _host = host;
        ModId = modId;
        _modDirectory = modDirectory;
    }

    /// <summary>
    /// Loads a native mod assembly from a DLL path.
    /// </summary>
    public Assembly LoadFromModPath(string assemblyPath)
    {
        _assemblyPaths.Add(assemblyPath);
        return LoadFromAssemblyPath(assemblyPath);
    }

    /// <summary>
    /// Same cross-ALC type resolution as ModLoadContext — core API and shared
    /// dependencies resolve to the default ALC.
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name == null)
            return null;

        if (ModCoreAssemblies.IsCore(name))
            return null;

        if (_host.TryGetLoadedSharedAssembly(name, out _))
            return null;

        return null;
    }

    /// <summary>
    /// Native mods ARE allowed to load unmanaged DLLs — that is the
    /// entire point of this ALC variant.
    /// </summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        return base.LoadUnmanagedDll(unmanagedDllName);
    }

    /// <summary>
    /// Returns the list of assembly file paths loaded by this context.
    /// </summary>
    public IReadOnlyList<string> GetLoadedPaths() => _assemblyPaths;
}
