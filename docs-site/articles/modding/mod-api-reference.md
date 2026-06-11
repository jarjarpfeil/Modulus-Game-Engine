# Modulus Engine — Mod API Reference

> **Namespace:** `Modulus.Modding.Api` (stable ABI)  
> **Engine namespace:** `Stride.Engine.Modding`  
> **Version:** 4.4.0.2  
> **License:** MIT

This document is the complete reference for the Modulus Engine modding API. All interfaces in the `Modulus.Modding.Api` namespace are part of the stable ABI — signatures will not change within a major version.

---

## Table of Contents

- [Interfaces](#interfaces)
  - [IMod](#imod)
  - [IModContext](#imodcontext)
  - [IModComponent](#imodcomponent)
  - [IModSystem](#imodsystem)
  - [IModEventBus](#imodeventbus)
  - [IModSerializable](#imodserializable)
- [Classes](#classes)
  - [ModManifest](#modmanifest)
  - [ModDependency](#moddependency)
  - [ModComponentDeclaration](#modcomponentdeclaration)
  - [ModSystemDeclaration](#modsystemdeclaration)
  - [ModShaderDeclaration](#modshaderdeclaration)
  - [ModCompatibility](#modcompatibility)
  - [ModLoadOrderResolver](#modloadorderresolver)
- [Enums](#enums)
  - [ModState](#modstate)
  - [ModSceneLoadBehavior](#modsceneloadbehavior)
  - [CompatibilityResult](#compatibilityresult)
- [Complete Example](#complete-example)

---

## Interfaces

### IMod

**Namespace:** `Modulus.Modding.Api`  
**File:** `Modulus.Modding.Api/IMod.cs`

Entry point interface for mods. Every mod DLL that declares an `entryPoint` in `mod.json` must implement this interface. The engine calls these methods during the mod lifecycle.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | Unique mod identifier (must match `mod.json` id). |
| `Name` | `string` | Human-readable mod name. |
| `Version` | `System.Version` | Mod version. |
| `MinApiVersion` | `System.Version` | Minimum API version this mod requires. |

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `Initialize` | `void Initialize(IModContext context)` | Called once when the mod is first loaded. Use this to register components, subscribe to events, and set up initial state. |
| `OnEnabled` | `void OnEnabled()` | Called when the mod is enabled (or re-enabled after disable). |
| `OnDisabled` | `void OnDisabled()` | Called when the mod is disabled (by user or error). |

#### Example

```csharp
using Modulus.Modding.Api;
using System;

public class MyMod : IMod
{
    public string Id => "com.example.my-mod";
    public string Name => "My Awesome Mod";
    public Version Version => new(1, 2, 0);
    public Version MinApiVersion => new(1, 0);

    private IModContext _context = null!;
    private IModSystem? _system;

    public void Initialize(IModContext context)
    {
        _context = context;
        _context.Logger.Info("MyMod initializing...");

        // Subscribe to engine events
        _context.EventBus.Subscribe<GameStartedEvent>(OnGameStarted);

        // Register a custom system
        _system = new MyGameplaySystem();
    }

    public void OnEnabled()
    {
        _context.Logger.Info("MyMod enabled.");
    }

    public void OnDisabled()
    {
        _context.Logger.Info("MyMod disabled.");
    }

    private void OnGameStarted(GameStartedEvent evt)
    {
        _context.Logger.Info($"Game started at {evt.Timestamp}");
    }
}
```

---

### IModContext

**Namespace:** `Modulus.Modding.Api`  
**File:** `Modulus.Modding.Api/IModContext.cs`

Context provided to mods during initialization. Gives access to engine services without exposing internal Stride types directly.

For engine types not directly exposed here (ECS, ContentManager, Scene), use `Services` to resolve them:

```csharp
var sceneSystem = context.Services.GetService<SceneSystem>();
var contentManager = context.Services.GetService<IContentManager>();
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Services` | `IServiceRegistry` | Access to the engine's service registry. Use this to resolve engine subsystems. |
| `Logger` | `ILogger` | Logger scoped to the mod. |
| `EventBus` | `IModEventBus` | Inter-mod event bus. |
| `ModDirectory` | `string` | Path to the mod's own directory on disk. |
| `ModId` | `string` | The mod's unique identifier (matches `mod.json` id). |

#### Example

```csharp
public void Initialize(IModContext context)
{
    // Log a message
    context.Logger.Info($"Mod '{context.ModId}' loading from {context.ModDirectory}");

    // Access the service registry
    var sceneSystem = context.Services.GetService<SceneSystem>();
    var contentManager = context.Services.GetService<IContentManager>();

    // Use the event bus
    context.EventBus.Subscribe<ModLoadedEvent>(evt =>
    {
        context.Logger.Info($"Another mod loaded: {evt.ModId}");
    });
}
```

---

### IModComponent

**Namespace:** `Modulus.Modding.Api`  
**File:** `Modulus.Modding.Api/IModComponent.cs`

Standalone interface for mod components. Mods implement this to define custom ECS components without referencing Stride types directly.

The engine internally wraps `IModComponent` instances in an adapter class (`EntityComponent`) so they can participate in the normal Stride ECS pipeline. Mods never see the adapter — they interact only with this interface.

This abstraction enables cross-game compatibility: the same `IModComponent` implementation works in any game built on Modulus, regardless of which Stride version or game-specific components are present.

> **Note:** All methods have default empty implementations, so you only need to override the ones you need.

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `OnAttach` | `void OnAttach()` | Called when this component is first attached to an entity. Use this for initialization that depends on the entity context. |
| `OnDetach` | `void OnDetach()` | Called when this component is removed from an entity or the entity is destroyed. Clean up any resources or subscriptions here. |
| `Update` | `void Update(float deltaTime)` | Called each frame while the component is active. `deltaTime` is seconds elapsed since last frame. |

#### Example

```csharp
using Modulus.Modding.Api;

public class HealthComponent : IModComponent
{
    public float MaxHealth { get; set; } = 100f;
    public float CurrentHealth { get; private set; }
    public bool IsAlive => CurrentHealth > 0;

    private IModEventBus? _eventBus;

    public HealthComponent(IModEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void OnAttach()
    {
        CurrentHealth = MaxHealth;
    }

    public void OnDetach()
    {
        // Clean up if needed
        _eventBus = null;
    }

    public void Update(float deltaTime)
    {
        // Regenerate health over time
        if (IsAlive && CurrentHealth < MaxHealth)
        {
            CurrentHealth = Math.Min(MaxHealth, CurrentHealth + 5f * deltaTime);
        }
    }

    public void TakeDamage(float amount)
    {
        CurrentHealth = Math.Max(0, CurrentHealth - amount);
        if (!IsAlive)
        {
            _eventBus?.Publish(new EntityDiedEvent { Health = this });
        }
    }
}
```

---

### IModSystem

**Namespace:** `Modulus.Modding.Api`  
**File:** `Modulus.Modding.Api/IModSystem.cs`

Standalone interface for mod systems. Mods implement this to define custom ECS systems (processors) without referencing Stride types directly.

The engine internally wraps `IModSystem` instances in an adapter class (`EntityProcessor`) so they participate in the normal Stride ECS update loop. Mods never see the adapter.

Systems run every frame in priority order (lower priority number = runs first).

#### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Priority` | `int` | `100` | Execution priority. Lower values run first. Use this to control ordering between mod systems. |

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `Update` | `void Update(float deltaTime)` | Called each frame. Contains the mod's gameplay logic. `deltaTime` is seconds elapsed since last frame. |
| `OnRegistered` | `void OnRegistered()` | Called once when the system is registered with the engine. Use this for one-time setup (subscribing to events, etc.). |
| `OnUnregistered` | `void OnUnregistered()` | Called once when the system is unregistered (mod unload/disable). Clean up any resources here. |

#### Example

```csharp
using Modulus.Modding.Api;

public class EnemyAISystem : IModSystem
{
    // Run after physics (priority 50) but before rendering (priority 200)
    public int Priority => 100;

    private IModEventBus? _eventBus;
    private List<EnemyComponent> _enemies = new();

    public void OnRegistered()
    {
        // Subscribe to events when the system starts
        // _eventBus.Subscribe<EnemySpawnedEvent>(OnEnemySpawned);
    }

    public void OnUnregistered()
    {
        _enemies.Clear();
    }

    public void Update(float deltaTime)
    {
        foreach (var enemy in _enemies)
        {
            // Simple AI: move toward player
            UpdateEnemyAI(enemy, deltaTime);
        }
    }

    private void UpdateEnemyAI(EnemyComponent enemy, float deltaTime)
    {
        // AI logic here
    }
}
```

---

### IModEventBus

**Namespace:** `Modulus.Modding.Api`  
**File:** `Modulus.Modding.Api/IModEventBus.cs`

Inter-mod communication bus. Allows mods to publish and subscribe to typed events. Event subscriptions are scoped to the mod's lifecycle — unsubscribed automatically on mod unload.

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `Subscribe<T>` | `void Subscribe<T>(Action<T> handler)` | Subscribes to events of type `T`. The handler will be called when `Publish<T>` is invoked. |
| `Unsubscribe<T>` | `void Unsubscribe<T>(Action<T> handler)` | Unsubscribes a previously registered handler for events of type `T`. |
| `Publish<T>` | `void Publish<T>(T evt)` | Publishes an event to all subscribed handlers of type `T`. |
| `UnsubscribeAll` | `void UnsubscribeAll(string modId)` | Removes all event subscriptions for a specific mod. Called during mod unload. |

#### Example

```csharp
// Define event types
public record PlayerDamagedEvent(string PlayerId, float Damage, string Source);
public record PlayerHealedEvent(string PlayerId, float Amount);
public record ModMessageEvent(string SenderModId, string Message);

// Publishing mod
public class CombatMod : IMod
{
    private IModContext _context = null!;

    public void Initialize(IModContext context)
    {
        _context = context;
    }

    public void OnPlayerTakesDamage(string playerId, float damage)
    {
        // Publish to all mods that care about damage
        _context.EventBus.Publish(new PlayerDamagedEvent(playerId, damage, "enemy"));
    }

    // ... other IMod members
}

// Subscribing mod
public class DamageNumbersMod : IMod
{
    private IModContext _context = null!;

    public void Initialize(IModContext context)
    {
        _context = context;

        // Subscribe to damage events from any mod
        _context.EventBus.Subscribe<PlayerDamagedEvent>(OnPlayerDamaged);
        _context.EventBus.Subscribe<PlayerHealedEvent>(OnPlayerHealed);
    }

    private void OnPlayerDamaged(PlayerDamagedEvent evt)
    {
        // Show floating damage number
        _context.Logger.Info($"-{evt.Damage} HP for {evt.PlayerId}");
    }

    private void OnPlayerHealed(PlayerHealedEvent evt)
    {
        _context.Logger.Info($"+{evt.Amount} HP for {evt.PlayerId}");
    }

    public void OnDisabled()
    {
        // Manually unsubscribe if needed (auto-cleaned on unload)
        _context.EventBus.Unsubscribe<PlayerDamagedEvent>(OnPlayerDamaged);
    }

    // ... other IMod members
}
```

---

### IModSerializable

**Namespace:** `Modulus.Modding.Api`  
**File:** `Modulus.Modding.Api/IModSerializable.cs`

Interface for mod state persistence. Mods implement this to define save/load behavior. State is stored per-mod in a standard location.

**Storage location:** `%APPDATA%/ModulusEngine/mod-states/{modId}/state.dat`

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `Save` | `void Save(Stream stream)` | Save mod state to the given stream. The stream is writable and positioned at the start. |
| `Load` | `void Load(Stream stream)` | Load mod state from the given stream. The stream is readable and positioned at the start. |

#### Example

```csharp
using Modulus.Modding.Api;
using System.IO;
using System.Text.Json;

public class SettingsMod : IMod, IModSerializable
{
    public string Id => "com.example.settings";
    public string Name => "Settings Manager";
    public Version Version => new(1, 0, 0);
    public Version MinApiVersion => new(1, 0);

    private IModContext _context = null!;
    private ModSettings _settings = new();

    public void Initialize(IModContext context)
    {
        _context = context;
    }

    public void OnEnabled() { }
    public void OnDisabled() { }

    // Serialize settings to a stream
    public void Save(Stream stream)
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(json);
    }

    // Deserialize settings from a stream
    public void Load(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        _settings = JsonSerializer.Deserialize<ModSettings>(json) ?? new ModSettings();
    }
}

public class ModSettings
{
    public float MusicVolume { get; set; } = 0.8f;
    public float SfxVolume { get; set; } = 1.0f;
    public bool ShowDamageNumbers { get; set; } = true;
    public string Language { get; set; } = "en";
}
```

---

## Classes

### ModManifest

**Namespace:** `Stride.Engine.Modding`  
**File:** `Stride.Engine/Modding/ModManifest.cs`

Parsed representation of a `mod.json` manifest file. Every mod must include a `mod.json` at its root.

#### Fields

| Field | JSON Key | Type | Default | Description |
|-------|----------|------|---------|-------------|
| `Id` | `id` | `string` | `""` | Unique mod identifier (kebab-case, e.g. `"com.example.my-mod"`). |
| `Name` | `name` | `string` | `""` | Human-readable mod name. |
| `Version` | `version` | `string` | `"1.0.0"` | Mod version (semver). |
| `ApiVersion` | `apiVersion` | `string` | `"1.0"` | Minimum API version required. |
| `Author` | `author` | `string?` | `null` | Mod author. |
| `Description` | `description` | `string?` | `null` | Mod description. |
| `EntryPoint` | `entryPoint` | `string?` | `null` | Fully-qualified entry point type (e.g. `"MyMod.Main, MyMod"`). |
| `Dependencies` | `dependencies` | `List<ModDependency>` | `[]` | Mod dependencies. |
| `RejectFutureVersions` | `rejectFutureVersions` | `bool` | `false` | If true, reject on major API version mismatch instead of warning. |
| `Components` | `components` | `List<ModComponentDeclaration>` | `[]` | Custom components declared by this mod. |
| `Systems` | `systems` | `List<ModSystemDeclaration>` | `[]` | Custom systems declared by this mod. |
| `Assets` | `assets` | `List<string>` | `[]` | Asset paths included in the mod. |
| `Shaders` | `shaders` | `List<ModShaderDeclaration>?` | `null` | Custom shaders included in the mod. |
| `Scenes` | `scenes` | `List<ModSceneDeclaration>` | `[]` | Scene assets shipped by this mod, with optional load behavior. |
| `LoadOrder` | `loadOrder` | `int` | `100` | Load order priority (lower = loads first). |
| `Tags` | `tags` | `List<string>` | `[]` | Mod tags for categorization. |
| `Type` | `type` | `string` | `"standard"` | Mod type: `"standard"`, `"patch"`, or `"data"`. |

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `FromStream` | `static ModManifest FromStream(Stream stream)` | Parses a `mod.json` manifest from a stream. |
| `FromJson` | `static ModManifest FromJson(string json)` | Parses a `mod.json` manifest from a JSON string. |

Both methods use `System.Text.Json` with:
- `PropertyNameCaseInsensitive = true`
- `AllowTrailingCommas = true`
- `ReadCommentHandling = JsonCommentHandling.Skip`

#### Example `mod.json`

```json
{
    "id": "com.example.my-mod",
    "name": "My Awesome Mod",
    "version": "1.2.0",
    "apiVersion": "1.0",
    "author": "Example Author",
    "description": "A cool mod that adds new gameplay features.",
    "entryPoint": "MyMod.Main, MyMod",
    "type": "standard",
    "loadOrder": 50,
    "tags": ["gameplay", "combat"],
    "dependencies": [
        { "id": "com.example.core-lib", "minVersion": "2.0.0" },
        { "id": "com.example.optional-helper", "minVersion": "1.0.0", "optional": true }
    ],
    "components": [
        { "type": "MyMod.HealthComponent, MyMod", "processor": "MyMod.HealthProcessor, MyMod" }
    ],
    "systems": [
        { "type": "MyMod.EnemyAISystem, MyMod", "priority": 100 }
    ],
    "shaders": [
        { "name": "CustomOutline", "path": "shaders/Outline.sdsl" }
    ],
    "scenes": [
        { "path": "assets/MainMenu", "name": "Custom Main Menu", "behavior": "replace" },
        { "path": "assets/Background", "behavior": "background" }
    ],
    "assets": [
        "textures/characters.png",
        "audio/music.ogg"
    ]
}
```

#### Parsing in Code

```csharp
// From a file stream
using var stream = File.OpenRead("mod.json");
var manifest = ModManifest.FromStream(stream);

// From a JSON string
var json = File.ReadAllText("mod.json");
var manifest = ModManifest.FromJson(json);

Console.WriteLine($"Loaded mod: {manifest.Name} v{manifest.Version}");
Console.WriteLine($"Dependencies: {manifest.Dependencies.Count}");
```

---

### ModDependency

**Namespace:** `Stride.Engine.Modding`  
**File:** `Stride.Engine/Modding/ModManifest.cs`

A dependency declared in `mod.json`.

#### Fields

| Field | JSON Key | Type | Default | Description |
|-------|----------|------|---------|-------------|
| `Id` | `id` | `string` | `""` | The dependency's mod ID. |
| `MinVersion` | `minVersion` | `string` | `"1.0.0"` | Minimum version of the dependency required. |
| `Optional` | `optional` | `bool` | `false` | If true, the mod loads even if this dependency is missing. |

#### Example

```json
{
    "dependencies": [
        { "id": "com.example.core-lib", "minVersion": "2.0.0" },
        { "id": "com.example.optional-ui", "minVersion": "1.0.0", "optional": true }
    ]
}
```

---

### ModComponentDeclaration

**Namespace:** `Stride.Engine.Modding`  
**File:** `Stride.Engine/Modding/ModManifest.cs`

A component declared in `mod.json`.

#### Fields

| Field | JSON Key | Type | Default | Description |
|-------|----------|------|---------|-------------|
| `Type` | `type` | `string` | `""` | Fully-qualified type name of the `IModComponent` implementation. |
| `Processor` | `processor` | `string?` | `null` | Fully-qualified type name of the associated processor (optional). |

#### Example

```json
{
    "components": [
        { "type": "MyMod.HealthComponent, MyMod" },
        { "type": "MyMod.InventoryComponent, MyMod", "processor": "MyMod.InventoryProcessor, MyMod" }
    ]
}
```

---

### ModSystemDeclaration

**Namespace:** `Stride.Engine.Modding`  
**File:** `Stride.Engine/Modding/ModManifest.cs`

A system declared in `mod.json`.

#### Fields

| Field | JSON Key | Type | Default | Description |
|-------|----------|------|---------|-------------|
| `Type` | `type` | `string` | `""` | Fully-qualified type name of the `IModSystem` implementation. |
| `Priority` | `priority` | `int` | `0` | Execution priority (lower = runs first). |

#### Example

```json
{
    "systems": [
        { "type": "MyMod.EnemyAISystem, MyMod", "priority": 100 },
        { "type": "MyMod.PhysicsSystem, MyMod", "priority": 50 }
    ]
}
```

---

### ModShaderDeclaration

**Namespace:** `Stride.Engine.Modding`  
**File:** `Stride.Engine/Modding/ModManifest.cs`

A shader declared in `mod.json`.

#### Fields

| Field | JSON Key | Type | Default | Description |
|-------|----------|------|---------|-------------|
| `Name` | `name` | `string` | `""` | The shader's logical name (referenced in materials). |
| `Path` | `path` | `string` | `""` | Path to the shader file within the mod package (e.g. `"shaders/Outline.sdsl"`). |

#### Example

```json
{
    "shaders": [
        { "name": "CustomOutline", "path": "shaders/Outline.sdsl" },
        { "name": "WaterRipple", "path": "shaders/Water.sdsl" }
    ]
}
```

---

### ModCompatibility

**Namespace:** `Stride.Engine.Modding`  
**File:** `Stride.Engine/Modding/ModCompatibility.cs`

Static class that checks API version compatibility between mods and the engine. Implements the "always try, unless explicitly unsafe" rule.

#### Static Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EngineApiVersion` | `Version` | `new(1, 0)` | The engine's current API version. Set once during engine initialization. |
| `AllowOutdatedMods` | `bool` | `false` | When true, major version mismatches produce a warning instead of a rejection. Can be set via CLI flag `--allow-outdated-mods`. |

#### Static Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `CheckCompatibility` | `CompatibilityReport CheckCompatibility(Version modApiVersion, bool rejectFutureVersions = false)` | Checks whether a mod is compatible with the current engine API version. |
| `CheckCompatibility` | `CompatibilityReport CheckCompatibility(string? modApiVersionString, bool rejectFutureVersions = false)` | Parses the `apiVersion` string from a mod manifest and checks compatibility. |

#### Compatibility Rules

| Scenario | Behavior |
|----------|----------|
| Exact match | **Compatible** |
| Patch bump (1.0.0 → 1.0.1) | **Compatible** (bug fixes) |
| Minor bump (1.0.0 → 1.1.0) | **Compatible** (new features, old mods still work) |
| Minor downgrade (1.1.0 → 1.0) | **Compatible with warning** |
| Major bump (1.x → 2.x) | **Incompatible** by default; warning with `AllowOutdatedMods` |
| Major downgrade (2.x → 1.x) | **Incompatible** by default; warning with `AllowOutdatedMods` |
| Same major, future minor | **Compatible with warning** |

The `rejectFutureVersions` manifest flag forces major mismatches to hard-fail even when `AllowOutdatedMods` is enabled.

#### Example

```csharp
// Set engine version once at startup
ModCompatibility.EngineApiVersion = new Version(2, 0, 0);

// Check a mod's compatibility
var report = ModCompatibility.CheckCompatibility("1.5.0");

switch (report.Result)
{
    case CompatibilityResult.Compatible:
        Console.WriteLine("Mod is compatible — loading normally.");
        break;

    case CompatibilityResult.CompatibleWithWarning:
        Console.WriteLine($"Warning: {report.Message}");
        foreach (var issue in report.Issues)
            Console.WriteLine($"  - {issue}");
        break;

    case CompatibilityResult.Incompatible:
        Console.WriteLine($"Cannot load mod: {report.Message}");
        break;
}

// Override to allow outdated mods
ModCompatibility.AllowOutdatedMods = true;
var report2 = ModCompatibility.CheckCompatibility("1.0.0");
// report2.Result == CompatibleWithWarning (major mismatch tolerated)
```

---

### ModLoadOrderResolver

**Namespace:** `Stride.Engine.Modding`  
**File:** `Stride.Engine/Modding/ModLoadOrderResolver.cs`

Resolves deterministic mod load order via topological sort with cycle detection.

**Algorithm:**
1. Parse all mod manifests, build dependency graph
2. Detect cycles using DFS with coloring (White → Gray → Black)
3. Topological sort (Kahn's algorithm)
4. Tie-break by `loadOrder` field (lower = loads first)
5. Report cycles clearly: `"Circular dependency: A → B → C → A"`

**Edge cases:**
- Missing dependency → mod not loaded, error reported
- Circular dependency → all mods in cycle not loaded, error reported
- Conflicting versions → mod not loaded, error reported
- Optional dependencies → mod loads even if optional dependency missing, warning logged

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `ResolveLoadOrder` | `LoadOrderResult ResolveLoadOrder(IEnumerable<ModPackage> mods)` | Resolves the load order for a collection of mod packages. Returns ordered list of loadable mods + errors for unresolvable mods. |
| `IsVersionSatisfied` | `static bool IsVersionSatisfied(string installedVersion, string requiredMinVersion)` | Checks if an installed version satisfies a minimum version requirement (semver: installed >= required). |

#### LoadOrderResult

| Property | Type | Description |
|----------|------|-------------|
| `OrderedMods` | `List<ModPackage>` | Mods in resolved load order (ready to load). |
| `Errors` | `List<ModLoadError>` | Mods that could not be loaded due to errors. |
| `Warnings` | `List<string>` | Warnings for optional dependencies that are missing. |
| `Success` | `bool` | Whether all mods resolved successfully (no errors). |

#### ModLoadError

| Property | Type | Description |
|----------|------|-------------|
| `ModId` | `string` | The mod ID that failed. |
| `Message` | `string` | Human-readable error message. |
| `ErrorType` | `ModLoadErrorType` | The type of error. |

#### ModLoadErrorType (nested enum)

| Value | Description |
|-------|-------------|
| `MissingDependency` | A required dependency is not installed. |
| `CircularDependency` | Mods form a circular dependency chain. |
| `VersionConflict` | Two mods require incompatible versions of the same dependency. |

#### Example

```csharp
var resolver = new ModLoadOrderResolver();
var result = resolver.ResolveLoadOrder(modPackages);

if (result.Success)
{
    Console.WriteLine("All mods resolved successfully:");
    foreach (var mod in result.OrderedMods)
    {
        Console.WriteLine($"  {mod.Manifest.Id} v{mod.Manifest.Version}");
    }
}
else
{
    Console.WriteLine("Mod loading errors:");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"  [{error.ErrorType}] {error.ModId}: {error.Message}");
    }
}

// Log warnings for missing optional dependencies
foreach (var warning in result.Warnings)
{
    Console.WriteLine($"  Warning: {warning}");
}

// Check version satisfaction manually
bool satisfied = ModLoadOrderResolver.IsVersionSatisfied("2.1.0", "2.0.0"); // true
bool satisfied2 = ModLoadOrderResolver.IsVersionSatisfied("1.0.0", "2.0.0"); // false
```

---

## Enums

### ModState

**Namespace:** `Stride.Engine.Modding`  
**File:** `Stride.Engine/Modding/ModPackage.cs`

Mod lifecycle states.

| Value | Description |
|-------|-------------|
| `Loaded` | Mod is loaded and active. |
| `Disabled` | Mod is disabled by the user. |
| `Errored` | Mod was disabled automatically due to an error. |

#### Example

```csharp
var package = modHost.GetPackage("com.example.my-mod");

switch (package.State)
{
    case ModState.Loaded:
        Console.WriteLine("Mod is running.");
        break;
    case ModState.Disabled:
        Console.WriteLine("Mod is disabled.");
        break;
    case ModState.Errored:
        Console.WriteLine($"Mod errored: {package.ErrorReason}");
        break;
}
```

---

### ModSceneLoadBehavior

**Namespace:** `Modulus.Modding.Api`  
**File:** `Modulus.Modding.Api/ModSceneLoadBehavior.cs`

Declares how a mod's scene integrates with the host game. Set in `mod.json` under `scenes[].behavior`, or defaults to `MenuSelect`.

| Value | Integer | Description |
|-------|---------|-------------|
| `MenuSelect` | `0` | No explicit behavior declared — the engine presents this scene as a player-selectable option (e.g., a scene selection menu). This is the default. |
| `Replace` | `1` | Scene replaces the game's main scene entirely (total conversion mod). When loaded, the current scene is unloaded first. |
| `Additive` | `2` | Scene is loaded additively on top of the current scene. Entities from this scene coexist with the game's existing entities. |
| `Background` | `3` | Scene replaces background/environment elements but keeps existing gameplay entities intact. |

---

### CompatibilityResult

**Namespace:** `Stride.Engine.Modding`  
**File:** `Stride.Engine/Modding/ModCompatibility.cs`

Result of an API compatibility check between a mod and the engine.

| Value | Description |
|-------|-------------|
| `Compatible` | Mod is fully compatible — load normally. |
| `CompatibleWithWarning` | Minor/major mismatch but within acceptable range. Mod loads with a warning. |
| `Incompatible` | Mod is incompatible and must not be loaded. |

#### CompatibilityReport

Returned by `ModCompatibility.CheckCompatibility()`:

| Property | Type | Description |
|----------|------|-------------|
| `Result` | `CompatibilityResult` | The compatibility verdict. |
| `ModApiVersion` | `Version` | The mod's declared minimum API version. |
| `EngineApiVersion` | `Version` | The engine's current API version. |
| `Message` | `string` | Human-readable explanation of the compatibility status. |
| `Issues` | `List<string>` | List of specific issues found (warnings or errors). |
| `IsLoadable` | `bool` | Whether the mod should be loaded (`Result != Incompatible`). |

---

## Complete Example

Here is a complete, working mod that demonstrates all major API features:

```csharp
using Modulus.Modding.Api;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// ──────────────────────────────────────────────
// Event types for inter-mod communication
// ──────────────────────────────────────────────
public record ScoreChangedEvent(string PlayerId, int NewScore);
public record GameOverEvent(string PlayerId, int FinalScore);

// ──────────────────────────────────────────────
// Custom component
// ──────────────────────────────────────────────
public class ScoreComponent : IModComponent
{
    public int Score { get; private set; }
    private IModEventBus _eventBus;
    private string _playerId;

    public ScoreComponent(IModEventBus eventBus, string playerId)
    {
        _eventBus = eventBus;
        _playerId = playerId;
    }

    public void OnAttach() => Score = 0;

    public void AddScore(int points)
    {
        Score += points;
        _eventBus.Publish(new ScoreChangedEvent(_playerId, Score));
    }
}

// ──────────────────────────────────────────────
// Custom system
// ──────────────────────────────────────────────
public class ScoreTrackerSystem : IModSystem
{
    public int Priority => 150; // After gameplay systems

    private List<ScoreComponent> _scores = new();

    public void OnRegistered() { }

    public void Update(float deltaTime)
    {
        // Could do score-related logic each frame
    }

    public void OnUnregistered()
    {
        _scores.Clear();
    }

    public void Register(ScoreComponent score) => _scores.Add(score);
}

// ──────────────────────────────────────────────
// Persisted state
// ──────────────────────────────────────────────
public class LeaderboardData
{
    public List<LeaderboardEntry> Entries { get; set; } = new();
}

public class LeaderboardEntry
{
    public string PlayerId { get; set; } = "";
    public int HighScore { get; set; }
    public DateTime AchievedAt { get; set; }
}

// ──────────────────────────────────────────────
// Main mod class — implements everything
// ──────────────────────────────────────────────
public class ScoringMod : IMod, IModSerializable
{
    public string Id => "com.example.scoring-mod";
    public string Name => "Scoring System";
    public Version Version => new(2, 1, 0);
    public Version MinApiVersion => new(1, 0);

    private IModContext _context = null!;
    private ScoreTrackerSystem _system = null!;
    private LeaderboardData _leaderboard = new();

    public void Initialize(IModContext context)
    {
        _context = context;
        _context.Logger.Info($"Initializing {Name} v{Version}");

        // Register our custom system
        _system = new ScoreTrackerSystem();

        // Subscribe to events from other mods
        _context.EventBus.Subscribe<GameOverEvent>(OnGameOver);
    }

    public void OnEnabled()
    {
        _context.Logger.Info("Scoring mod enabled.");
    }

    public void OnDisabled()
    {
        _context.Logger.Info("Scoring mod disabled.");
    }

    // ── IModSerializable ──

    public void Save(Stream stream)
    {
        var json = JsonSerializer.Serialize(_leaderboard, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(json);
    }

    public void Load(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        _leaderboard = JsonSerializer.Deserialize<LeaderboardData>(json)
                       ?? new LeaderboardData();
    }

    // ── Event handlers ──

    private void OnGameOver(GameOverEvent evt)
    {
        // Update leaderboard
        var existing = _leaderboard.Entries.Find(e => e.PlayerId == evt.PlayerId);
        if (existing == null || evt.FinalScore > existing.HighScore)
        {
            _leaderboard.Entries.Add(new LeaderboardEntry
            {
                PlayerId = evt.PlayerId,
                HighScore = evt.FinalScore,
                AchievedAt = DateTime.UtcNow
            });
            _context.Logger.Info($"New high score for {evt.PlayerId}: {evt.FinalScore}");
        }
    }
}
```

**Corresponding `mod.json`:**

```json
{
    "id": "com.example.scoring-mod",
    "name": "Scoring System",
    "version": "2.1.0",
    "apiVersion": "1.0",
    "author": "Example Author",
    "description": "Adds a scoring and leaderboard system.",
    "entryPoint": "ScoringMod, ScoringMod",
    "type": "standard",
    "loadOrder": 100,
    "tags": ["gameplay", "scoring"],
    "dependencies": [],
    "components": [
        { "type": "ScoreComponent, ScoringMod" }
    ],
    "systems": [
        { "type": "ScoreTrackerSystem, ScoringMod", "priority": 150 }
    ],
    "assets": [],
    "scenes": []
}
```

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 4.4.0.2 | Current | Initial API reference documentation. |

---

*This document is auto-generated from the Modulus Engine source code. For the latest version, see the engine repository.*
