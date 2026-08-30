// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Assemblies that must resolve from the host ALC, not the mod's collectible ALC.
    /// Loading duplicates causes EntityComponent type-mismatch failures.
    /// </summary>
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Modulus.Modding.Api.dll",
        "Modulus.Mod.Sdk.dll",
    };

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

        var loadedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        LoadDllsFromDir(Path.Combine(ModDirectory, "assemblies"), LoadContext.LoadFromModPath, loadedNames);
        LoadDllsFromDir(Path.Combine(ModDirectory, "bin", "Debug"), LoadContext.LoadFromModPath, loadedNames);
        LoadDllsFromDir(Path.Combine(ModDirectory, "bin", "Release"), LoadContext.LoadFromModPath, loadedNames);

        // Scan bin/<Config>/<TFM>/ (e.g. bin/Debug/net10.0/)
        var binDir = Path.Combine(ModDirectory, "bin");
        if (Directory.Exists(binDir))
        {
            foreach (var configDir in Directory.GetDirectories(binDir))
            {
                if (Directory.Exists(configDir))
                {
                    foreach (var tfmDir in Directory.GetDirectories(configDir))
                    {
                        LoadDllsFromDir(tfmDir, LoadContext.LoadFromModPath, loadedNames);
                    }
                }
            }
        }

        // Root-level fallback (single-DLL mods)
        LoadDllsFromDir(ModDirectory, LoadContext.LoadFromModPath, loadedNames);
    }

    /// <summary>
    /// Loads mod assemblies into a non-collectible NativeModLoadContext.
    /// Used for mods with RequiresNativeCode = true. Native DLLs are allowed
    /// but the mod cannot be hot-swapped.
    /// </summary>
    public void LoadNativeAssemblies(ModHost host)
    {
        NativeLoadContext = new NativeModLoadContext(host, Manifest.Id, ModDirectory);

        var loadedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        LoadDllsFromDir(Path.Combine(ModDirectory, "assemblies"), NativeLoadContext.LoadFromModPath, loadedNames);
        LoadDllsFromDir(Path.Combine(ModDirectory, "bin", "Debug"), NativeLoadContext.LoadFromModPath, loadedNames);
        LoadDllsFromDir(Path.Combine(ModDirectory, "bin", "Release"), NativeLoadContext.LoadFromModPath, loadedNames);

        // Scan bin/<Config>/<TFM>/ (e.g. bin/Debug/net10.0/)
        var binDir = Path.Combine(ModDirectory, "bin");
        if (Directory.Exists(binDir))
        {
            foreach (var configDir in Directory.GetDirectories(binDir))
            {
                if (Directory.Exists(configDir))
                {
                    foreach (var tfmDir in Directory.GetDirectories(configDir))
                    {
                        LoadDllsFromDir(tfmDir, NativeLoadContext.LoadFromModPath, loadedNames);
                    }
                }
            }
        }

        // Root-level fallback (single-DLL mods)
        LoadDllsFromDir(ModDirectory, NativeLoadContext.LoadFromModPath, loadedNames);

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

    /// <summary>
    /// Scan a directory for DLLs, skip shared/known assemblies, skip duplicates,
    /// and load each via the provided loader delegate.
    /// </summary>
    private static void LoadDllsFromDir(
        string dir,
        Func<string, Assembly> load,
        HashSet<string> loadedNames)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var dll in Directory.GetFiles(dir, "*.dll"))
        {
            var name = Path.GetFileName(dll);
            if (IsSharedAssembly(name))
                continue;
            if (!loadedNames.Add(name))
                continue;
            load(dll);
        }
    }

    /// <summary>
    /// Returns true if this assembly is shared/contract and must resolve from
    /// the host ALC rather than the mod's collectible ALC.
    /// </summary>
    private static bool IsSharedAssembly(string dllName)
    {
        if (dllName.StartsWith("Stride.", StringComparison.OrdinalIgnoreCase))
            return true;
        return SharedAssemblies.Contains(dllName);
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
