// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Manages the .modpkg format and mod installation lifecycle.
///
/// .modpkg format (ZIP archive):
///   mymod.modpkg/
///   ├── mod.json              (required — mod manifest)
///   ├── assemblies/MyMod.dll  (required for standard/patch mods)
///   ├── assets/...            (optional — compiled game assets)
///   ├── data/config.json      (optional — mod configuration)
///   ├── asset-guids.json      (optional — GUID mappings for asset resolution)
///   └── shaders/              (optional — pre-compiled shader bytecode)
///
/// This class handles:
/// - Creating .modpkg from a prepared mod directory
/// - Installing .modpkg into a game's mods/ directory
/// - Uninstalling mods (removing files)
/// - Listing installed mods
/// - Validating .modpkg format
/// </summary>
public sealed class ModPackageManager
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModPackageManager");

    private readonly string _modsDirectory;

    /// <summary>
    /// Creates a new ModPackageManager for the given mods directory.
    /// </summary>
    /// <param name="modsDirectory">Path to the game's mods/ directory.</param>
    public ModPackageManager(string modsDirectory)
    {
        _modsDirectory = modsDirectory ?? throw new ArgumentNullException(nameof(modsDirectory));
    }

    /// <summary>The mods directory this manager operates on.</summary>
    public string ModsDirectory => _modsDirectory;

    // ──────────────────────────────────────────────
    //  Packaging
    // ──────────────────────────────────────────────

    /// <summary>
    /// Creates a .modpkg archive from a prepared mod directory.
    ///
    /// The source directory must contain at minimum:
    ///   - mod.json
    ///
    /// For standard/patch mods, it should also contain:
    ///   - assemblies/ (with at least one .dll)
    ///
    /// The output .modpkg is a ZIP archive with the mod's contents at the root.
    /// </summary>
    /// <param name="sourceDirectory">Path to the prepared mod directory.</param>
    /// <param name="outputPath">Path where the .modpkg file will be created.</param>
    /// <returns>The created ModPackage.</returns>
    public ModPackage CreatePackage(string sourceDirectory, string outputPath)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");

        // Validate mod.json exists
        var manifestPath = Path.Combine(sourceDirectory, "mod.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("mod.json not found in source directory.", manifestPath);

        // Parse and validate the manifest
        ModManifest manifest;
        using (var stream = File.OpenRead(manifestPath))
            manifest = ModManifest.FromStream(stream);

        var validationErrors = ModValidator.Validate(manifest);
        if (validationErrors.Count > 0)
            throw new InvalidOperationException(
                $"Cannot package mod '{manifest.Id}': validation failed:\n  - {string.Join("\n  - ", validationErrors)}");

        // For standard/patch mods, verify assemblies exist
        if (manifest.Type is "standard" or "patch")
        {
            var assembliesDir = Path.Combine(sourceDirectory, "assemblies");
            if (!Directory.Exists(assembliesDir) || !Directory.GetFiles(assembliesDir, "*.dll").Any())
            {
                // Also check root for single-DLL mods
                if (!Directory.GetFiles(sourceDirectory, "*.dll").Any())
                    throw new InvalidOperationException(
                        $"Cannot package mod '{manifest.Id}': no assemblies found. " +
                        "Standard/patch mods must have assemblies in assemblies/ or at the root.");
            }
        }

        // Create the .modpkg ZIP
        var outputDir = Path.GetDirectoryName(outputPath);
        if (outputDir != null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        ZipFile.CreateFromDirectory(sourceDirectory, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        Log.Info($"[ModPackageManager] Created package: {outputPath} ({new FileInfo(outputPath).Length} bytes)");

        return new ModPackage(manifest, sourceDirectory);
    }

    // ──────────────────────────────────────────────
    //  Installation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Installs a .modpkg file into the mods/ directory.
    ///
    /// Steps:
    /// 1. Validate the .modpkg is a valid ZIP with a mod.json
    /// 2. Check for existing installation (upgrade path)
    /// 3. Copy the .modpkg to mods/
    /// 4. Extract to mods/{id}/ for runtime access
    /// 5. Validate the manifest
    ///
    /// Does NOT load the mod — use ModHost.LoadMod() for that.
    /// </summary>
    /// <param name="modPkgPath">Path to the .modpkg file.</param>
    /// <returns>Path to the extracted mod directory.</returns>
    public string Install(string modPkgPath)
    {
        if (!File.Exists(modPkgPath))
            throw new FileNotFoundException($"Mod package not found: {modPkgPath}");

        // Ensure mods directory exists
        Directory.CreateDirectory(_modsDirectory);

        // 1. Quick-read the manifest from the ZIP to get the mod ID
        ModManifest manifest;
        using (var zip = ZipFile.OpenRead(modPkgPath))
        {
            var modJsonEntry = zip.GetEntry("mod.json")
                ?? throw new InvalidDataException($".modpkg missing mod.json: {modPkgPath}");

            using var entryStream = modJsonEntry.Open();
            manifest = ModManifest.FromStream(entryStream);
        }

        // 2. Validate manifest
        var validationErrors = ModValidator.Validate(manifest);
        if (validationErrors.Count > 0)
            throw new InvalidOperationException(
                $"Cannot install mod '{manifest.Id}': validation failed:\n  - {string.Join("\n  - ", validationErrors)}");

        // 3. Check for existing installation
        var targetDir = Path.Combine(_modsDirectory, manifest.Id);
        if (Directory.Exists(targetDir))
        {
            Log.Info($"[ModPackageManager] Upgrading existing mod '{manifest.Id}'");
            Directory.Delete(targetDir, recursive: true);
        }

        // 4. Copy .modpkg to mods/
        var destPkg = Path.Combine(_modsDirectory, Path.GetFileName(modPkgPath));
        if (!File.Exists(destPkg) ||
            !string.Equals(Path.GetFullPath(modPkgPath), Path.GetFullPath(destPkg), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(modPkgPath, destPkg, overwrite: true);
        }

        // 5. Extract to mods/{id}/
        ZipFile.ExtractToDirectory(destPkg, targetDir, overwriteFiles: true);

        // Store the .modpkg path in a metadata file for clean uninstall later
        var metadataPath = Path.Combine(targetDir, ".modpkg-source");
        File.WriteAllText(metadataPath, Path.GetFileName(modPkgPath));

        Log.Info($"[ModPackageManager] Installed mod '{manifest.Id}' v{manifest.Version} to {targetDir}");
        return targetDir;
    }

    // ──────────────────────────────────────────────
    //  Uninstallation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Uninstalls a mod by its ID. Removes both the extracted directory and the .modpkg file.
    /// Creates a backup before deletion for safety.
    /// The mod must be unloaded via ModHost.UnloadMod() before calling this.
    /// </summary>
    /// <param name="modId">The mod ID to uninstall.</param>
    /// <returns>True if the mod was found and removed.</returns>
    public bool Uninstall(string modId)
    {
        bool removed = false;

        // Remove extracted directory (and the .modpkg file alongside it)
        var targetDir = Path.Combine(_modsDirectory, modId);
        if (Directory.Exists(targetDir))
        {
            // Create backup before deletion
            var backupDir = Path.Combine(_modsDirectory, ".backups", modId, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            try
            {
                Directory.CreateDirectory(backupDir);
                CopyDirectory(targetDir, backupDir);
                Log.Info($"[ModPackageManager] Backed up mod '{modId}' to: {backupDir}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModPackageManager] Failed to create backup for mod '{modId}': {ex.Message}");
            }
            
            // Try to find and remove the associated .modpkg file
            var metadataPath = Path.Combine(targetDir, ".modpkg-source");
            if (File.Exists(metadataPath))
            {
                var pkgFileName = File.ReadAllText(metadataPath).Trim();
                var pkgFilePath = Path.Combine(_modsDirectory, pkgFileName);
                if (File.Exists(pkgFilePath))
                {
                    File.Delete(pkgFilePath);
                    Log.Info($"[ModPackageManager] Removed package file: {pkgFilePath}");
                }
            }

            Directory.Delete(targetDir, recursive: true);
            removed = true;
            Log.Info($"[ModPackageManager] Removed mod directory: {targetDir}");
        }

        if (!removed)
            Log.Warning($"[ModPackageManager] Mod '{modId}' not found in {_modsDirectory}");

        return removed;
    }
    
    /// <summary>
    /// Copies a directory recursively.
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    // ──────────────────────────────────────────────
    //  Listing
    // ──────────────────────────────────────────────

    /// <summary>
    /// Lists all installed mod packages (from both .modpkg files and extracted directories).
    /// Returns manifest info without loading assemblies.
    /// </summary>
    public List<ModManifest> ListInstalled()
    {
        var manifests = new List<ModManifest>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_modsDirectory))
            return manifests;

        // Scan extracted directories
        foreach (var dir in Directory.GetDirectories(_modsDirectory))
        {
            if (Path.GetFileName(dir).StartsWith('.'))
                continue; // Skip temp dirs

            var manifestPath = Path.Combine(dir, "mod.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                using var stream = File.OpenRead(manifestPath);
                var manifest = ModManifest.FromStream(stream);
                if (seenIds.Add(manifest.Id))
                    manifests.Add(manifest);
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModPackageManager] Failed to read manifest from {dir}: {ex.Message}");
            }
        }

        // Scan .modpkg files (for mods not already found as directories)
        foreach (var pkgFile in Directory.GetFiles(_modsDirectory, "*.modpkg"))
        {
            try
            {
                using var zip = ZipFile.OpenRead(pkgFile);
                var entry = zip.GetEntry("mod.json");
                if (entry == null) continue;

                using var stream = entry.Open();
                var manifest = ModManifest.FromStream(stream);
                if (seenIds.Add(manifest.Id))
                    manifests.Add(manifest);
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModPackageManager] Failed to read manifest from {pkgFile}: {ex.Message}");
            }
        }

        return manifests;
    }

    // ──────────────────────────────────────────────
    //  Validation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Validates a .modpkg file without installing it.
    /// Returns validation errors (empty = valid).
    /// </summary>
    public static List<string> ValidatePackage(string modPkgPath)
    {
        var errors = new List<string>();

        if (!File.Exists(modPkgPath))
        {
            errors.Add($"File not found: {modPkgPath}");
            return errors;
        }

        try
        {
            using var zip = ZipFile.OpenRead(modPkgPath);

            // Check for mod.json
            var modJsonEntry = zip.GetEntry("mod.json");
            if (modJsonEntry == null)
            {
                errors.Add("Missing mod.json in archive.");
                return errors;
            }

            // Parse manifest
            ModManifest manifest;
            using (var stream = modJsonEntry.Open())
                manifest = ModManifest.FromStream(stream);

            // Validate manifest fields
            errors.AddRange(ModValidator.Validate(manifest));

            // For standard/patch mods, check for assemblies
            if (manifest.Type is "standard" or "patch")
            {
                bool hasAssemblies = zip.Entries.Any(e =>
                    e.FullName.StartsWith("assemblies/", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

                bool hasRootDll = zip.Entries.Any(e =>
                    !e.FullName.Contains('/') &&
                    e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

                if (!hasAssemblies && !hasRootDll)
                    errors.Add("Standard/patch mod has no assemblies. Expected assemblies/*.dll or *.dll at root.");
            }
        }
        catch (InvalidDataException)
        {
            errors.Add("File is not a valid ZIP archive.");
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to read package: {ex.Message}");
        }

        return errors;
    }

    /// <summary>
    /// Checks whether a specific mod ID is installed (has an extracted directory in mods/).
    /// </summary>
    public bool IsInstalled(string modId)
    {
        var targetDir = Path.Combine(_modsDirectory, modId);
        return Directory.Exists(targetDir) && File.Exists(Path.Combine(targetDir, "mod.json"));
    }

    /// <summary>
    /// Gets the directory path for an installed mod.
    /// </summary>
    public string GetModDirectory(string modId)
    {
        return Path.Combine(_modsDirectory, modId);
    }
}
