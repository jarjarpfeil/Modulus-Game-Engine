// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Modulus.Modding.Api;
using Stride.Core;
using Stride.Core.Diagnostics;

namespace TestSystemReplacer;

/// <summary>
/// Marker interface for a replaceable service. In a real mod, this would be
/// something like PhysicsSystem or AudioSystem. For testing, we use a simple
/// marker to prove the replacement pattern works.
/// </summary>
public interface ITestReplaceableService
{
    bool WasReplaced { get; }
    string Name { get; }
}

/// <summary>
/// Default implementation that the engine pre-registers.
/// </summary>
public class DefaultTestService : ITestReplaceableService
{
    public bool WasReplaced => false;
    public string Name => "Default";
}

/// <summary>
/// Mod's replacement implementation.
/// </summary>
public class ModTestService : ITestReplaceableService
{
    public bool WasReplaced => true;
    public string Name => "ModReplaced";
}

/// <summary>
/// Entry point for the System Replacer test mod.
/// Proves that mods can replace core engine systems via the service registry.
/// </summary>
public class ModEntry : IMod
{
    public string Id => "com.modulus.test.system-replacer";
    public string Name => "Test System Replacer";
    public Version Version => new Version(1, 0, 0);
    public Version MinApiVersion => new Version(1, 0, 0);

    private IModContext? _context;
    private ITestReplaceableService? _originalService;

    public void Initialize(IModContext context)
    {
        _context = context;

        // Save the original service so we can restore it on unload
        _originalService = context.Services.GetService<ITestReplaceableService>();

        // Replace with our mod's implementation
        var replacement = new ModTestService();
        context.Services.AddService<ITestReplaceableService>(replacement);

        context.Logger.Info("Test service replaced with mod implementation");
    }

    public void OnEnabled()
    {
        _context?.Logger.Info("System Replacer mod enabled");
    }

    public void OnDisabled()
    {
        // Restore the original service
        if (_originalService != null && _context != null)
        {
            _context.Services.AddService<ITestReplaceableService>(_originalService);
            _context.Logger.Info("Original test service restored");
        }
    }
}
