# Modulus Engine

A modding-focused game engine built on [Stride](https://stride3d.net), enabling hot-swappable, cross-game mods via AssemblyLoadContext isolation.

## What is Modulus Engine?

Modulus Engine is a fork of Stride that adds a complete modding layer:

- **Game developers** use Stride's full API unchanged
- **Modders** target the stable `Modulus.Modding.Api` — same mod works across multiple games
- **Gamers** install/manage mods via a simple UI or CLI

## Quick Start

### For Game Developers

```bash
# Install templates
dotnet new install ModulusEngine.Templates

# Create a new game
dotnet new modulus-game -n MyAwesomeGame
cd MyAwesomeGame
dotnet build
```

### For Modders

```bash
# Create a new mod
dotnet new modulus-mod -n MyCoolMod
cd MyCoolMod
dotnet build

# Package your mod
dotnet pack
```

### For Gamers

1. Download `.modpkg` files from mod repositories
2. Place them in your game's `mods/` directory
3. Launch the game — mods load automatically
4. Use the in-game Mod Manager to enable/disable mods

## Key Features

| Feature | Description |
|---------|-------------|
| **AssemblyLoadContext Isolation** | Each mod gets its own ALC — unload cleanly without restarting |
| **Cross-Game Mods** | Same mod works in any Modulus-powered game |
| **Hot-Reload** | Reload mods at runtime without restarting the game |
| **Crash Safety** | Mod exceptions are caught — the game never crashes from a mod |
| **Save Compatibility** | Orphaned components are preserved when mods are uninstalled |
| **Deterministic Load Order** | Topological sort with cycle detection for reliable dependency resolution |

## Architecture Overview

```
┌─────────────────────────────────────────────┐
│              Game Studio (Editor)            │
│  ┌─────────────────────────────────────────┐ │
│  │         Mod Manager Panel               │ │
│  │  • Install/Uninstall .modpkg            │ │
│  │  • Enable/Disable mods                  │ │
│  │  • View mod details                     │ │
│  └─────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────┐
│              Modulus Engine                   │
│  ┌─────────────────────────────────────────┐ │
│  │           ModHost                       │ │
│  │  • Discovery    • Validation            │ │
│  │  • Loading      • Lifecycle             │ │
│  │  • Unloading    • State Persistence     │ │
│  └─────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────┐ │
│  │        Modulus.Modding.Api              │ │
│  │  • IMod        • IModContext            │ │
│  │  • IModComponent • IModSystem           │ │
│  │  • IModEventBus • IModSerializable      │ │
│  └─────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────┐ │
│  │        Stride Engine (Fork)             │ │
│  │  • ECS    • Rendering    • Physics      │ │
│  │  • Audio  • Content      • Editor       │ │
│  └─────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────┐
│              Mod Packages (.modpkg)           │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │  Mod A   │  │  Mod B   │  │  Mod C   │  │
│  │ (gameplay)│  │ (assets) │  │ (shaders)│  │
│  └──────────┘  └──────────┘  └──────────┘  │
└─────────────────────────────────────────────┘
```

## Documentation

- [Getting Started](docs/articles/modding/writing-a-mod.html) — Write your first mod
- [Mod API Reference](docs/articles/modding/mod-api-reference.html) — Complete API docs
- [Cross-Game Mods](docs/articles/modding/cross-game-mods.html) — Build mods that work across games
- [Architecture](docs/articles/modding/architecture.html) — How the modding system works
- [MCP Server](docs/articles/mcp-server/setup.html) — AI agent integration
- [Contributing](docs/articles/development/fork-management.html) — Development workflow

## Requirements

- .NET 10 SDK
- Windows (for engine development and Game Studio)
- Any .NET 10 platform (for mod development)

## License

MIT License — same as Stride.
