# Modulus Engine — AI Agent Instructions

This document provides guidelines for AI coding assistants (Copilot, Claude, Cursor, etc.) working in the Modulus Engine repository.

## Project Overview

Modulus is a modding-focused game engine forked from [Stride](https://stride3d.net/). The codebase is primarily C# targeting .NET 10.

## Coding Guidelines

- **Language:** C# 12+ with .NET 10 features
- **Style:** Follow existing Stride conventions (PascalCase for public, _camelCase for private fields)
- **No `#region` directives** — prefer clear, self-documenting code
- **XML documentation** required for all public APIs
- **Tests:** xUnit for unit tests, integration tests for engine systems

## Architecture

### Key Systems

- **ECS:** Entity Component System (`Stride.Engine`)
- **Rendering:** Graphics abstraction layer (`Stride.Graphics`)
- **Assets:** Content pipeline and asset management (`Stride.Assets`)
- **Editor:** Game Studio WPF application (`Stride.GameStudio`)
- **Modding:** AssemblyLoadContext-based mod isolation (`Modulus.Modding.Api`) — *coming soon*

### Project Structure

```
sources/
├── core/           # Stride.Core — serialization, reflection, math
├── engine/         # Stride.Engine — ECS, scene, components
├── graphics/       # Stride.Graphics — GPU abstraction
├── rendering/      # Stride.Rendering — render pipeline
├── editor/         # Stride.GameStudio — WPF editor
├── assets/         # Stride.Assets — content pipeline
└── templates/      # Project templates
```

## Pull Request Reviews

When reviewing PRs:

- Focus on logic, safety, performance, and code consistency
- Avoid suggesting large architectural changes in PR reviews
- Comments on formatting, grammar, or spelling are welcome
- Do not review auto-generated, third-party code, binary files, or assets
- If you find a bug or performance issue, suggest a concrete fix

## Modding API (Planned)

The modding layer will be in `sources/engine/Stride.Engine/Modding/` and `Modulus.Modding.Api`. Key design principles:

- **Stable ABI:** `Modulus.Modding.Api` is the only guaranteed-stable assembly
- **ALC isolation:** Each mod loads in its own AssemblyLoadContext
- **Event hooks, not inline logic:** Minimize changes to Stride core files
- **Managed C# only:** No native DLLs in mods

## Build System

- **Solution:** `build/Stride.sln` (full) or `build/Stride.Runtime.slnf` (fast subset)
- **Target:** .NET 10
- **Platform:** Windows required for editor and asset pipeline
- **CI:** GitHub Actions on Windows

## Common Pitfalls

- Stride's ECS is not thread-safe — marshal work to the game thread
- Asset compilation requires `AssetCompiler.exe` as external process
- Use solution filters for faster builds during development
- The editor uses WPF, not WinUI or Avalonia
