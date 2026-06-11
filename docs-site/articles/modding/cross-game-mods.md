# Cross-Game Mods

> How a single mod can run in *Dungeon Crawler*, *Space Explorer*, and any other game built on Modulus Engine — without changing a single line of code.

---

## Table of Contents

1. [What Is Cross-Game Modding?](#what-is-cross-game-modding)
2. [How It Works](#how-it-works)
3. [Standalone Interfaces — No Stride Dependency](#standalone-interfaces--no-stride-dependency)
4. [Internal Adapters — Bridging to Stride](#internal-adapters--bridging-to-stride)
5. [The Standard Library](#the-standard-library)
6. [Translation Mods — Converting Between Game Formats](#translation-mods--converting-between-game-formats)
7. [Best Practices for Cross-Game Compatibility](#best-practices-for-cross-game-compatibility)
8. [API Versioning](#api-versioning)
9. [Complete Example — One Mod, Two Games](#complete-example--one-mod-two-games)

---

## What Is Cross-Game Modding?

Cross-game modding means **a single mod package works in multiple games without modification**. Drop the same `.modulus` file into *Dungeon Crawler* and *Space Explorer* and it just works — same inventory system, same health bars, same crafting logic.

This is possible because every game built on Modulus Engine shares a common modding API (`Modulus.Modding.Api`). Mods are written against that API, not against any specific game. The engine handles the translation between what the mod asks for and what each particular game actually provides.

### Why This Matters

| Traditional Modding | Modulus Cross-Game |
|---|---|
| Write a mod per game engine | Write once, run everywhere |
| Learn Godot *and* Unity *and* Stride APIs | Learn one stable API |
| Mods break when the game updates | API contract shields mods from engine changes |
| No community portability | One mod marketplace for all Modulus games |

---

## How It Works

```
┌─────────────────────────────────────────────────────┐
│                    Your Mod                          │
│            (targets Modulus.Modding.Api)              │
└──────────────────────┬──────────────────────────────┘
                       │  depends on
                       ▼
┌─────────────────────────────────────────────────────┐
│              Modulus.Modding.Api                      │
│   IModComponent · IModSystem · IModContext            │
│   (stable ABI — versioned, backward-compatible)      │
└──────────────────────┬──────────────────────────────┘
                       │  bridged by
                       ▼
┌─────────────────────────────────────────────────────┐
│           Internal Adapters (per game)               │
│   StrideComponentAdapter · GodotNodeAdapter · etc.   │
│   (game-specific — you never touch these)            │
└──────────────────────┬──────────────────────────────┘
                       │  adapts to
                       ▼
┌─────────────────────────────────────────────────────┐
│              Game Engine (Stride, etc.)              │
│   EntityComponentSystem · SceneGraph · Physics       │
└─────────────────────────────────────────────────────┘
```

The key insight: **`Modulus.Modding.Api` is the stable ABI (Application Binary Interface).** Mods target this ABI, not Stride or any other engine. The API is delivered as a standalone .NET assembly with zero engine references.

When a game ships, it bundles:

- **Modulus.Modding.Api.dll** — the contract mods build against
- **Modulus.EngineAdapter.Stride.dll** (or similar) — the internal bridge

Mods never reference the adapter directly. They only see the API.

---

## Standalone Interfaces — No Stride Dependency

The two core interfaces every mod author uses are completely engine-agnostic:

### `IModComponent`

Represents a piece of data attached to a game entity (think ECS component).

```csharp
using Modulus.Modding.Api;

/// <summary>
/// A component that can be attached to any entity in any Modulus-powered game.
/// Zero engine dependencies — pure C# interface.
/// </summary>
public interface IModComponent
{
    /// <summary>Unique type identifier for this component.</summary>
    string TypeId { get; }

    /// <summary>Serialize the component to a portable format.</summary>
    ComponentData Serialize();

    /// <summary>Deserialize from a portable format.</summary>
    void Deserialize(ComponentData data);
}
```

### `IModSystem`

Represents logic that runs every frame or on specific events.

```csharp
using Modulus.Modding.Api;

/// <summary>
/// A system that processes components each frame.
/// No knowledge of Stride's GameBase, ScriptComponent, or Entity.
/// </summary>
public interface IModSystem
{
    /// <summary>Called once when the mod is loaded.</summary>
    void Initialize(IModContext context);

    /// <summary>Called every frame (or at the configured tick rate).</summary>
    void Update(IModContext context, float deltaTime);

    /// <summary>Called when the mod is unloaded.</summary>
    void Shutdown(IModContext context);
}
```

### `IModContext`

The runtime interface through which mods interact with the game — again, no engine types leak through.

```csharp
public interface IModContext
{
    // Entity management
    EntityHandle CreateEntity(string name);
    void DestroyEntity(EntityHandle entity);
    IReadOnlyList<EntityHandle> GetEntitiesWithComponent<T>() where T : IModComponent;

    // Component access
    T GetComponent<T>(EntityHandle entity) where T : IModComponent;
    void SetComponent<T>(EntityHandle entity, T component) where T : IModComponent;

    // Events
    void Subscribe<TEvent>(Action<TEvent> handler);
    void Publish<TEvent>(TEvent evt);

    // Asset loading (returns a handle; the adapter resolves the actual type)
    AssetHandle LoadAsset(string path);

    // Logging
    ILogger Log { get; }
}
```

Every type referenced above (`EntityHandle`, `ComponentData`, `AssetHandle`, `ILogger`) lives in `Modulus.Modding.Api`. None of them inherit from or wrap Stride types. This is what makes cross-game compatibility possible.

---

## Internal Adapters — Bridging to Stride

Behind the scenes, each game engine has an **adapter layer** that implements `IModContext` using that engine's native types.

```
StrideComponentAdapter : IModContext
{
    // Internally:
    //   EntityHandle  →  maps to Stride.Entity
    //   GetComponent  →  queries Stride's EntityComponentSystem
    //   CreateEntity  →  calls Stride SceneSystem
    //   LoadAsset     →  calls Stride ContentManager
    //   deltaTime     →  comes from Stride GameTime
}
```

**You never write adapters.** They ship with the engine. When a game developer integrates Modulus into their Stride project, they configure which adapter to use. The mod never knows or cares.

If someone ports Modulus to Godot, they write `GodotNodeAdapter : IModContext` — existing mods continue to work unchanged.

### Adapter Registration (Game Developer Side)

Game developers register the adapter once during startup:

```csharp
// In the game's initialization code (game developer writes this, not mod authors)
var modLoader = new ModLoader();
modLoader.UseAdapter(new StrideComponentAdapter(scene, contentManager, gameServices));
modLoader.LoadModsFrom("mods/");
```

That's the only engine-specific code in the entire mod lifecycle.

---

## The Standard Library

To maximize cross-game compatibility, Modulus ships a **Standard Library** — a curated set of components and systems that cover the most common gameplay needs. The Standard Library is itself a mod (`Modulus.StandardLib.modulus`) that games can opt into.

### What's Included

| Component | Description | Common In |
|---|---|---|
| `HealthComponent` | HP, max HP, regeneration, damage types | Almost every game |
| `InventoryComponent` | Slot-based inventory with stacking | RPGs, survival, adventure |
| `TransformComponent` | Position, rotation, scale (3D) | Every 3D game |
| `ColliderComponent` | Bounding box/sphere collision data | Action, platformer, RPG |
| `MovementComponent` | Velocity, acceleration, friction | Any game with moving entities |
| `DialogueComponent` | Conversation trees with choices | RPG, adventure, visual novel |
| `QuestComponent` | Objectives, rewards, state tracking | RPG, adventure, MMO |
| `LootTableComponent` | Weighted random item drops | RPG, roguelike, survival |
| `StatsComponent` | Key-value stat modifiers (STR, DEX, etc.) | RPG, ARPG |

### Example: Using Standard Library Components

```csharp
using Modulus.StandardLib.Components;
using Modulus.StandardLib.Systems;

public class GoblinMod : IModSystem
{
    public void Initialize(IModContext context)
    {
        // Create a goblin entity using Standard Library components
        var goblin = context.CreateEntity("Goblin");

        // Works in Dungeon Crawler, Space Explorer, or any Modulus game
        context.SetComponent(goblin, new HealthComponent
        {
            CurrentHP = 50,
            MaxHP = 50,
            RegenRate = 1.0f
        });

        context.SetComponent(goblin, new LootTableComponent
        {
            Entries = new[]
            {
                new LootEntry("gold_coin", weight: 80, minCount: 1, maxCount: 10),
                new LootEntry("rusty_sword", weight: 15, minCount: 1, maxCount: 1),
                new LootEntry("health_potion", weight: 5, minCount: 1, maxCount: 3)
            }
        });

        context.SetComponent(goblin, new MovementComponent
        {
            Speed = 3.5f,
            Acceleration = 8.0f
        });

        context.Log.Info("Goblin spawned with Standard Library components.");
    }

    public void Update(IModContext context, float deltaTime) { }
    public void Shutdown(IModContext context) { }
}
```

Because this mod uses only Standard Library components, it works identically in every game that includes `Modulus.StandardLib.modulus`.

---

## Translation Mods — Converting Between Game Formats

Some games don't use Standard Library components — they have their own `GameHealth` or `GameInventory` types. **Translation mods** bridge the gap using `IComponentAdapter`.

### `IComponentAdapter`

```csharp
/// <summary>
/// Translates between a Standard Library component and a game-specific component.
/// Registered with the mod context so mods using Standard Library types
/// automatically work with the game's native types.
/// </summary>
public interface IComponentAdapter<TModComponent, TGameComponent>
    where TModComponent : IModComponent
{
    /// <summary>Convert from the game's native format to the mod format.</summary>
    TModComponent FromGame(TGameComponent gameComponent);

    /// <summary>Convert from the mod format to the game's native format.</summary>
    TGameComponent ToGame(TModComponent modComponent);
}
```

### Example: Health Translation for a Custom RPG

Suppose *Fantasy RPG* has its own health system with armor, resistances, and bleed:

```csharp
// Game-specific type (exists in Fantasy RPG, not in Standard Library)
public class RPGHealth
{
    public float CurrentHP;
    public float MaxHP;
    public float Armor;
    public float BleedRate;
    public Dictionary<string, float> ElementalResistances;
}

// Translation adapter (ships with Fantasy RPG or as a community mod)
public class RPGHealthAdapter : IComponentAdapter<HealthComponent, RPGHealth>
{
    public HealthComponent FromGame(RPGHealth game)
    {
        return new HealthComponent
        {
            CurrentHP = game.CurrentHP,
            MaxHP = game.MaxHP,
            RegenRate = -game.BleedRate // bleed is negative regen
        };
    }

    public RPGHealth ToGame(HealthComponent mod)
    {
        return new RPGHealth
        {
            CurrentHP = mod.CurrentHP,
            MaxHP = mod.MaxHP,
            Armor = 0,           // mod doesn't know about armor — safe default
            BleedRate = -mod.RegenRate,
            ElementalResistances = new Dictionary<string, float>()
        };
    }
}
```

### How Translation Works at Runtime

```
Mod says: SetComponent(goblin, new HealthComponent { CurrentHP = 50 })
    ↓
Engine checks: Does the game have a registered adapter for HealthComponent?
    ↓ YES
Adapter.ToGame(new HealthComponent { CurrentHP = 50 })
    → RPGHealth { CurrentHP = 50, Armor = 0, ... }
    ↓
Game stores RPGHealth natively — game systems (armor, bleed) work as expected
```

The mod never knows it's being translated. The game never knows it's receiving translated data. Both sides work with their preferred types.

### Who Writes Translation Mods?

- **Game developers** ship adapters with their game for maximum fidelity.
- **Community members** can write adapters for popular games and publish them.
- **Mod authors** can bundle adapters for specific games they've tested against.

---

## Best Practices for Cross-Game Compatibility

### 1. Use Standard Library Components Whenever Possible

```csharp
// ✅ Good — works everywhere
context.SetComponent(entity, new HealthComponent { CurrentHP = 100, MaxHP = 100 });

// ❌ Avoid — only works in games that happen to have this type
context.SetComponent(entity, new MyCustomHealth { HitPoints = 100 });
```

If the Standard Library has a component that fits your needs, use it. This guarantees your mod works in every game that ships with `Modulus.StandardLib.modulus`.

### 2. Document Custom Components Thoroughly

If you must define custom components, document them so game developers and community members can write adapters:

```csharp
/// <summary>
/// Component for entities that can cast spells.
/// 
/// ## Cross-Game Notes
/// - Games without a magic system should ignore this component.
/// - If a game has its own spell system, register a SpellAdapter.
/// - Expected game capabilities: cooldown tracking, mana cost, targeting.
/// 
/// ## Fields
/// - SpellId: Identifier for the spell (string, required)
/// - CooldownRemaining: Seconds until the spell can be cast again (float, default 0)
/// - ManaCost: Mana consumed per cast (float, default 10)
/// </summary>
public class SpellComponent : IModComponent
{
    public string TypeId => "mymod.spell";
    public string SpellId { get; set; }
    public float CooldownRemaining { get; set; }
    public float ManaCost { get; set; } = 10f;

    public ComponentData Serialize() { /* ... */ }
    public void Deserialize(ComponentData data) { /* ... */ }
}
```

### 3. Provide Adapters for Popular Games

If you test your mod against specific games, ship translation adapters:

```
MyMod/
├── MyMod.modulus                    # The mod itself
├── Adapters/
│   ├── MyMod.Adapter.DungeonCrawler.modulus
│   └── MyMod.Adapter.SpaceExplorer.modulus
└── README.md
```

### 4. Gracefully Handle Missing Components

Don't assume every game has every component. Check before accessing:

```csharp
public void Update(IModContext context, float deltaTime)
{
    foreach (var entity in context.GetEntitiesWithComponent<SpellComponent>())
    {
        var spell = context.GetComponent<SpellComponent>(entity);

        // Graceful: check if the game supports mana
        if (context.HasComponent<ManaComponent>(entity))
        {
            var mana = context.GetComponent<ManaComponent>(entity);
            if (mana.Current < spell.ManaCost) continue;
            mana.Current -= spell.ManaCost;
            context.SetComponent(entity, mana);
        }
        // else: cast the spell for free — mod still works, just without mana gating

        CastSpell(context, entity, spell);
    }
}
```

### 5. Use Events for Loose Coupling

Instead of directly modifying game-specific state, publish events and let the game (or other mods) respond:

```csharp
// ✅ Good — game-agnostic event
context.Publish(new DamageEvent
{
    Target = entity,
    Amount = 25,
    Type = DamageType.Physical
});

// ❌ Avoid — directly manipulating game-specific systems
context.GetSystem<RPGCombatSystem>().ApplyDamage(entity, 25, "physical");
```

### 6. Test Against Multiple Games

The easiest way to verify cross-game compatibility:

1. Write your mod against the Standard Library
2. Test it in at least two different Modulus games
3. If it works in both without changes, it's cross-game compatible

---

## API Versioning

`Modulus.Modding.Api` uses **Semantic Versioning** (SemVer): `MAJOR.MINOR.PATCH`.

### Compatibility Rules

| Version Change | Behavior | Example |
|---|---|---|
| **PATCH** (e.g., 1.0.0 → 1.0.1) | Always accepted. Bug fixes only. | Fix a serialization edge case |
| **MINOR** (e.g., 1.0.0 → 1.2.0) | Always accepted. New features, no breaking changes. | Add `HasComponent<T>()` to `IModContext` |
| **MAJOR** (e.g., 1.0.0 → 2.0.0) | **Rejected by default.** Breaking changes. | Remove `CreateEntity(string)` signature |

### How Version Checking Works

When the mod loader processes a mod, it reads the declared API version from the mod manifest:

```json
{
    "id": "goblin-expansion",
    "name": "Goblin Expansion",
    "version": "1.2.0",
    "apiVersion": "1.0.0",
    "dependencies": {
        "Modulus.StandardLib": ">=1.0.0"
    }
}
```

The loader then checks against its own `Modulus.Modding.Api` version:

```
Game ships with Modulus.Modding.Api v1.3.0

Mod declares apiVersion: "1.0.0"
  → Major match (1 == 1) ✅
  → Minor: mod wants 0, game has 3 — compatible (game is newer) ✅
  → Result: LOAD ✅

Mod declares apiVersion: "2.0.0"
  → Major mismatch (2 != 1) ❌
  → Result: REJECTED (by default)
```

### Overriding Major Version Rejection

Game developers or advanced users can force-load mods with a major version mismatch:

```csharp
var modLoader = new ModLoader();
modLoader.VersionPolicy = new VersionPolicy
{
    AllowMajorMismatch = true,       // override the default rejection
    LogMajorMismatchWarning = true   // but still warn in the log
};
```

Or via configuration:

```json
{
    "modulus": {
        "versioning": {
            "allowMajorMismatch": true,
            "logWarnings": true
        }
    }
}
```

> **⚠️ Warning:** Overriding major version rejection may cause runtime errors if the mod relies on APIs that were removed or changed. Use at your own risk.

### Version Ranges in Dependencies

Mods can declare version ranges for their dependencies:

```json
{
    "dependencies": {
        "Modulus.StandardLib": ">=1.0.0 <2.0.0",
        "FantasyCombat": "^1.2.0"
    }
}
```

Standard range operators are supported: `>=`, `<=`, `>`, `<`, `^` (caret — compatible with), `~` (tilde — approximately).

---

## Complete Example — One Mod, Two Games

Let's build a **Regeneration Aura** mod that gives nearby allies health regeneration. It will work identically in *Dungeon Crawler* (a fantasy action RPG) and *Space Explorer* (a sci-fi sandbox).

### Step 1: Define the Mod

```csharp
// RegenAura.cs
using Modulus.Modding.Api;
using Modulus.StandardLib.Components;

public class RegenAuraComponent : IModComponent
{
    public string TypeId => "regen-aura.effect";

    /// <summary>Radius of the aura in world units.</summary>
    public float Radius { get; set; } = 5.0f;

    /// <summary>HP restored per second to allies inside the aura.</summary>
    public float RegenPerSecond { get; set; } = 5.0f;

    /// <summary>If true, the aura also affects the entity itself.</summary>
    public bool AffectSelf { get; set; } = true;

    public ComponentData Serialize()
    {
        var data = new ComponentData();
        data.SetFloat("radius", Radius);
        data.SetFloat("regenPerSecond", RegenPerSecond);
        data.SetBool("affectSelf", AffectSelf);
        return data;
    }

    public void Deserialize(ComponentData data)
    {
        Radius = data.GetFloat("radius", 5.0f);
        RegenPerSecond = data.GetFloat("regenPerSecond", 5.0f);
        AffectSelf = data.GetBool("affectSelf", true);
    }
}

public class RegenAuraSystem : IModSystem
{
    private IModContext _context;

    public void Initialize(IModContext context)
    {
        _context = context;
        _context.Log.Info("RegenAura mod loaded.");
    }

    public void Update(IModContext context, float deltaTime)
    {
        foreach (var auraEntity in context.GetEntitiesWithComponent<RegenAuraComponent>())
        {
            var aura = context.GetComponent<RegenAuraComponent>(auraEntity);
            var auraPos = context.GetComponent<TransformComponent>(auraEntity);

            if (auraPos == null) continue;

            foreach (var ally in context.GetEntitiesWithComponent<HealthComponent>())
            {
                if (!aura.AffectSelf && ally == auraEntity) continue;

                var allyPos = context.GetComponent<TransformComponent>(ally);
                if (allyPos == null) continue;

                float distance = Vector3.Distance(auraPos.Position, allyPos.Position);
                if (distance > aura.Radius) continue;

                // Apply regeneration
                var health = context.GetComponent<HealthComponent>(ally);
                health.CurrentHP = Math.Min(health.MaxHP,
                    health.CurrentHP + aura.RegenPerSecond * deltaTime);
                context.SetComponent(ally, health);
            }
        }
    }

    public void Shutdown(IModContext context)
    {
        context.Log.Info("RegenAura mod unloaded.");
    }
}
```

### Step 2: Package the Mod Manifest

```json
{
    "id": "regen-aura",
    "name": "Regeneration Aura",
    "version": "1.0.0",
    "apiVersion": "1.0.0",
    "author": "Community",
    "description": "Grants health regeneration to nearby allies.",
    "dependencies": {
        "Modulus.StandardLib": ">=1.0.0"
    },
    "systems": [
        "RegenAuraSystem"
    ]
}
```

### Step 3: Drop It Into Two Games

#### In *Dungeon Crawler* (Stride-based fantasy RPG)

```
DungeonCrawler/
├── mods/
│   ├── regen-aura.modulus          ← same file
│   └── Modulus.StandardLib.modulus
└── DungeonCrawler.exe
```

A Paladin character spawns with the aura:

```csharp
var paladin = context.CreateEntity("Paladin");
context.SetComponent(palladin, new TransformComponent { Position = new Vector3(0, 0, 0) });
context.SetComponent(palladin, new HealthComponent { CurrentHP = 200, MaxHP = 200 });
context.SetComponent(palladin, new RegenAuraComponent
{
    Radius = 8.0f,
    RegenPerSecond = 10.0f,
    AffectSelf = true
});
```

Nearby warriors, mages, and rogues automatically gain 10 HP/sec while within 8 units.

#### In *Space Explorer* (Stride-based sci-fi sandbox)

```
SpaceExplorer/
├── mods/
│   ├── regen-aura.modulus          ← same file
│   └── Modulus.StandardLib.modulus
└── SpaceExplorer.exe
```

A Repair Drone entity uses the same aura:

```csharp
var drone = context.CreateEntity("RepairDrone");
context.SetComponent(drone, new TransformComponent { Position = new Vector3(50, 10, 0) });
context.SetComponent(drone, new HealthComponent { CurrentHP = 75, MaxHP = 75 });
context.SetComponent(drone, new RegenAuraComponent
{
    Radius = 12.0f,
    RegenPerSecond = 3.0f,
    AffectSelf = false
});
```

Nearby spaceships and stations get 3 HP/sec hull repair within 12 units. The drone doesn't heal itself.

### Why It Works in Both Games

1. **Only uses `Modulus.Modding.Api` and `Modulus.StandardLib`** — no Stride, no game-specific types.
2. **Uses `HealthComponent` and `TransformComponent`** from the Standard Library — both games support them.
3. **The API version (`1.0.0`)** is compatible with both games (both ship with `Modulus.Modding.Api` v1.x).
4. **No custom adapters needed** — Standard Library components map directly to both games' internal systems via their respective engine adapters.

### What Would Break Cross-Game Compatibility?

```csharp
// ❌ This breaks cross-game compatibility:
using Stride.Core.Mathematics;          // Stride-specific namespace
using DungeonCrawler.Combat;            // Game-specific namespace

// ❌ This only works if the game happens to have a "ManaComponent":
context.GetComponent<ManaComponent>(entity);

// ❌ This assumes a game-specific system exists:
context.GetSystem<SpaceExplorer.FlightSystem>().SetCourse(star);
```

Any of these would make the mod work in one game but crash or fail silently in another.

---

## Summary

| Concept | Key Takeaway |
|---|---|
| **Cross-Game Modding** | One mod package, multiple games, zero changes |
| **Modulus.Modding.Api** | The stable ABI — mods build against this, never the engine |
| **IModComponent / IModSystem** | Pure C# interfaces with zero engine dependencies |
| **Internal Adapters** | Bridge API calls to Stride/Godot/etc. — you never touch them |
| **Standard Library** | Pre-built components (Health, Inventory, etc.) that maximize compatibility |
| **Translation Mods** | `IComponentAdapter` converts between Standard Library and game-specific types |
| **Best Practices** | Use Standard Lib, document customs, provide adapters, handle missing components |
| **API Versioning** | Minor/patch always work; major rejected by default (overridable) |

---

*For more details, see [Modding API Reference](./api-reference.md) and [Standard Library Catalog](./standard-library.md).*
