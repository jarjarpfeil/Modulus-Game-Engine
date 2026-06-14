// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Modulus.Modding.Api;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Represents a loaded mod package — its manifest, ALC, assemblies, and state.
/// </summary>
public sealed class ModPackage
{
    /// <summary>The parsed mod.json manifest.</summary>
    public ModManifest Manifest { get; }

    /// <summary>The AssemblyLoadContext this mod's assemblies live in.</summary>
    public ModLoadContext? LoadContext { get; private set; }

    /// <summary>
    /// The non-collectible ALC for native mods (when <see cref="IsHotSwappable"/> is false).
    /// </summary>
    public NativeModLoadContext? NativeLoadContext { get; private set; }

    /// <summary>The mod's root directory (unpacked .modpkg).</summary>
    public string ModDirectory { get; }

    /// <summary>Whether the mod is currently enabled and running.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether this mod can be hot-swapped (unloaded and reloaded at runtime).
    /// Native mods are loaded into a non-collectible ALC and cannot be hot-swapped.
    /// </summary>
    public bool IsHotSwappable { get; set; } = true;

    /// <summary>Error state — non-null if the mod errored out.</summary>
    public string? ErrorReason { get; set; }

    /// <summary>Whether the mod was disabled automatically due to an error.</summary>
    public bool IsErrored => ErrorReason != null;

    /// <summary>Mod state for lifecycle tracking.</summary>
    public ModState State { get; set; } = ModState.Loaded;

    /// <summary>The mod's entry-point IMod instance, if loaded.</summary>
    public object? ModInstance { get; set; }

    /// <summary>Loaded assemblies from this mod.</summary>
    public Assembly? ModAssembly { get; internal set; }

    public ModPackage(ModManifest manifest, string modDirectory)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        ModDirectory = modDirectory ?? throw new ArgumentNullException(nameof(modDirectory));
    }

    /// <summary>
    /// Loads mod assemblies into a fresh ALC.
    /// </summary>
    public void LoadAssemblies(ModHost host)
    {
        LoadContext = new ModLoadContext(host, Manifest.Id, ModDirectory);

        var assembliesDir = Path.Combine(ModDirectory, "assemblies");
        if (Directory.Exists(assembliesDir))
        {
            foreach (var dll in Directory.GetFiles(assembliesDir, "*.dll"))
            {
                LoadContext.LoadFromModPath(dll);
            }
        }

        // Also try loading assemblies from the root (single DLL mods)
        foreach (var dll in Directory.GetFiles(ModDirectory, "*.dll"))
        {
            LoadContext.LoadFromModPath(dll);
        }
    }

    /// <summary>
    /// Loads mod assemblies into a non-collectible NativeModLoadContext.
    /// Used for mods with RequiresNativeCode = true. Native DLLs are allowed
    /// but the mod cannot be hot-swapped.
    /// </summary>
    public void LoadNativeAssemblies(ModHost host)
    {
        NativeLoadContext = new NativeModLoadContext(host, Manifest.Id, ModDirectory);

        var assembliesDir = Path.Combine(ModDirectory, "assemblies");
        if (Directory.Exists(assembliesDir))
        {
            foreach (var dll in Directory.GetFiles(assembliesDir, "*.dll"))
            {
                NativeLoadContext.LoadFromModPath(dll);
            }
        }

        foreach (var dll in Directory.GetFiles(ModDirectory, "*.dll"))
        {
            NativeLoadContext.LoadFromModPath(dll);
        }

        IsHotSwappable = false;
    }

    /// <summary>
    /// Unloads the mod's ALC and nullifies all strong references to it.
    /// This is the single critical step for ALC garbage collection — any
    /// remaining strong reference (even this property) will permanently root
    /// the ALC and leak the mod's entire DLL memory.
    ///
    /// For native mods, the ALC is non-collectible, so this only nulls
    /// the reference (the native DLLs remain loaded in the process).
    /// </summary>
    public void Unload()
    {
        ModAssembly = null;
        if (LoadContext != null)
        {
            LoadContext.Unload();
            LoadContext = null;
        }
        if (NativeLoadContext != null)
        {
            // Native ALC is non-collectible — cannot Unload().
            // Null the reference, but the native DLL file handles loaded into the
            // process by this mod (both managed and unmanaged) will remain resident
            // until a full process restart. There is no workaround for this — it is
            // a fundamental OS limitation of loadable native module handles.
            GlobalLogger.GetLogger("ModPackage").Warning(
                $"[ModPackage] Nulled NativeLoadContext reference for mod '{Manifest.Id}', " +
                "but native DLL handles will remain loaded until process exit.");
            NativeLoadContext = null;
        }
    }

    /// <summary>
    /// Legacy alias — call <see cref="Unload"/> instead. Kept for backward
    /// compatibility during transition.
    /// </summary>
    public void UnloadAssemblies() => Unload();

    /// <summary>
    /// Extracts a .modpkg zip to a temp directory and creates a ModPackage from it.
    /// Uses system temp directory to avoid cluttering the mods folder.
    /// </summary>
    public static ModPackage FromModPkg(string modPkgPath, string extractDirectory)
    {
        if (!File.Exists(modPkgPath))
            throw new FileNotFoundException($"Mod package not found: {modPkgPath}");

        // Use system temp directory for extraction to avoid leaving temp files in mods/
        var tempDir = Path.Combine(Path.GetTempPath(), "ModulusEngine", "modpkg-extract", Path.GetFileNameWithoutExtension(modPkgPath));
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
        
        Directory.CreateDirectory(tempDir);

        using var zip = ZipFile.OpenRead(modPkgPath);
        zip.ExtractToDirectory(tempDir, overwriteFiles: true);

        var manifestPath = Path.Combine(tempDir, "mod.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($".modpkg missing mod.json: {modPkgPath}");

        using var manifestStream = File.OpenRead(manifestPath);
        var manifest = ModManifest.FromStream(manifestStream);

        // Return package pointing to temp directory for inspection
        // Caller should copy to final location if keeping
        return new ModPackage(manifest, tempDir);
    }
}

/// <summary>
/// Mod lifecycle states.
/// </summary>
public enum ModState
{
    /// <summary>Mod is loaded and active.</summary>
    Loaded,
    /// <summary>Mod is disabled by the user.</summary>
    Disabled,
    /// <summary>Mod was disabled automatically due to an error.</summary>
    Errored,
}
