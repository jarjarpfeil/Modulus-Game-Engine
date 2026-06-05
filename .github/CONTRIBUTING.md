# 🤝 Contributing

We welcome contributions to Modulus Engine! This project is a fork of [Stride](https://stride3d.net/) with a focus on modding support.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR-USERNAME/modulus.git`
3. Create a feature branch: `git checkout -b feature/your-feature`
4. Make your changes
5. Submit a pull request

## Development Setup

### Prerequisites

- .NET 10 SDK
- Windows (required for Game Studio editor)
- Visual Studio 2022 or later (recommended)

### Building

```bash
dotnet build build/Stride.sln
```

### Running Tests

```bash
dotnet test build/Stride.Tests.Simple.slnf
```

## Code Guidelines

- Follow existing Stride coding conventions
- Use clear, self-documenting code (no `#region` directives)
- Add XML documentation for public APIs
- Include tests for new features

## What to Work On

Check out our [Issues](https://github.com/Modulus-Engine/modulus/issues) for tasks. Issues tagged with **`good first issue`** are great starting points.

### Priority Areas

- Modding API design and implementation
- AssemblyLoadContext isolation
- Editor integration for mod management
- Cross-game mod compatibility
- Documentation and examples

## Pull Request Process

1. Ensure your code builds with zero errors
2. Run existing tests to check for regressions
3. Update documentation if needed
4. Fill out the PR template completely
5. Request review from maintainers

## Questions?

- Open a [GitHub Discussion](https://github.com/Modulus-Engine/modulus/discussions)
- Join our Discord (coming soon)

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE.md).
