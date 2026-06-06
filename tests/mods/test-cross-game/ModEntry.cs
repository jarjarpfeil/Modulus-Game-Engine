// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Modulus.Modding.Api;
using Stride.Core.Diagnostics;

namespace TestCrossGame;

/// <summary>
/// Death event published via the mod event bus when an entity dies.
/// </summary>
public record EntityDiedEvent(Guid EntityId, string ComponentType);

/// <summary>
/// Entry point for the Cross-Game test mod.
/// Proves that a single mod works identically across different game projects.
/// </summary>
public class ModEntry : IMod
{
    public string Id => "com.modulus.test.cross-game";
    public string Name => "Test Cross-Game Mod";
    public Version Version => new Version(1, 0, 0);
    public Version MinApiVersion => new Version(1, 0, 0);

    private IModContext? _context;
    private int _eventSubscribeCount;

    public void Initialize(IModContext context)
    {
        _context = context;

        // Subscribe to events — proves event bus works across ALC boundary
        context.EventBus.Subscribe<EntityDiedEvent>(OnEntityDied);

        context.Logger.Info("Cross-Game mod initialized — health system active");
    }

    public void OnEnabled()
    {
        _context?.Logger.Info("Cross-Game mod enabled");
    }

    public void OnDisabled()
    {
        _context?.Logger.Info("Cross-Game mod disabled");
    }

    private void OnEntityDied(EntityDiedEvent evt)
    {
        _eventSubscribeCount++;
        _context?.Logger.Info($"Entity {evt.EntityId} died ({evt.ComponentType})");
    }

    /// <summary>For test verification: how many death events were received.</summary>
    public int EventSubscribeCount => _eventSubscribeCount;
}
