// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Modulus.Modding.Api;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Serialization;

namespace Stride.Engine.Modding;

/// <summary>
/// Central mod lifecycle manager. Handles discovery, validation, loading, unloading,
/// and provides the shared assembly resolution map for cross-ALC type identity.
///
/// Integrates ModContentManager for asset resolution, ModEventBus for inter-mod
/// communication, ModLifecycleManager for ALC cleanup, ModExceptionHandler for
/// crash safety, ModShaderManager for shader extraction, and ModStateStore for
/// state persistence.
/// </summary>
public class ModHost
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModHost");

    private readonly IServiceRegistry _services;
    private readonly Dictionary<string, ModPackage> _loadedMods = [];
    private readonly Dictionary<string, Assembly> _sharedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ModTypeRegistry _typeRegistry;
    private readonly ModSystemRegistry _systemRegistry;
    private readonly ModEventBus _eventBus;
    private readonly ModContentManager _contentManager;
    private readonly ModLifecycleManager _lifecycleManager;
    private readonly ModExceptionHandler _exceptionHandler;
    private readonly ModShaderManager _shaderManager;
    private readonly OrphanComponentHandler _orphanHandler;
    private ModStateStore? _stateStore;

    /// <summary>Path to the mods/ directory. Defaults to "mods" relative to game content.</summary>
    public string ModsDirectory { get; set; } = "mods";

    /// <summary>All currently loaded mod packages, keyed by mod ID.</summary>
    public IReadOnlyDictionary<string, ModPackage> LoadedMods => _loadedMods;

    /// <summary>The event bus for inter-mod communication.</summary>
    public IModEventBus EventBus => _eventBus;

    /// <summary>The content manager for multi-source asset resolution.</summary>
    public ModContentManager ContentManager => _contentManager;

    /// <summary>The lifecycle manager for mod resource tracking and cleanup.</summary>
    public ModLifecycleManager LifecycleManager => _lifecycleManager;

    /// <summary>The exception handler for crash-safe mod execution.</summary>
    public ModExceptionHandler ExceptionHandler => _exceptionHandler;

    /// <summary>The shader manager for mod shader registration.</summary>
    public ModShaderManager ShaderManager => _shaderManager;

    /// <summary>The orphan component handler for save-compatible mod uninstall.</summary>
    public OrphanComponentHandler OrphanHandler => _orphanHandler;

    /// <summary>The state store for mod persistence, or null if not enabled.</summary>
    public ModStateStore? StateStore => _stateStore;

    public ModHost(IServiceRegistry services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _typeRegistry = new ModTypeRegistry();
        _systemRegistry = new ModSystemRegistry(services);
        _eventBus = new ModEventBus();
        _contentManager = new ModContentManager(services);
        _lifecycleManager = new ModLifecycleManager(services);
        _exceptionHandler = new ModExceptionHandler();
        _shaderManager = new ModShaderManager();
        _orphanHandler = new OrphanComponentHandler();
        
        // Connect EffectSystem if available (for shader registration)
        var effectSystem = services.GetService<Rendering.EffectSystem>();
        if (effectSystem != null)
            _shaderManager.SetEffectSystem(effectSystem);
    }

    /// <summary>
    /// Registers ModHost as a service so it's accessible from HttpApiSystem and other game systems.
    /// </summary>
    public void RegisterService()
    {
        _services.AddService(this);
        Log.Info("[ModHost] Registered as engine service");
    }

    /// <summary>
    /// Enables mod state persistence. Call once during engine initialization.
    /// </summary>
    /// <param name="baseDirectory">
    /// Base directory for state storage. Defaults to %APPDATA%/ModulusEngine.
    /// </param>
    public void EnableStatePersistence(string? baseDirectory = null)
    {
        baseDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModulusEngine");
        _stateStore = new ModStateStore(baseDirectory);
        Log.Info($"[ModHost] State persistence enabled: {baseDirectory}");
    }

    // ──────────────────────────────────────────────
    //  Discovery
    // ──────────────────────────────────────────────

    /// <summary>
    /// Discovers all mod packages in the mods directory without loading them.
    /// </summary>
    public List<ModPackage> DiscoverMods()
    {
        return ModDiscovery.Discover(ModsDirectory);
    }

    // ──────────────────────────────────────────────
    //  Validation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Validates a mod manifest. Returns validation errors (empty = valid).
    /// </summary>
    public List<string> ValidateMod(ModManifest manifest)
    {
        return ModValidator.Validate(manifest);
    }

    // ──────────────────────────────────────────────
    //  Loading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Loads a mod from a directory containing mod.json + assemblies/.
    /// Returns the loaded ModPackage, or throws on failure.
    ///
    /// Full lifecycle:
    /// 1. Parse manifest, validate
    /// 2. Load assemblies into collectible ALC
    /// 3. Register types with serializer/AssemblyRegistry
    /// 4. Register systems with ECS
    /// 5. Register mod content with ModContentManager
    /// 6. Register mod shaders with ModShaderManager
    /// 7. Instantiate and call IMod.Initialize() (crash-safe)
    /// 8. Restore saved state if available
    /// </summary>
    public ModPackage LoadMod(string modDirectory)
    {
        var manifestPath = Path.Combine(modDirectory, "mod.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"mod.json not found in: {modDirectory}");

        ModManifest manifest;
        using (var stream = File.OpenRead(manifestPath))
            manifest = ModManifest.FromStream(stream);

        // Validate
        var errors = ModValidator.Validate(manifest);
        
        // Security: Check for native DLLs in mod directory
        var nativeDllErrors = ModValidator.ValidateNoNativeDlls(modDirectory);
        errors.AddRange(nativeDllErrors);
        
        // API Compatibility check
        var compatReport = ModCompatibility.CheckCompatibility(manifest.ApiVersion, manifest.RejectFutureVersions);
        if (!compatReport.IsLoadable)
        {
            errors.Add(compatReport.Message);
            foreach (var issue in compatReport.Issues)
                errors.Add($"  - {issue}");
        }
        else if (compatReport.Result == CompatibilityResult.CompatibleWithWarning)
        {
            Log.Warning($"[ModHost] Mod '{manifest.Id}': {compatReport.Message}");
            foreach (var issue in compatReport.Issues)
                Log.Warning($"[ModHost]   {issue}");
        }
        
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Mod validation failed for '{manifest.Id}':\n  - {string.Join("\n  - ", errors)}");

        // Check for duplicate
        if (_loadedMods.ContainsKey(manifest.Id))
            throw new InvalidOperationException($"Mod '{manifest.Id}' is already loaded. Unload it first.");

        // Create package and load assemblies
        var package = new ModPackage(manifest, modDirectory);
        package.LoadAssemblies(this);

        // Discover the main mod assembly (first non-core DLL loaded)
        if (package.LoadContext != null)
        {
            foreach (var asm in package.LoadContext.Assemblies)
            {
                if (!IsCoreApiAssembly(asm.GetName().Name))
                {
                    package.ModAssembly = asm;
                    break;
                }
            }
        }

        // Register assembly with the shared map
        if (package.ModAssembly != null)
        {
            var name = package.ModAssembly.GetName().Name;
            if (name != null)
                _sharedAssemblies[name] = package.ModAssembly;
        }

        // Register types with serialization system
        if (package.ModAssembly != null)
        {
            _typeRegistry.RegisterModAssembly(package.ModAssembly);
        }

        // Register systems with ECS
        _systemRegistry.RegisterModSystems(package);

        // Register mod content for asset resolution
        _contentManager.RegisterModContent(package);

        // Check for GUID collisions with other mods
        var guidCollisions = _contentManager.CheckGuidCollisions(manifest.Id);
        if (guidCollisions.Count > 0)
        {
            Log.Error($"[ModHost] GUID collision detected for mod '{manifest.Id}':");
            foreach (var collision in guidCollisions)
                Log.Error($"  {collision}");
            throw new InvalidOperationException(
                $"Mod '{manifest.Id}' has GUID collisions with existing mods. Loading aborted.");
        }

        // Register mod shaders
        _shaderManager.RegisterModShaders(manifest.Id, manifest);

        // Load and initialize the entry point (IMod) — crash-safe via ModExceptionHandler
        if (manifest.EntryPoint != null && package.ModAssembly != null)
        {
            // Support "Namespace.Type, Assembly" format — extract just the type name
            var entryPointTypeName = manifest.EntryPoint;
            var commaIndex = entryPointTypeName.IndexOf(',');
            if (commaIndex >= 0)
                entryPointTypeName = entryPointTypeName.Substring(0, commaIndex).Trim();

            var entryType = package.ModAssembly.GetType(entryPointTypeName);
            if (entryType != null)
            {
                try
                {
                    var instance = Activator.CreateInstance(entryType);
                    package.ModInstance = instance;

                    // If it implements IMod, call Initialize + OnEnabled (crash-safe)
                    if (instance is IMod mod)
                    {
                        var logger = GlobalLogger.GetLogger($"Mod.{manifest.Id}");
                        var context = new ModContext(_services, logger, _eventBus, modDirectory, manifest.Id);

                        _exceptionHandler.ExecuteModInitialize(manifest.Id, () =>
                        {
                            mod.Initialize(context);
                            mod.OnEnabled();
                        });

                        // Restore saved state if available
                        if (_stateStore != null && mod is IModSerializable serializable && _stateStore.HasState(manifest.Id))
                        {
                            try
                            {
                                var saved = _stateStore.LoadState(manifest.Id);
                                if (saved != null)
                                {
                                    using var stream = new MemoryStream(saved.Data);
                                    serializable.Load(stream);
                                    Log.Info($"[ModHost] Restored state for mod '{manifest.Id}' (saved by v{saved.Version})");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"[ModHost] Failed to restore state for mod '{manifest.Id}': {ex.Message}");
                            }
                        }
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("initialization failed"))
                {
                    // ModExceptionHandler.ExecuteModInitialize wraps the exception
                    Log.Error($"[ModHost] {ex.Message}");
                    package.State = ModState.Errored;
                    package.ErrorReason = ex.Message;
                }
                catch (Exception ex)
                {
                    Log.Error($"[ModHost] Failed to initialize mod '{manifest.Id}': {ex.Message}");
                    package.State = ModState.Errored;
                    package.ErrorReason = $"Initialize failed: {ex.Message}";
                }
            }
            else
            {
                Log.Warning($"[ModHost] Entry point type '{manifest.EntryPoint}' not found in mod '{manifest.Id}'");
            }
        }

        package.IsEnabled = package.State == ModState.Loaded;
        _loadedMods[manifest.Id] = package;
        Log.Info($"[ModHost] Loaded mod: {manifest.Id} v{manifest.Version}");

        return package;
    }

    /// <summary>
    /// Installs a .modpkg file into the mods directory, then loads it.
    /// </summary>
    public ModPackage InstallMod(string modPkgPath)
    {
        // Extract to mods/{id}/
        var tempPkg = ModPackage.FromModPkg(modPkgPath, Path.Combine(ModsDirectory, ".temp_extract"));
        var targetDir = Path.Combine(ModsDirectory, tempPkg.Manifest.Id);

        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, recursive: true);

        // Copy the .modpkg to mods/ and extract
        var destPkg = Path.Combine(ModsDirectory, Path.GetFileName(modPkgPath));
        if (!File.Exists(destPkg) || !string.Equals(Path.GetFullPath(modPkgPath), Path.GetFullPath(destPkg), StringComparison.OrdinalIgnoreCase))
            File.Copy(modPkgPath, destPkg, overwrite: true);

        // Extract in place for direct directory access
        System.IO.Compression.ZipFile.ExtractToDirectory(destPkg, targetDir, overwriteFiles: true);

        // Clean up temp extraction
        var tempDir = Path.Combine(ModsDirectory, ".temp_extract");
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
        
        // Invalidate discovery cache after install
        ModDiscovery.InvalidateCache(ModsDirectory);

        return LoadMod(targetDir);
    }

    // ──────────────────────────────────────────────
    //  Unloading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Unloads a mod: full 17-step cleanup via ModLifecycleManager.
    /// Saves state if the mod implements IModSerializable.
    /// </summary>
    public void UnloadMod(string modId)
    {
        if (!_loadedMods.TryGetValue(modId, out var package))
            return;

        Log.Info($"[ModHost] Unloading mod: {modId}");

        // Save mod state if available
        if (_stateStore != null && package.ModInstance is IModSerializable serializable)
        {
            try
            {
                using var stream = new MemoryStream();
                serializable.Save(stream);
                _stateStore.SaveState(modId, stream.ToArray(), System.Version.Parse(package.Manifest.Version));
                Log.Info($"[ModHost] Saved state for mod '{modId}'");
            }
            catch (Exception ex)
            {
                Log.Warning($"[ModHost] Failed to save state for mod '{modId}': {ex.Message}");
            }
        }

        // Call IMod.OnDisabled() — crash-safe
        if (package.ModInstance is IMod mod)
        {
            _exceptionHandler.ExecuteModCode(modId, () => mod.OnDisabled(), onDisable: () => { });
        }

        // Unsubscribe all event handlers
        _eventBus.UnsubscribeAll(modId);

        // Unregister mod shaders
        _shaderManager.UnregisterModShaders(modId);

        // Unregister mod content (removes GUID mappings too)
        _contentManager.UnregisterModContent(package);

        // Wire OrphanComponentHandler: Check for re-hydration opportunities
        if (package.ModAssembly != null)
        {
            var rehydrated = _orphanHandler.RehydrateOrphans(modId, package.ModAssembly);
            if (rehydrated > 0)
                Log.Info($"[ModHost] Re-hydrated {rehydrated} orphaned components for mod '{modId}'");
        }

        // Perform full 17-step cleanup via lifecycle manager
        var alcWeakRef = _lifecycleManager.PerformFullCleanup(modId, package.ModAssembly, package);

        // Verify ALC was collected (diagnostic)
        if (!ModLifecycleManager.VerifyAlcCollected(alcWeakRef))
        {
            Log.Warning($"[ModHost] ALC for mod '{modId}' was NOT collected — possible reference leak!");
        }

        _loadedMods.Remove(modId);
        Log.Info($"[ModHost] Unloaded mod: {modId}");
    }

    // ──────────────────────────────────────────────
    //  Enable / Disable
    // ──────────────────────────────────────────────

    /// <summary>
    /// Enables a previously loaded (but disabled/errored) mod.
    /// </summary>
    public void EnableMod(string modId)
    {
        if (!_loadedMods.TryGetValue(modId, out var package))
            throw new InvalidOperationException($"Mod '{modId}' is not loaded.");

        if (package.State != ModState.Disabled && package.State != ModState.Errored)
            return; // Already enabled

        package.State = ModState.Loaded;
        package.IsEnabled = true;
        package.ErrorReason = null;

        if (package.ModAssembly != null)
        {
            _typeRegistry.RegisterModAssembly(package.ModAssembly);
            // Only register systems if they weren't already registered
            // (prevents duplicate processors after disable/enable cycle)
            _systemRegistry.RegisterModSystems(package);
        }

        // Call IMod.OnEnabled() — crash-safe
        if (package.ModInstance is IMod mod)
        {
            _exceptionHandler.ExecuteModCode(modId, () => mod.OnEnabled(), onDisable: () =>
            {
                package.State = ModState.Errored;
                package.ErrorReason = "OnEnabled failed";
            });
        }

        Log.Info($"[ModHost] Enabled mod: {modId}");
    }

    /// <summary>
    /// Disables a mod without unloading it.
    /// </summary>
    public void DisableMod(string modId)
    {
        if (!_loadedMods.TryGetValue(modId, out var package))
            throw new InvalidOperationException($"Mod '{modId}' is not loaded.");

        if (package.State != ModState.Loaded)
            return; // Already disabled

        // Call IMod.OnDisabled() — crash-safe
        if (package.ModInstance is IMod mod)
        {
            _exceptionHandler.ExecuteModCode(modId, () => mod.OnDisabled(), onDisable: () => { });
        }

        package.State = ModState.Disabled;
        package.IsEnabled = false;

        _systemRegistry.UnregisterModSystems(package);

        if (package.ModAssembly != null)
            _typeRegistry.UnregisterModAssembly(package.ModAssembly);

        Log.Info($"[ModHost] Disabled mod: {modId}");
    }

    // ──────────────────────────────────────────────
    //  Load All (discovery + load in dependency order)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Discovers all mods in ModsDirectory, resolves dependency order, and loads them.
    /// Uses ModLoadOrderResolver for deterministic topological sort with cycle detection.
    /// Returns the list of successfully loaded packages.
    /// </summary>
    public List<ModPackage> LoadAllMods()
    {
        var discovered = DiscoverMods();

        // Resolve load order with dependency resolution
        var resolver = new ModLoadOrderResolver();
        var result = resolver.ResolveLoadOrder(discovered);

        // Log warnings (e.g., missing optional deps)
        foreach (var warning in result.Warnings)
            Log.Warning($"[ModHost] {warning}");

        // Log errors (cycles, missing required deps, version conflicts)
        foreach (var error in result.Errors)
            Log.Error($"[ModHost] {error.Message}");

        // Load in resolved order
        var loaded = new List<ModPackage>();
        foreach (var pkg in result.OrderedMods)
        {
            try
            {
                LoadMod(pkg.ModDirectory);
                loaded.Add(pkg);
            }
            catch (Exception ex)
            {
                Log.Error($"[ModHost] Failed to load mod '{pkg.Manifest.Id}': {ex.Message}");
            }
        }

        Log.Info($"[ModHost] Loaded {loaded.Count}/{discovered.Count} mods ({result.Errors.Count} errors)");
        return loaded;
    }

    // ──────────────────────────────────────────────
    //  Shared Assembly Resolution
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called by ModLoadContext to resolve shared dependencies across ALCs.
    /// </summary>
    internal bool TryGetLoadedSharedAssembly(string name, out Assembly? assembly)
    {
        return _sharedAssemblies.TryGetValue(name, out assembly);
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private static bool IsCoreApiAssembly(string? name)
    {
        if (name == null) return false;
        return name.StartsWith("Stride.", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Modulus.Modding.Api", StringComparison.OrdinalIgnoreCase);
    }
}
