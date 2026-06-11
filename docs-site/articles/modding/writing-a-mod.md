# Writing a Mod for Modulus Engine

A comprehensive guide to creating mods for the Modulus Engine — a modding-focused game engine built on [Stride](https://stride3d.net/) and .NET 10.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Creating a Mod Project](#2-creating-a-mod-project)
3. [The mod.json Manifest](#3-the-modjson-manifest)
4. [Implementing the IMod Interface](#4-implementing-the-imod-interface)
5. [Creating Custom Components](#5-creating-custom-components)
6. [Creating Custom Systems](#6-creating-custom-systems)
7. [Using the Event Bus](#7-using-the-event-bus)
8. [State Persistence](#8-state-persistence)
9. [Building and Packaging](#9-building-and-packaging)
10. [Testing Your Mod](#10-testing-your-mod)

---

## 1. Prerequisites

Before you begin, make sure you have the following installed:

| Requirement | Details |
|---|---|
| **.NET 10 SDK** | Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0). Available for Windows, macOS, and Linux. |
| **A code editor** | Visual Studio 2026, JetBrains Rider, or VS Code with the C# Dev Kit extension. |
| **Modulus Engine** | The engine binaries or source. Mods reference the `Modulus.Engine` NuGet package or a local project reference. |
| **Modulus CLI Templates** | Installed via `dotnet new install` (see below). |

### Installing the Mod Template

The Modulus Engine ships a `dotnet new` template that scaffolds a ready-to-build mod project:

```bash
dotnet new install Modulus.Templates
```

Verify the template is available:

```bash
dotnet new list modulus
```

You should see `modulus-mod` in the output.

---

## 2. Creating a Mod Project

Use the `modulus-mod` template to generate a new mod project:

```bash
dotnet new modulus-mod -n MyFirstMod
cd MyFirstMod
```

This creates the following structure:

```
MyFirstMod/
├── MyFirstMod.csproj
├── mod.json
├── ModEntry.cs
└── Properties/
    └── launchSettings.json
```

### Key files

| File | Purpose |
|---|---|
| `MyFirstMod.csproj` | .NET project file targeting `net10.0`. References the `Modulus.Engine` package. |
| `mod.json` | The mod manifest (metadata, dependencies, entry point). |
| `ModEntry.cs` | Skeleton implementation of the `IMod` interface. |
| `Properties/launchSettings.json` | Debug launch profile that points at the Modulus host executable. |

Open the project in your editor of choice and restore packages:

```bash
dotnet restore
```

---

## 3. The mod.json Manifest

Every mod **must** have a `mod.json` file at its root. This is the manifest that the engine reads at load time.

### Complete schema

```json
{
  "id": "com.example.myfirstmod",
  "name": "My First Mod",
  "version": "1.0.0",
  "apiVersion": "1.0.0",
  "type": "mod",
  "entryPoint": "MyFirstMod.ModEntry, MyFirstMod",
  "loadOrder": 100,
  "dependencies": [
    {
      "id": "com.example.corelib",
      "version": ">=2.0.0"
    }
  ],
  "components": [
    "MyFirstMod.Components.HealthComponent",
    "MyFirstMod.Components.InventoryComponent"
  ],
  "systems": [
    "MyFirstMod.Systems.HealthRegenSystem",
    "MyFirstMod.Systems.InventorySystem"
  ],
  "assets": [
    "Assets/Textures/*.png",
    "Assets/Audio/*.ogg",
    "Assets/Data/*.json"
  ]
}
```

### Field reference

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | `string` | ✅ | Reverse-domain identifier. Must be globally unique. |
| `name` | `string` | ✅ | Human-readable display name. |
| `version` | `string` | ✅ | Semantic version (`MAJOR.MINOR.PATCH`). |
| `apiVersion` | `string` | ✅ | Minimum Modulus API version this mod targets. |
| `type` | `string` | ✅ | One of `"mod"`, `"plugin"`, or `"library"`. |
| `entryPoint` | `string` | ✅ | Assembly-qualified type name of the class implementing `IMod`. |
| `loadOrder` | `int` | ❌ | Integer controlling load priority. Lower values load first. Default: `100`. |
| `dependencies` | `array` | ❌ | List of mods this mod depends on. Each entry has `id` and `version` (NuGet-style range). |
| `components` | `array` | ❌ | Fully qualified type names of `EntityComponent` subclasses to register. |
| `systems` | `array` | ❌ | Fully qualified type names of `EntityProcessor` subclasses to register. |
| `assets` | `array` | ❌ | Glob patterns for asset files to bundle with the mod. |

---

## 4. Implementing the IMod Interface

The `IMod` interface is the entry point for every mod. The engine instantiates your entry-point class and calls its lifecycle methods.

### Interface contract

```csharp
namespace Modulus.Engine.Modding;

/// <summary>
/// Implement this interface in your mod's entry-point class.
/// </summary>
public interface IMod
{
    /// <summary>Unique mod identifier (must match mod.json id).</summary>
    string Id { get; }

    /// <summary>Human-readable name.</summary>
    string Name { get; }

    /// <summary>Semantic version string.</summary>
    string Version { get; }

    /// <summary>Minimum Modulus API version required.</summary>
    string MinApiVersion { get; }

    /// <summary>
    /// Called once when the mod is first loaded by the engine.
    /// Register components, systems, event handlers, and services here.
    /// </summary>
    void Initialize(IModContext context);

    /// <summary>Called when the mod transitions to the enabled state.</summary>
    void OnEnabled();

    /// <summary>Called when the mod is disabled or unloaded.</summary>
    void OnDisabled();
}
```

### Complete example

```csharp
using Modulus.Engine.Modding;
using Modulus.Engine.ECS;
using MyFirstMod.Components;
using MyFirstMod.Systems;

namespace MyFirstMod;

public class ModEntry : IMod
{
    public string Id => "com.example.myfirstmod";
    public string Name => "My First Mod";
    public string Version => "1.0.0";
    public string MinApiVersion => "1.0.0";

    private IModContext _context = null!;

    public void Initialize(IModContext context)
    {
        _context = context;

        // Register custom components and systems
        context.RegisterComponent<HealthComponent>();
        context.RegisterComponent<InventoryComponent>();
        context.RegisterSystem<HealthRegenSystem>();
        context.RegisterSystem<InventorySystem>();

        // Subscribe to engine events
        context.EventBus.Subscribe<EntitySpawnedEvent>(OnEntitySpawned);

        context.Logger.Info($"{Name} v{Version} initialized.");
    }

    public void OnEnabled()
    {
        _context.Logger.Info($"{Name} enabled.");
    }

    public void OnDisabled()
    {
        _context.EventBus.Unsubscribe<EntitySpawnedEvent>(OnEntitySpawned);
        _context.Logger.Info($"{Name} disabled.");
    }

    private void OnEntitySpawned(EntitySpawnedEvent evt)
    {
        _context.Logger.Debug($"Entity {evt.Entity.Id} spawned in scene.");
    }
}
```

---

## 5. Creating Custom Components

Components are plain data containers attached to entities. In Modulus Engine (built on Stride's ECS), components inherit from `EntityComponent` and use `[DataContract]` / `[DataMember]` attributes for serialization.

### Example: HealthComponent

```csharp
using Modulus.Engine.ECS;
using Modulus.Engine.Serialization;

namespace MyFirstMod.Components;

/// <summary>
/// Tracks an entity's current and maximum health.
/// </summary>
[DataContract("MyFirstMod.HealthComponent")]
public class HealthComponent : EntityComponent
{
    [DataMember(1)]
    public float CurrentHealth { get; set; } = 100f;

    [DataMember(2)]
    public float MaxHealth { get; set; } = 100f;

    [DataMember(3)]
    public float RegenRate { get; set; } = 1.0f; // HP per second

    /// <summary>
    /// Convenience property — true when health has reached zero.
    /// </summary>
    [DataMemberIgnore]
    public bool IsDead => CurrentHealth <= 0f;

    /// <summary>
    /// Apply damage. Clamps to zero.
    /// </summary>
    public void TakeDamage(float amount)
    {
        CurrentHealth = Math.Max(0f, CurrentHealth - amount);
    }

    /// <summary>
    /// Heal. Clamps to max.
    /// </summary>
    public void Heal(float amount)
    {
        CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
    }
}
```

### Example: InventoryComponent

```csharp
using Modulus.Engine.ECS;
using Modulus.Engine.Serialization;

namespace MyFirstMod.Components;

[DataContract("MyFirstMod.InventoryComponent")]
public class InventoryComponent : EntityComponent
{
    [DataMember(1)]
    public int MaxSlots { get; set; } = 20;

    [DataMember(2)]
    public List<InventorySlot> Slots { get; set; } = new();

    public bool AddItem(string itemId, int quantity = 1)
    {
        // Try to stack first
        var existing = Slots.FirstOrDefault(s => s.ItemId == itemId && s.Quantity < s.MaxStack);
        if (existing != null)
        {
            existing.Quantity += quantity;
            return true;
        }

        if (Slots.Count >= MaxSlots)
            return false;

        Slots.Add(new InventorySlot { ItemId = itemId, Quantity = quantity });
        return true;
    }

    public bool RemoveItem(string itemId, int quantity = 1)
    {
        var slot = Slots.FirstOrDefault(s => s.ItemId == itemId);
        if (slot == null) return false;

        slot.Quantity -= quantity;
        if (slot.Quantity <= 0)
            Slots.Remove(slot);

        return true;
    }
}

[DataContract("MyFirstMod.InventorySlot")]
public class InventorySlot
{
    [DataMember(1)]
    public string ItemId { get; set; } = string.Empty;

    [DataMember(2)]
    public int Quantity { get; set; }

    [DataMember(3)]
    public int MaxStack { get; set; } = 99;
}
```

### Key rules for components

- Always annotate the class with `[DataContract("UniqueId")]`. The string should be a stable, globally unique identifier.
- Mark serializable fields with `[DataMember(order)]`. The order integer determines serialization order and must be stable across versions.
- Use `[DataMemberIgnore]` for computed properties.
- Components hold **data only** — put logic in systems.

---

## 6. Creating Custom Systems

Systems contain the logic that operates on components. In Modulus Engine, systems inherit from `EntityProcessor<TComponent>`, where `TComponent` is the component type the system processes.

### Example: HealthRegenSystem

```csharp
using Modulus.Engine.ECS;
using Modulus.Engine.Timing;
using MyFirstMod.Components;

namespace MyFirstMod.Systems;

/// <summary>
/// Regenerates health on entities that have a HealthComponent.
/// Runs every frame, applying regen scaled by delta time.
/// </summary>
public class HealthRegenSystem : EntityProcessor<HealthComponent>
{
    /// <summary>
    /// Called by the engine for every entity that has a HealthComponent.
    /// </summary>
    protected override void Process(Entity entity, HealthComponent health, GameTime time)
    {
        if (health.IsDead)
            return;

        if (health.CurrentHealth < health.MaxHealth)
        {
            health.Heal(health.RegenRate * (float)time.Elapsed.TotalSeconds);
        }
    }
}
```

### Example: InventorySystem

```csharp
using Modulus.Engine.ECS;
using Modulus.Engine.Timing;
using MyFirstMod.Components;

namespace MyFirstMod.Systems;

/// <summary>
/// Handles inventory-related logic, such as auto-sorting.
/// </summary>
public class InventorySystem : EntityProcessor<InventoryComponent>
{
    protected override void Process(Entity entity, InventoryComponent inventory, GameTime time)
    {
        // Remove empty slots
        inventory.Slots.RemoveAll(s => s.Quantity <= 0);
    }
}
```

### Multi-component processing

If your system needs to operate on entities that have **multiple** component types, use the generic overload:

```csharp
using Modulus.Engine.ECS;
using Modulus.Engine.Timing;
using MyFirstMod.Components;

namespace MyFirstMod.Systems;

/// <summary>
/// Applies poison damage over time to entities that have both
/// a HealthComponent and a PoisonEffectComponent.
/// </summary>
public class PoisonSystem : EntityProcessor<HealthComponent, PoisonEffectComponent>
{
    protected override void Process(
        Entity entity,
        HealthComponent health,
        PoisonEffectComponent poison,
        GameTime time)
    {
        var dt = (float)time.Elapsed.TotalSeconds;
        health.TakeDamage(poison.DamagePerSecond * dt);

        poison.RemainingDuration -= dt;
        if (poison.RemainingDuration <= 0f)
        {
            entity.Remove(poison); // Remove the poison effect component
        }
    }
}
```

### Key rules for systems

- Override `Process(Entity, TComponent, GameTime)` (or the multi-component variant).
- Systems are automatically called each frame for every matching entity.
- Keep systems focused — one responsibility per system.
- Don't store per-entity state in the system; use components for that.

---

## 7. Using the Event Bus

The event bus provides a decoupled publish/subscribe messaging system for mod-to-mod and mod-to-engine communication.

### Defining an event

Events are plain C# classes (records work great):

```csharp
namespace MyFirstMod.Events;

/// <summary>
/// Published when a player picks up an item.
/// </summary>
public record ItemPickedUpEvent(
    Entity Player,
    string ItemId,
    int Quantity
);

/// <summary>
/// Published when an entity's health reaches zero.
/// </summary>
public record EntityDiedEvent(
    Entity Entity,
    Entity? Killer = null
);
```

### Subscribing to events

Subscribe in your `IMod.Initialize()` method (or wherever appropriate):

```csharp
public void Initialize(IModContext context)
{
    _context = context;

    // Subscribe to built-in and custom events
    context.EventBus.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
    context.EventBus.Subscribe<EntityDiedEvent>(OnEntityDied);
    context.EventBus.Subscribe<EntitySpawnedEvent>(OnEntitySpawned);
}

private void OnItemPickedUp(ItemPickedUpEvent evt)
{
    _context.Logger.Info($"{evt.Player.Id} picked up {evt.Quantity}x {evt.ItemId}.");
}

private void OnEntityDied(EntityDiedEvent evt)
{
    _context.Logger.Warn($"Entity {evt.Entity.Id} has died.");
}

private void OnEntitySpawned(EntitySpawnedEvent evt)
{
    // Add default health to newly spawned entities
    var health = evt.Entity.Get<HealthComponent>();
    if (health == null)
    {
        evt.Entity.Add(new HealthComponent());
    }
}
```

### Publishing events

Publish from any code that has access to the `IModContext`:

```csharp
// Inside a system or mod callback
_context.EventBus.Publish(new ItemPickedUpEvent(
    Player: playerEntity,
    ItemId: "sword_iron",
    Quantity: 1
));
```

### Unsubscribing

Always unsubscribe in `OnDisabled()` to prevent memory leaks:

```csharp
public void OnDisabled()
{
    _context.EventBus.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
    _context.EventBus.Unsubscribe<EntityDiedEvent>(OnEntityDied);
    _context.EventBus.Unsubscribe<EntitySpawnedEvent>(OnEntitySpawned);
}
```

### Event bus best practices

- **Unsubscribe in `OnDisabled()`** — always pair subscribe/unsubscribe calls.
- **Keep event payloads immutable** — use `record` types.
- **Avoid heavy processing in handlers** — defer complex work to systems.
- **Use specific event types** — don't create a generic `GameEvent` with string-based routing.

---

## 8. State Persistence

If your mod needs to save and load state (e.g., mod-specific configuration, tracked progress, or custom data), implement the `IModSerializable` interface.

### The IModSerializable interface

```csharp
namespace Modulus.Engine.Modding;

/// <summary>
/// Implement on your mod entry class to support save/load.
/// </summary>
public interface IModSerializable
{
    /// <summary>Serialize mod state to the provided writer.</summary>
    void Serialize(IModDataWriter writer);

    /// <summary>Deserialize mod state from the provided reader.</summary>
    void Deserialize(IModDataReader reader);
}
```

### Example: Saving and loading mod state

```csharp
using Modulus.Engine.Modding;

namespace MyFirstMod;

public class ModEntry : IMod, IModSerializable
{
    // ... IMod implementation omitted for brevity ...

    // ---- Mod-specific state ----
    private int _totalKills;
    private float _playTimeSeconds;
    private Dictionary<string, bool> _unlockedAchievements = new();

    // --- IModSerializable ---

    public void Serialize(IModDataWriter writer)
    {
        writer.WriteInt32("totalKills", _totalKills);
        writer.WriteFloat("playTimeSeconds", _playTimeSeconds);
        writer.BeginArray("achievements");
        foreach (var (key, value) in _unlockedAchievements)
        {
            writer.BeginObject();
            writer.WriteString("id", key);
            writer.WriteBoolean("unlocked", value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public void Deserialize(IModDataReader reader)
    {
        _totalKills = reader.ReadInt32("totalKills");
        _playTimeSeconds = reader.ReadFloat("playTimeSeconds");

        _unlockedAchievements.Clear();
        reader.BeginArray("achievements");
        while (reader.HasNext())
        {
            reader.BeginObject();
            var id = reader.ReadString("id");
            var unlocked = reader.ReadBoolean("unlocked");
            _unlockedAchievements[id] = unlocked;
            reader.EndObject();
        }
        reader.EndArray();
    }

    // --- Helper methods that modify state ---

    public void RecordKill()
    {
        _totalKills++;
        _context.Logger.Debug($"Total kills: {_totalKills}");
    }

    public void UnlockAchievement(string achievementId)
    {
        _unlockedAchievements[achievementId] = true;
        _context.EventBus.Publish(new AchievementUnlockedEvent(achievementId));
    }
}
```

### Persisting component data

Components annotated with `[DataContract]` are automatically serialized by the engine. You don't need to implement `IModSerializable` for component data — only for mod-level state that lives outside components.

---

## 9. Building and Packaging

### Building

Build the mod like any .NET project:

```bash
dotnet build -c Release
```

This produces compiled DLLs in `bin/Release/net10.0/`.

### Packaging into .modpkg

The `.modpkg` format is a standard ZIP archive containing:

1. `mod.json` — the manifest.
2. Compiled DLL(s) — the mod assembly and any dependencies.
3. Assets — any files matched by the `assets` globs in `mod.json`.

Use the Modulus CLI to package:

```bash
dotnet modulus pack
```

Or manually create the package:

```bash
# From the project root
dotnet publish -c Release -o ./publish

cd publish
zip -r ../MyFirstMod.modpkg \
    mod.json \
    MyFirstMod.dll \
    MyFirstMod.deps.json \
    Assets/
```

The resulting `MyFirstMod.modpkg` can be:

- **Dropped into the engine's `Mods/` directory** for local testing.
- **Shared with other users** who can install it via the engine's mod manager.
- **Uploaded to a mod repository** (if your game provides one).

### Versioning tips

- Bump `MAJOR` for breaking changes (removed fields, incompatible API).
- Bump `MINOR` for new features that are backward-compatible.
- Bump `PATCH` for bug fixes.
- Update `apiVersion` in `mod.json` if you require a newer engine API.

---

## 10. Testing Your Mod

### Local testing with the engine

1. Copy your `.modpkg` (or the built `publish/` folder) into the engine's `Mods/` directory:

   ```bash
   cp MyFirstMod.modpkg /path/to/modulus-engine/Mods/
   ```

2. Launch the engine. Your mod should appear in the mod manager with a green indicator if it loaded successfully.

3. Check the engine's log output for your mod's initialization messages.

### Debugging with an IDE

The template includes `Properties/launchSettings.json` which configures the debugger to launch the Modulus host executable:

```json
{
  "profiles": {
    "Debug Mod": {
      "commandName": "Executable",
      "executablePath": "C:/Program Files/Modulus Engine/Modulus.Host.exe",
      "commandLineArgs": "--mod-debug MyFirstMod",
      "workingDirectory": "C:/Program Files/Modulus Engine"
    }
  }
}
```

Press **F5** in Visual Studio or Rider to attach the debugger directly to your mod code. Breakpoints in `Initialize`, `Process`, and event handlers will be hit normally.

### Unit testing

Mod components and systems can be tested outside the engine using standard .NET test projects:

```bash
dotnet new xunit -n MyFirstMod.Tests
dotnet add MyFirstMod.Tests reference MyFirstMod
```

Example test:

```csharp
using MyFirstMod.Components;
using Xunit;

namespace MyFirstMod.Tests;

public class HealthComponentTests
{
    [Fact]
    public void TakeDamage_ReducesHealth()
    {
        var health = new HealthComponent { CurrentHealth = 50f, MaxHealth = 100f };
        health.TakeDamage(20f);
        Assert.Equal(30f, health.CurrentHealth);
    }

    [Fact]
    public void TakeDamage_ClampsToZero()
    {
        var health = new HealthComponent { CurrentHealth = 10f };
        health.TakeDamage(999f);
        Assert.True(health.IsDead);
        Assert.Equal(0f, health.CurrentHealth);
    }

    [Fact]
    public void Heal_ClampsToMax()
    {
        var health = new HealthComponent { CurrentHealth = 90f, MaxHealth = 100f };
        health.Heal(50f);
        Assert.Equal(100f, health.CurrentHealth);
    }

    [Fact]
    public void AddItem_StacksExistingItem()
    {
        var inv = new InventoryComponent();
        inv.AddItem("sword", 1);
        inv.AddItem("sword", 2);

        Assert.Single(inv.Slots);
        Assert.Equal(3, inv.Slots[0].Quantity);
    }

    [Fact]
    public void AddItem_ReturnsFalseWhenFull()
    {
        var inv = new InventoryComponent { MaxSlots = 1 };
        inv.AddItem("sword");
        Assert.False(inv.AddItem("shield"));
    }
}
```

Run tests:

```bash
dotnet test MyFirstMod.Tests
```

### Checklist before release

- [ ] `mod.json` has a unique `id`, correct `version`, and accurate `apiVersion`.
- [ ] `Initialize` registers all components and systems.
- [ ] `OnDisabled` unsubscribes all event handlers.
- [ ] `IModSerializable` (if implemented) round-trips correctly — test save then load.
- [ ] `dotnet build -c Release` succeeds with no warnings.
- [ ] `dotnet modulus pack` produces a valid `.modpkg`.
- [ ] Mod loads in the engine without errors.
- [ ] Unit tests pass: `dotnet test`.

---

## Quick Reference

| Task | Command / Code |
|---|---|
| Install template | `dotnet new install Modulus.Templates` |
| Create mod project | `dotnet new modulus-mod -n MyMod` |
| Build | `dotnet build -c Release` |
| Package | `dotnet modulus pack` |
| Register component | `context.RegisterComponent<T>()` in `Initialize` |
| Register system | `context.RegisterSystem<T>()` in `Initialize` |
| Subscribe to event | `context.EventBus.Subscribe<TEvent>(handler)` |
| Publish event | `context.EventBus.Publish(new TEvent(...))` |
| Unsubscribe | `context.EventBus.Unsubscribe<TEvent>(handler)` |

---

## Complete Project File

For reference, here is the full `.csproj` generated by the template:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Modulus.Engine" Version="1.0.0" />
  </ItemGroup>

</Project>
```

---

*Happy modding! For questions, visit the [Modulus Engine community forums](https://modulus-engine.dev/community) or open an issue on the [GitHub repository](https://github.com/modulus-engine/modulus).*
