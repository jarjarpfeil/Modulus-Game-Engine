// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

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

    /// <summary>The mod's root directory (unpacked .modpkg).</summary>
    public string ModDirectory { get; }

    /// <summary>Whether the mod is currently enabled and running.</summary>
    public bool IsEnabled { get; set; }

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
    /// Unloads the mod's ALC.
    /// </summary>
    public void UnloadAssemblies()
    {
        ModAssembly = null;
        if (LoadContext != null)
        {
            LoadContext.Unload();
            LoadContext = null;
        }
    }

    /// <summary>
    /// Extracts a .modpkg zip to a temp directory and creates a ModPackage from it.
    /// </summary>
    public static ModPackage FromModPkg(string modPkgPath, string extractDirectory)
    {
        if (!File.Exists(modPkgPath))
            throw new FileNotFoundException($"Mod package not found: {modPkgPath}");

        Directory.CreateDirectory(extractDirectory);

        using var zip = ZipFile.OpenRead(modPkgPath);
        zip.ExtractToDirectory(extractDirectory, overwriteFiles: true);

        var manifestPath = Path.Combine(extractDirectory, "mod.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($".modpkg missing mod.json: {modPkgPath}");

        using var manifestStream = File.OpenRead(manifestPath);
        var manifest = ModManifest.FromStream(manifestStream);

        return new ModPackage(manifest, extractDirectory);
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
