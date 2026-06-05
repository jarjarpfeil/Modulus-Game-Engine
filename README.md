<p>
<a href="https://modulus-engine.github.io/">
<picture>
      <source media="(prefers-color-scheme: dark)" srcset="https://media.githubusercontent.com/media/stride3d/stride/84092e8aa924e2039b3f8d968907b48fc699c6b3/sources/data/images/Logo/stride-logo-readme-white.png">
      <source media="(prefers-color-scheme: light)" srcset="https://media.githubusercontent.com/media/stride3d/stride/84092e8aa924e2039b3f8d968907b48fc699c6b3/sources/data/images/Logo/stride-logo-readme-black.png">
      <img alt="Modulus Engine" src="https://media.githubusercontent.com/media/stride3d/stride/84092e8aa924e2039b3f8d968907b48fc699c6b3/sources/data/images/Logo/stride-logo-readme-black.png">
</picture>
</a>
</p>

[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE.md)

# Modulus Engine

A modding-focused game engine built on [Stride](https://stride3d.net/), enabling hot-swappable, cross-game mods via AssemblyLoadContext isolation.

## What is Modulus?

Modulus is a fork of the Stride game engine that adds a first-class modding layer. Two levels of abstraction:

1. **Game developers** — use Stride's full API unchanged
2. **Mod developers** — target `Modulus.Modding.Api` for stable, cross-game mods

### Key Features

- **Hot-swappable mods** — load/unload mods at runtime without restarting
- **Cross-game compatibility** — same mod works across multiple Modulus games
- **AssemblyLoadContext isolation** — mods can't crash the engine or corrupt other mods
- **Deterministic load order** — topological sort with cycle detection
- **Managed C# only** — mods are pure C# assemblies, no native DLLs
- **Editor integration** — mod management panel in Game Studio

## Getting Started

### Prerequisites

- .NET 10 SDK
- Windows (required for Game Studio editor and asset pipeline)
- Visual Studio 2022 or later (recommended)

### Build from Source

```bash
git clone https://github.com/Modulus-Engine/modulus.git
cd modulus
dotnet build build/Stride.sln
```

### Run Tests

```bash
dotnet test build/Stride.Tests.Simple.slnf
```

### Launch Game Studio

```bash
dotnet run --project sources/editor/Stride.GameStudio/Stride.GameStudio.csproj
```

## Project Structure

```
modulus/
├── sources/
│   ├── core/           # Stride core libraries
│   ├── engine/         # Engine runtime (ECS, rendering, physics, audio)
│   ├── editor/         # Game Studio editor
│   ├── assets/         # Asset pipeline
│   └── templates/      # Project templates
├── build/              # Solution files and build scripts
├── docs/               # Internal documentation
└── tests/              # Test projects
```

## Modding (Coming Soon)

Modulus is under active development. The modding API will be available in v1.0.

```csharp
// Example mod (future API)
public class MyMod : IMod
{
    public string Id => "com.example.mymod";
    public string Name => "My Mod";
    public Version Version => new(1, 0, 0);

    public void OnLoaded(IModContext context)
    {
        context.RegisterComponent<HealthComponent>();
        context.RegisterSystem<HealthProcessor>();
    }

    public void OnUnloaded() { }
}
```

## Documentation

- [Stride Documentation](https://doc.stride3d.net/) — base engine documentation
- [Contributing](.github/CONTRIBUTING.md) — how to contribute
- [Architecture](docs/) — internal engine documentation

## License

Modulus is licensed under the [MIT License](LICENSE.md).

Based on [Stride Game Engine](https://stride3d.net/) by .NET Foundation and Stride contributors.
